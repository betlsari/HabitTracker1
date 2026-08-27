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
using Filters;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly TokenService _tokenService;
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;
    private readonly BookCoverStorageService _coverStorage;
    private readonly AuthAuditService _auditService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        TokenService tokenService,
        AppDbContext context,
        EmailService emailService,
        BookCoverStorageService coverStorage,
        AuthAuditService auditService,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _context = context;
        _emailService = emailService;
        _coverStorage = coverStorage;
        _auditService = auditService;
        _logger = logger;
    }

    // =========================================================
    // REGISTER
    // =========================================================

    [HttpPost("register")]
    [EnableRateLimiting("AuthPolicy")]
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
            var onlyDuplicateEmailErrors = result.Errors.All(e =>
                e.Code == nameof(IdentityErrorDescriber.DuplicateUserName) ||
                e.Code == nameof(IdentityErrorDescriber.DuplicateEmail));

            await _auditService.LogAsync(
                AuthAuditEventTypes.Register,
                registerDto.Email,
                succeeded: false,
                detail: onlyDuplicateEmailErrors ? "Duplicate email" : "Validation failed");

            if (onlyDuplicateEmailErrors)
            {
                return Ok("Eğer bu email adresi kullanılabiliyorsa, kayıt oluşturuldu ve doğrulama emaili gönderildi.");
            }

            return BadRequest(result.Errors);
        }

        await _auditService.LogAsync(AuthAuditEventTypes.Register, registerDto.Email, succeeded: true, userId: user.Id);

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await SendEmailSafeAsync(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}");

        return Ok("Eğer bu email adresi kullanılabiliyorsa, kayıt oluşturuldu ve doğrulama emaili gönderildi.");
    }

    // =========================================================
    // LOGIN
    // =========================================================

    [HttpPost("login")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        var user = await _userManager.FindByEmailAsync(loginDto.Email);

        if (user == null)
        {
            await _auditService.LogAsync(
                AuthAuditEventTypes.LoginFailed, loginDto.Email, succeeded: false, detail: "Unknown email");
            return Unauthorized();
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            var eventType = result.IsLockedOut
                ? AuthAuditEventTypes.LoginLockedOut
                : AuthAuditEventTypes.LoginFailed;

            await _auditService.LogAsync(
                eventType, loginDto.Email, succeeded: false, userId: user.Id,
                detail: result.IsLockedOut ? "Account locked out" : "Invalid password");

            return Unauthorized();
        }

        await _auditService.LogAsync(AuthAuditEventTypes.LoginSucceeded, loginDto.Email, succeeded: true, userId: user.Id);

        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);

        return Ok(new { Token = accessToken, RefreshToken = refreshTokenValue });
    }

    // =========================================================
    // REFRESH
    // =========================================================

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenDto refreshTokenDto)
    {
        var hashedIncoming = TokenService.HashToken(refreshTokenDto.RefreshToken);

        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == hashedIncoming);

        if (storedToken == null)
        {
            return Unauthorized();
        }

        if (storedToken.RevokedAt != null)
        {
            await _auditService.LogAsync(
                AuthAuditEventTypes.RefreshTokenReused,
                storedToken.User?.Email ?? "unknown",
                succeeded: false,
                userId: storedToken.UserId,
                detail: "Revoked refresh token was reused — all sessions revoked");

            await RevokeAllRefreshTokensAsync(storedToken.UserId);

            return Unauthorized("Oturumunuz güvenlik nedeniyle sonlandırıldı. Lütfen tekrar giriş yapın.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized();
        }

        var newAccessToken = _tokenService.GenerateToken(storedToken.User!);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // Eski refresh token artık kullanılamaz.
        storedToken.RevokedAt = DateTime.UtcNow;

        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = TokenService.HashToken(newRefreshToken),
            UserId = storedToken.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime),
            RevokedAt = null,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });

        await _context.SaveChangesAsync();

        return Ok(new { Token = newAccessToken, RefreshToken = newRefreshToken });
    }

    // =========================================================
    // LOGOUT
    // =========================================================

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

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

        await _auditService.LogAsync(
            AuthAuditEventTypes.Logout,
            User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
            succeeded: true,
            userId: userId);

        return Ok(new { message = "Çıkış yapıldı." });
    }

    // =========================================================
    // LOGOUT ALL
    // =========================================================

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var revokedCount = await RevokeAllRefreshTokensAsync(userId);

        await _auditService.LogAsync(
            AuthAuditEventTypes.LogoutAll,
            User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
            succeeded: true,
            userId: userId,
            detail: $"Revoked {revokedCount} sessions");

        return Ok(new { message = "Tüm oturumlar kapatıldı.", revokedCount });
    }

    // =========================================================
