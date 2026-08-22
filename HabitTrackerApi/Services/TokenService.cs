namespace Services;
using Microsoft.IdentityModel.Tokens;
using Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Security.Cryptography;


public class TokenService
{

    private readonly IConfiguration _configuration;

    // 2FA akışında, şifre doğrulandıktan sonra asıl JWT yerine verilen, sadece
    // /api/auth/2fa/login uç noktasında kullanılabilecek kısa ömürlü ön-doğrulama
    // token'ı için claim adı.
    private const string TwoFactorPurposeClaim = "purpose";
    private const string TwoFactorPurposeValue = "2fa-pending";
    private static readonly TimeSpan PreAuthTokenLifetime = TimeSpan.FromMinutes(5);

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            // YENİ: Identity, şifre değiştiğinde / 2FA açılıp kapandığında /
            // ResetPasswordAsync çağrıldığında SecurityStamp'i otomatik olarak
            // yeniler. Bu claim'i JWT'ye gömüp Program.cs'deki OnTokenValidated
            // event'inde kullanıcının GÜNCEL SecurityStamp'i ile karşılaştırarak,
            // önceden verilmiş access token'ları anında (süresi dolmadan) geçersiz
            // kılabiliyoruz. Böylece "şifre değiştir" işlemi artık gerçekten
            // önceki oturumları sonlandırıyor.
            new Claim("sstamp", user.SecurityStamp ?? string.Empty)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials
);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }

    /// <summary>
    /// YENİ: Refresh token'lar artık veritabanında düz metin olarak saklanmıyor.
    /// DB sızıntısı durumunda token'ların doğrudan kullanılabilir olmasını
    /// önlemek için SHA-256 hash'i saklanıyor; istemciye verilen ham (random,
    /// 64 byte'lık) değer hiçbir zaman veritabanına yazılmıyor. Token zaten
    /// yüksek entropili rastgele bir değer olduğundan salt'a gerek yok
    /// (brute-force/rainbow-table pratik değil).
    /// </summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// YENİ: Şifresi doğrulanmış ama henüz 2FA kodunu girmemiş bir kullanıcı için
    /// kısa ömürlü (5 dk), sadece "purpose=2fa-pending" claim'i taşıyan bir token
    /// üretir. Bu token normal Authorize akışlarında kullanılamaz — hem
    /// ValidatePreAuthTokenAndGetUserId hem de artık (2FA bypass açığını
    /// kapatmak için) Program.cs'deki JWT Bearer OnTokenValidated event'i bu
    /// claim'i kontrol edip reddediyor; asıl erişim token'ı sadece kod
    /// doğrulandıktan sonra GenerateToken ile verilir.
    /// </summary>
    public string GeneratePreAuthToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(TwoFactorPurposeClaim, TwoFactorPurposeValue)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(PreAuthTokenLifetime),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// YENİ: GeneratePreAuthToken ile üretilmiş bir token'ı doğrular ve içindeki
    /// kullanıcı Id'sini döner. Token geçersiz, süresi dolmuş veya amacı
    /// "2fa-pending" değilse null döner.
    /// </summary>
    public string? ValidatePreAuthTokenAndGetUserId(string preAuthToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidAudience = _configuration["Jwt:Audience"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var principal = handler.ValidateToken(preAuthToken, parameters, out _);
            var purpose = principal.FindFirstValue(TwoFactorPurposeClaim);
            if (purpose != TwoFactorPurposeValue)
            {
                return null;
            }

            return principal.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}