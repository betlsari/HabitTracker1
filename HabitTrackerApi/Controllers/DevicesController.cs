using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using System.Security.Claims;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _context;

    public DevicesController(AppDbContext context)
    {
        _context = context;
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
    public async Task<IActionResult> Unregister([FromBody] RegisterDeviceTokenDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var existing = await _context.DeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == dto.Token);
        if (existing == null)
        {
            return NotFound();
        }

        _context.DeviceTokens.Remove(existing);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