// CONFIRM EMAIL
// =========================================================
// DÜZELTİLDİ: GET + query string yerine POST + body kullanılıyor.
// Sebep: (1) token artık URL'de taşınmıyor, proxy/server access log'larına
// sızma riski ortadan kalkıyor. (2) GET endpoint'lere rate limit filtresi
// (AuthPolicy) eklemek anlamlıydı ama token zaten URL'de göründüğü için asıl
// sorunu çözmüyordu; POST'a geçince ikisi birlikte çözülüyor.
[HttpPost("confirm-email")]
[EnableRateLimiting("AuthPolicy")]
public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto dto)
{
    var user = await _userManager.FindByEmailAsync(dto.Email);

    if (user == null)
    {
        return NotFound();
    }

    var result = await _userManager.ConfirmEmailAsync(user, dto.Token);

    if (!result.Succeeded)
    {
        await _auditService.LogAsync(AuthAuditEventTypes.EmailConfirmationFailed, dto.Email, succeeded: false, userId: user.Id);
        return BadRequest(result.Errors);
    }

    await _auditService.LogAsync(AuthAuditEventTypes.EmailConfirmed, dto.Email, succeeded: true, userId: user.Id);

    return Ok("Email doğrulandı.");
}
    // =========================================================
    // RESEND CONFIRMATION
    // =========================================================

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            await SendEmailSafeAsync(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}");
        }

        // Kullanıcı enumeration saldırısını önlemek için her durumda aynı cevap dönüyor.
        return Ok("Eğer bu email adresi kayıtlıysa ve doğrulanmamışsa, yeni bir doğrulama kodu gönderildi.");
    }

    // =========================================================
    // FORGOT PASSWORD
    // =========================================================

    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        if (user == null)
        {
            return Ok("Eğer bu email adresi kayıtlıysa, şifre sıfırlama linki gönderilecektir.");
        }

        await _auditService.LogAsync(AuthAuditEventTypes.ForgotPasswordRequested, dto.Email, succeeded: true, userId: user.Id);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        await SendEmailSafeAsync(user.Email!, "Şifre Sıfırlama", $"Şifre sıfırlama kodunuz: {token}");

        return Ok("Eğer bu email kayıtlıysa, sıfırlama linki gönderildi.");
    }

    // =========================================================
    // RESET PASSWORD
    // =========================================================

    [HttpPost("reset-password")]
    [EnableRateLimiting("AuthPolicy")]
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
            await _auditService.LogAsync(AuthAuditEventTypes.PasswordResetFailed, dto.Email, succeeded: false, userId: user.Id);
            return BadRequest(result.Errors);
        }

        await _auditService.LogAsync(AuthAuditEventTypes.PasswordReset, dto.Email, succeeded: true, userId: user.Id);

        await RevokeAllRefreshTokensAsync(user.Id);

        await SendEmailSafeAsync(
            user.Email!,
            "Şifreniz değiştirildi",
            "Hesabınızın şifresi değiştirildi. Bu işlemi siz yapmadıysanız derhal destek ekibiyle iletişime geçin.");

        return Ok("Şifre başarıyla sıfırlandı.");
    }

    // =========================================================
    // CHANGE PASSWORD
    // =========================================================

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return NotFound();
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(user, dto.CurrentPassword, lockoutOnFailure: true);

        if (!passwordResult.Succeeded)
        {
            await _auditService.LogAsync(
                AuthAuditEventTypes.PasswordChangeFailed, user.Email!, succeeded: false, userId: user.Id,
                detail: passwordResult.IsLockedOut ? "Locked out" : "Wrong current password");

            if (passwordResult.IsLockedOut)
            {
                return BadRequest("Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin.");
            }

            return BadRequest("Mevcut şifre hatalı.");
        }

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded)
        {
            await _auditService.LogAsync(AuthAuditEventTypes.PasswordChangeFailed, user.Email!, succeeded: false, userId: user.Id);
            return BadRequest(result.Errors);
        }

        await _auditService.LogAsync(AuthAuditEventTypes.PasswordChanged, user.Email!, succeeded: true, userId: user.Id);

        await RevokeAllRefreshTokensAsync(user.Id);

        await SendEmailSafeAsync(
            user.Email!,
            "Şifreniz değiştirildi",
            "Hesabınızın şifresi değiştirildi. Bu işlemi siz yapmadıysanız derhal destek ekibiyle iletişime geçin.");

        return Ok("Şifre başarıyla değiştirildi.");
    }

    // =========================================================
    // GET CURRENT USER
    // =========================================================

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            user.TotalXp,
            user.FocusXpPool,
            user.TimeZoneId
        });
    }

    // =========================================================
    // UPDATE PROFILE
    // =========================================================

    [HttpPut("me/profile")]
    [Authorize]
    [SanitizeText]
    public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (user == null)
        {
            return NotFound();
        }

        user.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(dto.AvatarUrl) ? null : dto.AvatarUrl.Trim();

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(new
        {
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            user.TotalXp,
            user.FocusXpPool,
            user.TimeZoneId
        });
    }

    // =========================================================
    // DELETE ACCOUNT
    // =========================================================

    [HttpDelete("me")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return NotFound();
        }

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(user, dto.CurrentPassword, lockoutOnFailure: true);

        if (!passwordResult.Succeeded)
        {
            await _auditService.LogAsync(AuthAuditEventTypes.AccountDeleteFailed, user.Email!, succeeded: false, userId: user.Id);

            if (passwordResult.IsLockedOut)
            {
                return BadRequest("Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin.");
            }

            return BadRequest("Şifre hatalı. Hesap silme işlemi iptal edildi.");
        }

        await _coverStorage.DeleteAllCoversForUserAsync(userId);

        var result = await _userManager.DeleteAsync(user);

        if (!result.Succeeded)
        {
            await _auditService.LogAsync(AuthAuditEventTypes.AccountDeleteFailed, user.Email!, succeeded: false, userId: userId);
            return BadRequest(result.Errors);
        }

        await _auditService.LogAsync(AuthAuditEventTypes.AccountDeleted, user.Email!, succeeded: true, userId: userId);

        return Ok(new { message = "Hesabınız ve tüm ilişkili verileriniz kalıcı olarak silindi." });
    }

    // =========================================================
    // USER LEVEL
    // =========================================================

    [HttpGet("me/level")]
    [Authorize]
    public async Task<ActionResult<UserLevelDto>> GetMyLevel()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return NotFound();
        }

        var (level, currentLevelXp, xpForNextLevel, progress) = UserLevelService.GetLevelProgress(user.TotalXp);

        return Ok(new UserLevelDto
        {
            TotalXp = user.TotalXp,
            Level = level,
            CurrentLevelXp = currentLevelXp,
            XpForNextLevel = xpForNextLevel,
            ProgressPercent = progress
        });
    }

    // =========================================================
    // MY AUDIT LOG
    // =========================================================

    [HttpGet("me/audit-log")]
    [Authorize]
    public async Task<IActionResult> GetMyAuditLog(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var query = _context.AuthAuditEvents
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.EventType,
                a.Succeeded,
                a.IpAddress,
                a.Detail,
                a.CreatedAt
            })
            .ToListAsync();

        return Ok(new { items, page, pageSize, totalCount });
    }

    // =========================================================
    // UPDATE TIMEZONE
    // =========================================================

    [HttpPut("me/timezone")]
    [Authorize]
    public async Task<IActionResult> UpdateTimezone(UpdateTimezoneDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!TimeZones.IsValid(dto.TimeZoneId))
        {
            return BadRequest("Geçersiz saat dilimi kimliği (örn: 'Europe/Istanbul').");
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return NotFound();
        }

        user.TimeZoneId = dto.TimeZoneId;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(new
        {
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            user.TotalXp,
            user.FocusXpPool,
            user.TimeZoneId
        });
    }

    // =========================================================
    // PRIVATE HELPERS
    // =========================================================

    private async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(User user)
    {
        var accessToken = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = TokenService.HashToken(refreshToken),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime),
            RevokedAt = null,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });

        await _context.SaveChangesAsync();

        return (accessToken, refreshToken);
    }

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

    private async Task SendEmailSafeAsync(string toEmail, string subject, string body)
    {
        try
        {
            await _emailService.SendEmailAsync(toEmail, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email gönderilemedi. To={To} Subject={Subject}", toEmail, subject);
        }
    }
}