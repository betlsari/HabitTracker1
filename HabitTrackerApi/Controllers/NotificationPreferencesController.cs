using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using System.Security.Claims;
using Asp.Versioning;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiVersion("1.0")]

[Authorize]
public class NotificationPreferencesController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationPreferencesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationPreferenceDto>> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pref = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (pref == null)
        {
            
            return new NotificationPreferenceDto();
        }

        return ToDto(pref);
    }

    [HttpPut]
    public async Task<ActionResult<NotificationPreferenceDto>> Update(UpdateNotificationPreferenceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pref = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (pref == null)
        {
            pref = new NotificationPreference { UserId = userId };
            _context.NotificationPreferences.Add(pref);
        }

        pref.DisabledTypes = string.Join(',', dto.DisabledTypes.Distinct());
        pref.QuietHoursStart = dto.QuietHoursStart;
        pref.QuietHoursEnd = dto.QuietHoursEnd;
        pref.DigestEnabled = dto.DigestEnabled;
        pref.DigestHourUtc = dto.DigestHourUtc;
        pref.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToDto(pref);
    }

    // YENİ (🟡 eksik uç nokta): Kullanıcının bildirim tercihini (kapatılmış
    // türler + sessiz saatler) varsayılana döndürmesinin hiçbir yolu yoktu;
    // istemci tek tek DisabledTypes'ı boşaltıp QuietHours alanlarını null
    // göndererek PUT çağırmak zorundaydı. DELETE artık tercih kaydını
    // tamamen kaldırıp GET'in zaten döndürdüğü "hiçbir şey yapılandırılmamış"
    // varsayılan durumuna (tüm bildirimler açık, sessiz saat yok) geri
    // döndürüyor.
    [HttpDelete]
    public async Task<ActionResult<NotificationPreferenceDto>> ResetToDefault()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pref = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (pref != null)
        {
            _context.NotificationPreferences.Remove(pref);
            await _context.SaveChangesAsync();
        }

        return new NotificationPreferenceDto();
    }

    private static NotificationPreferenceDto ToDto(NotificationPreference pref) => new()
    {
        DisabledTypes = string.IsNullOrWhiteSpace(pref.DisabledTypes)
            ? new List<string>()
            : pref.DisabledTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        QuietHoursStart = pref.QuietHoursStart,
        QuietHoursEnd = pref.QuietHoursEnd,
        DigestEnabled = pref.DigestEnabled,
        DigestHourUtc = pref.DigestHourUtc
    };
}