namespace Services;
using Microsoft.IdentityModel.Tokens;
using Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Configuration;
using System.Text;
using System.Security.Cryptography;


public class TokenService
{
    private readonly IConfiguration _configuration;
    private readonly JwtOptions _jwtOptions;

    private const string TwoFactorPurposeClaim = "purpose";
    private const string TwoFactorPurposeValue = "2fa-pending";

    // DÜZELTİLDİ (madde 8): PreAuthTokenLifetime artık sabit değil,
    // JwtOptions.PreAuthTokenLifetimeMinutes üzerinden konfigüre ediliyor.
    private TimeSpan PreAuthTokenLifetime => TimeSpan.FromMinutes(_jwtOptions.PreAuthTokenLifetimeMinutes);

    // DÜZELTİLDİ (madde 8): Access token ömrü artık JwtOptions'tan geliyor.
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_jwtOptions.AccessTokenLifetimeMinutes);

    // DÜZELTİLDİ (madde 8): Refresh token ömrü artık JwtOptions'tan geliyor;
    // AuthController bu değeri IssueTokensAsync/Refresh içinde kullanıyor.
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_jwtOptions.RefreshTokenLifetimeDays);

    public TokenService(IConfiguration configuration, IOptions<JwtOptions> jwtOptions)
    {
        _configuration = configuration;
        _jwtOptions = jwtOptions.Value;
    }

    public string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim("sstamp", user.SecurityStamp ?? string.Empty)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(AccessTokenLifetime),
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

    
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    
    public string GeneratePreAuthToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(TwoFactorPurposeClaim, TwoFactorPurposeValue),
            new Claim("sstamp", user.SecurityStamp ?? string.Empty)
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

    public (string? UserId, string? SecurityStamp) ValidatePreAuthTokenAndGetUserId(string preAuthToken)
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
                return (null, null);
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var securityStamp = principal.FindFirstValue("sstamp");
            return (userId, securityStamp);
        }
        catch (SecurityTokenException)
        {
            return (null, null);
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
    }
}