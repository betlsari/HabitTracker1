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
[EnableRateLimiting("AuthPolicy")] // brute-force / spam koruması (Program.cs'te tanımlı)
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

        var token = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null

        });
        await _context.SaveChangesAsync();
        return Ok(new { Token = token, RefreshToken = refreshToken });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenDto refreshTokenDto)
    {
        var storedToken = await _context.RefreshTokens
        .Include(rt => rt.User)
        .FirstOrDefaultAsync(rt => rt.Token == refreshTokenDto.RefreshToken);
        if (storedToken == null || storedToken.ExpiresAt < DateTime.UtcNow || storedToken.RevokedAt != null)
        {
            return Unauthorized();
        }
        var newToken = _tokenService.GenerateToken(storedToken.User!);
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        storedToken.RevokedAt = DateTime.UtcNow;
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefreshToken,
            UserId = storedToken.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null
        });
        await _context.SaveChangesAsync();
        return Ok(new { Token = newToken, RefreshToken = newRefreshToken });
    }

    // YENİ: Tek bir cihazdan çıkış yapar; sadece verilen refresh token'ı iptal eder.
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken && rt.UserId == userId);

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

    // YENİ: Tüm cihazlardan çıkış yapar (örn. "hesabım çalındı" senaryosu).
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Tüm oturumlar kapatıldı.", revokedCount = activeTokens.Count });
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

    // YENİ: Doğrulama emaili gelmediyse / süresi dolduysa tekrar gönderir.
    // Kullanıcı enumeration'ı önlemek için her durumda aynı mesaj dönülür.
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
        return Ok("Şifre başarıyla sıfırlandı.");
    }

    // YENİ: Giriş yapmış kullanıcı için "unuttum" akışı olmadan şifre değiştirme.
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

        return Ok("Şifre başarıyla değiştirildi.");
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
        return Ok(new { user.Email, user.TotalXp });
    }
}
