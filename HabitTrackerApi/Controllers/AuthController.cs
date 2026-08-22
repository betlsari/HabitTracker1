using Microsoft.AspNetCore.Identity;
using Models;
using Dtos;
using Microsoft.AspNetCore.Mvc;
using Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthPolicy")] 
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly TokenService _tokenService;
    private readonly EmailService _emailService;

    private readonly AppDbContext _context;

    public AuthController(UserManager<User> userManager, SignInManager<User> signInManager, TokenService tokenService, EmailService emailService, AppDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _emailService = emailService;
        _context = context;
    }


    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto registerDto)
    {
        var user = new User
        {
            UserName = registerDto.Email,
            Email = registerDto.Email,
            CreatedAt = DateTime.UtcNow
        };
        var result = await _userManager.CreateAsync(user, registerDto.Password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailAsync(user.Email, "Email Doğrulama", $"Doğrulama kodunuz: {token}");
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        var user = await _userManager.FindByEmailAsync(loginDto.Email);
        if (user == null)
        {
            return Unauthorized();
        }
        var result = await _signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsNotAllowed)
            {
                return BadRequest("Email adresiniz doğrulanmamış. Lütfen email adresinizi doğrulayın.");
            }
            if (result.IsLockedOut)
            {
                return BadRequest("Çok fazla başarısız giriş denemesi. Hesabınız geçici olarak kilitlendi, lütfen daha sonra tekrar deneyin.");
            }
            return Unauthorized();
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var preAuthToken = _tokenService.GeneratePreAuthToken(user);
            return Ok(new
            {
                RequiresTwoFactor = true,
                PreAuthToken = preAuthToken
            });
        }

        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);
        return Ok(new { RequiresTwoFactor = false, Token = accessToken, RefreshToken = refreshTokenValue });
    }

    
    [HttpPost("2fa/login")]
    public async Task<IActionResult> TwoFactorLogin(TwoFactorLoginDto dto)
    {
        var userId = _tokenService.ValidatePreAuthTokenAndGetUserId(dto.PreAuthToken);
        if (userId == null)
        {
            return Unauthorized("Oturum süresi dolmuş veya geçersiz. Lütfen tekrar giriş yapın.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return Unauthorized();
        }

        var codeValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, dto.Code);

        if (!codeValid)
        {
            // Kurtarma kodlarını da kabul et (authenticator cihazı kayıpsa).
            var recoveryValid = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, dto.Code);
            if (!recoveryValid.Succeeded)
            {
                return BadRequest("Doğrulama kodu hatalı.");
            }
        }

        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);
        return Ok(new { Token = accessToken, RefreshToken = refreshTokenValue });
    }

    
    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new { Enabled = user.TwoFactorEnabled });
    }

  
    [HttpPost("2fa/setup")]
    [Authorize]
    public async Task<IActionResult> SetupTwoFactor()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);

        const string issuer = "HabitTracker";
        var label = user.Email ?? user.UserName ?? user.Id;
        var authenticatorUri =
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(label)}" +
            $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        return Ok(new { SharedKey = unformattedKey, AuthenticatorUri = authenticatorUri });
    }

    
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactor(EnableTwoFactorDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        if (user.TwoFactorEnabled)
        {
            return BadRequest("İki adımlı doğrulama zaten etkin.");
        }

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, dto.Code);
        if (!isValid)
        {
            return BadRequest("Doğrulama kodu hatalı. Authenticator uygulamanızdaki güncel kodu girin.");
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        return Ok(new
        {
            message = "İki adımlı doğrulama etkinleştirildi. Kurtarma kodlarınızı güvenli bir yerde saklayın; her biri yalnızca bir kez kullanılabilir.",
            recoveryCodes
        });
    }

    
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor(DisableTwoFactorDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, dto.CurrentPassword);
        if (!passwordValid)
        {
            return BadRequest("Şifre hatalı.");
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);

        // YENİ: 2FA kapatmak hesabın koruma seviyesini düşürdüğü için, bu işlemi
        // gerçekten hesap sahibinin yaptığından emin olunsa da (şifre doğrulandı),
        // güvenlik hijyeni gereği diğer tüm cihazlardaki refresh token'lar iptal
        // ediliyor. Access token'lar da ResetAuthenticatorKeyAsync/SetTwoFactorEnabledAsync
        // SecurityStamp'i değiştirdiği için sstamp kontrolüyle otomatik geçersiz olur.
        await RevokeAllRefreshTokensAsync(user.Id);

        return Ok(new { message = "İki adımlı doğrulama devre dışı bırakıldı." });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenDto refreshTokenDto)
    {
        // DÜZELTİLDİ: Refresh token'lar artık DB'de hash'lenmiş saklanıyor;
        // gelen ham değer hash'lenip öyle aranıyor (bkz. TokenService.HashToken).
        var hashedIncoming = TokenService.HashToken(refreshTokenDto.RefreshToken);
        var storedToken = await _context.RefreshTokens
        .Include(rt => rt.User)
        .FirstOrDefaultAsync(rt => rt.Token == hashedIncoming);
        if (storedToken == null || storedToken.ExpiresAt < DateTime.UtcNow || storedToken.RevokedAt != null)
        {
            return Unauthorized();
        }
        var newToken = _tokenService.GenerateToken(storedToken.User!);
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        storedToken.RevokedAt = DateTime.UtcNow;
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = TokenService.HashToken(newRefreshToken),
            UserId = storedToken.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null
        });
        await _context.SaveChangesAsync();
        return Ok(new { Token = newToken, RefreshToken = newRefreshToken });
    }

    
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var hashedIncoming = TokenService.HashToken(dto.RefreshToken);
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == hashedIncoming && rt.UserId == userId);

        if (storedToken == null)
        {
            return NotFound();
        }

        if (storedToken.RevokedAt == null)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Çıkış yapıldı." });
    }

    
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var revokedCount = await RevokeAllRefreshTokensAsync(userId);
        return Ok(new { message = "Tüm oturumlar kapatıldı.", revokedCount });
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string email, string token)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return NotFound();
        }
        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }
        return Ok("Email doğrulandı.");
    }

    
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailService.SendEmailAsync(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}");
        }

        return Ok("Eğer bu email adresi kayıtlıysa ve doğrulanmamışsa, yeni bir doğrulama kodu gönderildi.");
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Ok("Eğer bu email adresi kayıtlıysa, şifre sıfırlama linki gönderilecektir.");
        }
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _emailService.SendEmailAsync(user.Email, "Şifre Sıfırlama", $"Şifre sıfırlama kodunuz: {token}");

        return Ok("Eğer bu email kayıtlıysa, sıfırlama linki gönderildi.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return BadRequest("Kullanıcı bulunamadı.");
        }
        var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // YENİ: Şifre sıfırlandığında, hesabı ele geçirmiş olabilecek biri
        // tarafından açılmış tüm oturumlar (refresh token'lar) iptal edilir.
        // Access token'lar da ResetPasswordAsync'in yenilediği SecurityStamp
        // sayesinde sstamp kontrolüyle otomatik geçersiz olur.
        await RevokeAllRefreshTokensAsync(user.Id);

        return Ok("Şifre başarıyla sıfırlandı.");
    }

    
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // YENİ: Şifre değiştiğinde tüm refresh token'lar iptal edilir. Access
        // token'lar da ChangePasswordAsync'in otomatik yenilediği SecurityStamp
        // sayesinde sstamp kontrolüyle anında geçersiz olur — hesabı ele geçirmiş
        // biri şifre değiştirilince artık gerçekten dışarı atılmış olur (önceden
        // JWT süresi dolana kadar, 2 saate kadar, geçerliliğini koruyordu).
        await RevokeAllRefreshTokensAsync(user.Id);

        return Ok("Şifre başarıyla değiştirildi.");
    }

    [HttpPut("timezone")]
    [Authorize]
    public async Task<IActionResult> UpdateTimezone(UpdateTimezoneDto dto)
    {
        if (!TimeZones.IsValid(dto.TimeZoneId))
        {
            return BadRequest("Geçersiz zaman dilimi.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        user.TimeZoneId = dto.TimeZoneId;
        await _userManager.UpdateAsync(user);
        return Ok(new { user.TimeZoneId });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }
        return Ok(new { user.Email, user.TotalXp, user.TimeZoneId, user.TwoFactorEnabled });
    }

    [HttpDelete("me")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, dto.CurrentPassword);
        if (!passwordValid)
        {
            return BadRequest("Şifre hatalı. Hesap silme işlemi iptal edildi.");
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(new { message = "Hesabınız ve tüm ilişkili verileriniz kalıcı olarak silindi." });
    }

    // Login ve 2fa/login uç noktalarında tekrarlanan token+refresh token
    // üretme/kaydetme mantığını tek yerde topluyor.
    private async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(User user)
    {
        var token = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        _context.RefreshTokens.Add(new RefreshToken
        {
            // DÜZELTİLDİ: Ham değer yerine hash saklanıyor; DB sızıntısında
            // token'lar doğrudan kullanılamaz. İstemciye dönen değer (refreshToken)
            // hiçbir zaman veritabanına yazılmıyor.
            Token = TokenService.HashToken(refreshToken),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null
        });
        await _context.SaveChangesAsync();
        return (token, refreshToken);
    }

    // YENİ: change-password, reset-password, 2fa/disable ve logout-all
    // arasında tekrarlanan "kullanıcının tüm aktif refresh token'larını iptal
    // et" mantığını tek yerde topluyor. Revoke edilen token sayısını döner.
    private async Task<int> RevokeAllRefreshTokensAsync(string userId)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        if (activeTokens.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return activeTokens.Count;
    }
}