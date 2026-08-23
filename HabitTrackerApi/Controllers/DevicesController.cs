using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Configuration;

public class DevicesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly int _maxDeviceTokensPerUser;

    public DevicesController(AppDbContext context, IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _maxDeviceTokensPerUser = limits.Value.MaxDeviceTokensPerUser;
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterDeviceTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var existing = await _context.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == dto.Token);

        if (existing != null)
        {
            existing.Platform = dto.Platform;
            existing.LastSeenAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        var currentCount = await _context.DeviceTokens.CountAsync(t => t.UserId == userId);
        if (currentCount >= _maxDeviceTokensPerUser)
        {
            // En eski kaydı silip yerine yenisini ekleyerek kullanıcıyı
            // engellemek yerine "en fazla N cihaz" politikasını uygula.
            var oldest = await _context.DeviceTokens
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.LastSeenAt)
                .FirstOrDefaultAsync();
            if (oldest != null)
            {
                _context.DeviceTokens.Remove(oldest);
            }
        }

        _context.DeviceTokens.Add(new DeviceToken
        {
            UserId = userId,
            Token = dto.Token,
            Platform = dto.Platform,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Ok();
    }

    

    [HttpDelete]
    public async Task<IActionResult> Unregister([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest("token zorunludur.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var existing = await _context.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token);
        if (existing == null)
        {
            return NotFound();
        }

        _context.DeviceTokens.Remove(existing);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
