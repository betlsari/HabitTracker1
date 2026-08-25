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
    private readonly EmailService _emailService;
    private readonly AppDbContext _context;
    private readonly IEmailQueue _emailQueue;
    private readonly SecurityStampCache _securityStampCache;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        TokenService tokenService,
        EmailService emailService,
        IEmailQueue emailQueue,
        AppDbContext context,
        SecurityStampCache securityStampCache,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _emailService = emailService;
        _emailQueue = emailQueue;
        _context = context;
        _securityStampCache = securityStampCache;
        _logger = logger;
    }

    [HttpPost("register")]
    [EnableRateLimiting("AuthPolicy")]
    [EmailRateLimit]
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
        await _emailQueue.EnqueueAsync(new EmailMessage(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}"));
        return Ok("Eğer bu email adresi kullanılabiliyorsa, kayıt oluşturuldu ve doğrulama emaili gönderildi.");
    }

    [HttpPost("login")]
    [EnableRateLimiting("AuthPolicy")]
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
            return Unauthorized();
        }

        var (accessToken, refreshTokenValue) = await IssueTokensAsync(user);
        return Ok(new { Token = accessToken, RefreshToken = refreshTokenValue });
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
            await RevokeAllRefreshTokensAsync(storedToken.UserId);
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
            ExpiresAt = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime),
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

    [HttpPost("email-change")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    [EmailRateLimit]
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

        if (!string.IsNullOrWhiteSpace(oldEmail) &&
            !string.Equals(oldEmail, dto.NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            await _emailQueue.EnqueueAsync(new EmailMessage(
                oldEmail,
                "Hesap email adresiniz değiştirildi",
                $"Hesabınızın email adresi '{dto.NewEmail}' olarak değiştirildi. Bu işlemi siz yapmadıysanız, lütfen derhal şifrenizi sıfırlayın."));
        }

        return Ok("Email adresiniz güncellendi. Lütfen tekrar giriş yapın.");
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string email, string token)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return NotFound();
        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded) return BadRequest(result.Errors);
        return Ok("Email doğrulandı.");
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("AuthPolicy")]
    [EmailRateLimit]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailQueue.EnqueueAsync(new EmailMessage(user.Email!, "Email Doğrulama", $"Doğrulama kodunuz: {token}"));
        }
        return Ok("Eğer bu email adresi kayıtlıysa ve doğrulanmamışsa, yeni bir doğrulama kodu gönderildi.");
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthPolicy")]
    [EmailRateLimit]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Ok("Eğer bu email adresi kayıtlıysa, şifre sıfırlama linki gönderilecektir.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _emailQueue.EnqueueAsync(new EmailMessage(user.Email!, "Şifre Sıfırlama", $"Şifre sıfırlama kodunuz: {token}"));
        return Ok("Eğer bu email kayıtlıysa, sıfırlama linki gönderildi.");
    }

    [HttpGet("me/export")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ExportMyData([FromServices] UserDataExportService exportService)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

        var export = await exportService.ExportAsync(user);
        return Ok(export);
    }

    [HttpGet("me/level")]
    [Authorize]
    public async Task<ActionResult<UserLevelDto>> GetMyLevel()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

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
        await _emailQueue.EnqueueAsync(new EmailMessage(
            user.Email!, "Şifreniz değiştirildi",
            "Hesabınızın şifresi değiştirildi. Bu işlemi siz yapmadıysanız derhal destek ekibiyle iletişime geçin."));

        return Ok("Şifre başarıyla sıfırlandı.");
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(user, dto.CurrentPassword, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            if (passwordResult.IsLockedOut)
            {
                return BadRequest("Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin.");
            }
            return BadRequest("Mevcut şifre hatalı.");
        }

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        await RevokeAllRefreshTokensAsync(user.Id);
        await _emailQueue.EnqueueAsync(new EmailMessage(
            user.Email!, "Şifreniz değiştirildi",
            "Hesabınızın şifresi değiştirildi. Bu işlemi siz yapmadıysanız derhal destek ekibiyle iletişime geçin."));

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
        if (user == null) return NotFound();

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
        if (user == null) return NotFound();
        return Ok(new { user.Email, user.DisplayName, user.AvatarUrl, user.TotalXp, user.TimeZoneId });
    }

    [HttpPut("me/profile")]
    [Authorize]
    [SanitizeText]
    public async Task<IActionResult> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();

        user.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(dto.AvatarUrl) ? null : dto.AvatarUrl.Trim();
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { user.Email, user.DisplayName, user.AvatarUrl, user.TotalXp, user.TimeZoneId });
    }

    [HttpDelete("me")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(user, dto.CurrentPassword, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            if (passwordResult.IsLockedOut)
            {
                return BadRequest("Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin.");
            }
            return BadRequest("Şifre hatalı. Hesap silme işlemi iptal edildi.");
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        await _securityStampCache.InvalidateAsync(userId!);
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
            ExpiresAt = DateTime.UtcNow.Add(_tokenService.RefreshTokenLifetime),
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

        await _securityStampCache.InvalidateAsync(userId);
        return activeTokens.Count;
    }
}