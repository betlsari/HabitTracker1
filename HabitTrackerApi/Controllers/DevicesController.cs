using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Configuration;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly int _maxDeviceTokensPerUser;

    public DevicesController(AppDbContext context, IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _maxDeviceTokensPerUser = limits.Value.MaxDeviceTokensPerUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<DeviceTokenDto>>> GetDevices(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = _context.DeviceTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastSeenAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<DeviceTokenDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // DÜZELTİLDİ: Önceden "mevcut kayıt sayısını say -> limit aşılıyorsa
    // en eskiyi sil -> yenisini ekle" adımları ayrı ayrı SaveChanges'lerdi
    // ve hiçbir eşzamanlılık koruması yoktu. Aynı kullanıcıdan gelen iki
    // eşzamanlı istek, ikisi de aynı anda "count < max" görüp limiti
    // aşabiliyordu (klasik check-then-act race condition). Artık tüm akış
    // tek bir transaction içinde ve bu kullanıcıya özel bir Postgres
    // advisory lock (pg_advisory_xact_lock) ile korunuyor; kilit transaction
    // sonunda (commit/rollback) otomatik serbest kalıyor, ayrı bir "unlock"
    // çağrısına gerek yok.
    [HttpPost]
    public async Task<IActionResult> Register(RegisterDeviceTokenDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({userId}))");

            var existing = await _context.DeviceTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == dto.Token);

            if (existing != null)
            {
                existing.Platform = dto.Platform;
                existing.LastSeenAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok();
            }

            var currentCount = await _context.DeviceTokens.CountAsync(t => t.UserId == userId);
            if (currentCount >= _maxDeviceTokensPerUser)
            {
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

            await transaction.CommitAsync();
            return Ok();
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> UnregisterById(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var existing = await _context.DeviceTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (existing == null)
        {
            return NotFound();
        }

        _context.DeviceTokens.Remove(existing);
        await _context.SaveChangesAsync();
        return NoContent();
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

    private static DeviceTokenDto ToDto(DeviceToken t) => new()
    {
        Id = t.Id,
        Platform = t.Platform,
        CreatedAt = t.CreatedAt,
        LastSeenAt = t.LastSeenAt,
        TokenSuffix = t.Token.Length >= 4 ? t.Token[^4..] : t.Token
    };
}