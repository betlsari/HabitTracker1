// HabitTrackerApi/Controllers/AuthController.cs
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
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly TokenService _tokenService;
    private readonly EmailService _emailService;
    private readonly AuthAuditService _authAudit;
    private readonly ILogger<AuthController> _logger;

    private readonly AppDbContext _context;

    private readonly IEmailQueue _emailQueue;
    private readonly TwoFactorLockoutService _twoFactorLockout;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        TokenService tokenService,
        EmailService emailService,
        IEmailQueue emailQueue,
        AuthAuditService authAudit,
        AppDbContext context,
        TwoFactorLockoutService twoFactorLockout,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _emailService = emailService;
        _emailQueue = emailQueue;
        _authAudit = authAudit;
        _context = context;
        _twoFactorLockout = twoFactorLockout;
        _logger = logger;
    }

    [HttpPost("2fa/login")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> TwoFactorLogin(TwoFactorLoginDto dto)
    {
        var userId = _tokenService.ValidatePreAuthTokenAndGetUserId(dto.PreAuthToken);
        if (userId == null)
        {
            await _authAudit.RecordAsync(HttpContext, "two-factor-login", false, detail: "invalid-preauth-token");
            return Unauthorized("Oturum süresi dolmuş veya geçersiz. Lütfen tekrar giriş yapın.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !await _userManager.GetTwoFactorEnabledAsync(user))
        {
            await _authAudit.RecordAsync(HttpContext, "two-factor-login", false, user, detail: "two-factor-not-enabled");
            return Unauthorized();
        }

        if (await _twoFactorLockout.IsLockedOutAsync(userId))
        {
            await _authAudit.RecordAsync(HttpContext, "two-factor-login", false, user, detail: "2fa-locked-out");
            return BadRequest("Çok fazla başarısız 2FA denemesi. Lütfen daha sonra tekrar deneyin.");
        }

        var codeValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, dto.Code);

        if (!codeValid)
        {
            var recoveryValid = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, dto.Code);
            if (!recoveryValid.Succeeded)
            {
                await _twoFactorLockout.RecordFailureAsync(userId);
                await _authAudit.RecordAsync(HttpContext, "two-factor-login", false, user, detail: "invalid-code");
                return BadRequest("Doğrulama kodu hatalı.");
            }
        }

        await _twoFactorLockout.ResetAsync(userId);
        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);
        await _authAudit.RecordAsync(HttpContext, "two-factor-login", true, user);
        return Ok(new { Token = accessToken, RefreshToken = refreshTokenValue });
    }

    // DÜZELTİLDİ: 2FA kurulum başlatma (secret/QR üretimi) artık audit
    // trail'e yazılıyor. Kurulum tek başına 2FA'yı etkinleştirmez ama bir
    // hesapta authenticator secret'inin sıfırlanmasını (ResetAuthenticatorKeyAsync)
    // içerir; login/2fa-login/email-change ile tutarlı olması için izleniyor.
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

        await _authAudit.RecordAsync(HttpContext, "two-factor-setup", true, user);

        return Ok(new { SharedKey = unformattedKey, AuthenticatorUri = authenticatorUri });
    }

    // DÜZELTİLDİ: 2FA etkinleştirme başarılı/başarısız denemeleri artık
    // audit trail'e yazılıyor.
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

        if (await _twoFactorLockout.IsLockedOutAsync(userId!))
        {
            return BadRequest("Çok fazla başarısız 2FA denemesi. Lütfen daha sonra tekrar deneyin.");
        }

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, dto.Code);
        if (!isValid)
        {
            await _twoFactorLockout.RecordFailureAsync(userId!);
            await _authAudit.RecordAsync(HttpContext, "two-factor-enable", false, user, detail: "invalid-code");
            return BadRequest("Doğrulama kodu hatalı. Authenticator uygulamanızdaki güncel kodu girin.");
        }

        await _twoFactorLockout.ResetAsync(userId!);
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        await _authAudit.RecordAsync(HttpContext, "two-factor-enable", true, user);

        return Ok(new
        {
            message = "İki adımlı doğrulama etkinleştirildi. Kurtarma kodlarınızı güvenli bir yerde saklayın; her biri yalnızca bir kez kullanılabilir.",
            recoveryCodes
        });
    }

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

            if (onlyDuplicateEmailErrors)
            {
                return Ok("Eğer bu email adresi kullanılabiliyorsa, kayıt oluşturuldu ve doğrulama emaili gönderildi.");
            }

            return BadRequest(result.Errors);
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        _emailQueue.Enqueue(new EmailMessage(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}"));
        return Ok("Eğer bu email adresi kullanılabiliyorsa, kayıt oluşturuldu ve doğrulama emaili gönderildi.");
    }

    [HttpPost("login")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        var user = await _userManager.FindByEmailAsync(loginDto.Email);
        if (user == null)
        {
            await _authAudit.RecordAsync(HttpContext, "login", false, email: loginDto.Email, detail: "unknown-user");
            return Unauthorized();
        }
        var result = await _signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsNotAllowed)
            {
                await _authAudit.RecordAsync(HttpContext, "login", false, user, detail: "email-not-confirmed");
                return BadRequest("Email adresiniz doğrulanmamış. Lütfen email adresinizi doğrulayın.");
            }
            if (result.IsLockedOut)
            {
                await _authAudit.RecordAsync(HttpContext, "login-lockout", false, user, detail: "lockout");
                return BadRequest("Çok fazla başarısız giriş denemesi. Hesabınız geçici olarak kilitlendi, lütfen daha sonra tekrar deneyin.");
            }
            await _authAudit.RecordAsync(HttpContext, "login", false, user, detail: "invalid-password");
            return Unauthorized();
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            await _authAudit.RecordAsync(HttpContext, "login-password", true, user, detail: "two-factor-required");
            var preAuthToken = _tokenService.GeneratePreAuthToken(user);
            return Ok(new
            {
                RequiresTwoFactor = true,
                PreAuthToken = preAuthToken
            });
        }

        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);
        await _authAudit.RecordAsync(HttpContext, "login", true, user);
        return Ok(new { RequiresTwoFactor = false, Token = accessToken, RefreshToken = refreshTokenValue });
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

    // DÜZELTİLDİ: 2FA devre dışı bırakma başarılı/başarısız denemeleri
    // artık audit trail'e yazılıyor. 2FA'yı kapatmak hesabın güvenlik
    // seviyesini düşüren en hassas işlemlerden biri olduğu için önemli.
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
            await _authAudit.RecordAsync(HttpContext, "two-factor-disable", false, user, detail: "invalid-password");
            return BadRequest("Şifre hatalı.");
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);

        await RevokeAllRefreshTokensAsync(user.Id);

        await _authAudit.RecordAsync(HttpContext, "two-factor-disable", true, user);

        return Ok(new { message = "İki adımlı doğrulama devre dışı bırakıldı." });
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("AuthPolicy")]
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
            var revokedCount = await RevokeAllRefreshTokensAsync(storedToken.UserId);
            _logger.LogWarning(
                "Refresh token reuse tespit edildi. UserId={UserId} RevokedTokenCount={RevokedCount}",
                storedToken.UserId, revokedCount);
            return Unauthorized("Oturumunuz güvenlik nedeniyle sonlandırıldı. Lütfen tekrar giriş yapın.");
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
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
            RevokedAt = null,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
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

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<SessionDto>>> GetSessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await _context.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SessionDto
            {
                Id = t.Id, CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt,
                IpAddress = t.IpAddress, UserAgent = t.UserAgent
            })
            .ToListAsync();
    }

    [HttpDelete("sessions/{sessionId:int}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(int sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var session = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId);
        if (session == null)
        {
            return NotFound();
        }

        if (session.RevokedAt == null)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }

    [HttpGet("audit-log")]
    [Authorize]
    public async Task<ActionResult<PagedResultDto<AuthAuditEventDto>>> GetAuditLog(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUser = await _userManager.FindByIdAsync(userId);
        var email = currentUser?.Email;

        var query = _context.AuthAuditEvents.AsNoTracking()
            .Where(e => e.UserId == userId || (email != null && e.Email == email))
            .OrderByDescending(e => e.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuthAuditEventDto
            {
                Id = e.Id,
                UserId = e.UserId,
                Email = e.Email,
                EventType = e.EventType,
                Succeeded = e.Succeeded,
                IpAddress = e.IpAddress,
                UserAgent = e.UserAgent,
                Detail = e.Detail,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync();

        return new PagedResultDto<AuthAuditEventDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // DÜZELTİLDİ: Bu uç e-posta gönderimi tetiklediği için (spam/email
    // bombing riski) AuthPolicy (5/dk) rate limitine alındı. Önceden sadece
    // global limitere (120/dk/kullanıcı) tabiydi; yetkili herhangi bir
    // kullanıcı, üçüncü bir kişinin adresini NewEmail olarak vererek o
    // adrese dakikada onlarca onay kodu e-postası tetikleyebiliyordu.
    [HttpPost("email-change")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> RequestEmailChange(RequestEmailChangeDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();
        if (string.Equals(user.Email, dto.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Yeni email mevcut email ile aynı olamaz.");
        }

        var token = await _userManager.GenerateChangeEmailTokenAsync(user, dto.NewEmail);
        await _emailService.SendEmailAsync(dto.NewEmail, "Email değişikliği", $"Onay kodunuz: {token}");
        await _authAudit.RecordAsync(HttpContext, "email-change-request", true, user, detail: dto.NewEmail);
        return Ok("Onay kodu yeni email adresinize gönderildi.");
    }

    [HttpPost("email-change/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();

        var oldEmail = user.Email;

        var result = await _userManager.ChangeEmailAsync(user, dto.NewEmail, dto.Token);
        if (!result.Succeeded) return BadRequest(result.Errors);

        await _userManager.SetUserNameAsync(user, dto.NewEmail);
        await RevokeAllRefreshTokensAsync(user.Id);
        await _authAudit.RecordAsync(HttpContext, "email-change-confirm", true, user, detail: dto.NewEmail);

        if (!string.IsNullOrWhiteSpace(oldEmail) &&
            !string.Equals(oldEmail, dto.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            _emailQueue.Enqueue(new EmailMessage(
                oldEmail,
                "Hesap email adresiniz değiştirildi",
                $"Hesabınızın email adresi '{dto.NewEmail}' olarak değiştirildi. " +
                "Bu işlemi siz yapmadıysanız, lütfen derhal şifrenizi sıfırlayın ve bizimle iletişime geçin."));
        }

        return Ok("Email adresiniz güncellendi. Lütfen tekrar giriş yapın.");
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
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            _emailQueue.Enqueue(new EmailMessage(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}"));
        }

        return Ok("Eğer bu email adresi kayıtlıysa ve doğrulanmamışsa, yeni bir doğrulama kodu gönderildi.");
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Ok("Eğer bu email adresi kayıtlıysa, şifre sıfırlama linki gönderilecektir.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        _emailQueue.Enqueue(new EmailMessage(user.Email!, "Şifre Sıfırlama", $"Şifre sıfırlama kodunuz: {token}"));

        return Ok("Eğer bu email kayıtlıysa, sıfırlama linki gönderildi.");
    }

    [HttpGet("me/export")]
    [Authorize]
    public async Task<IActionResult> ExportMyData([FromServices] UserDataExportService exportService)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null)
        {
            return NotFound();
        }

        var export = await exportService.ExportAsync(user);
        return Ok(export);
    }

    [HttpGet("me/level")]
    [Authorize]
    public async Task<ActionResult<UserLevelDto>> GetMyLevel()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
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
            return BadRequest(result.Errors);
        }

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

    private async Task<(string AccessToken, string RefreshToken)> IssueTokensAsync(User user)
    {
        var token = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = TokenService.HashToken(refreshToken),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
        });
        await _context.SaveChangesAsync();
        return (token, refreshToken);
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
}