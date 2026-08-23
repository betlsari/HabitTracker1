using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using Data;
using Models;
using Dtos;
using Services;
using System.Security.Claims;

namespace Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Authorize]
public class StatsController : ControllerBase
{
    private const int MaxMonthsBack = 60;
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;

    public StatsController(AppDbContext context, UserManager<User> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet("monthly")]
    public async Task<ActionResult<MonthlySummaryDto>> GetMonthlySummary(int monthsBack = 12)
    {
        if (monthsBack is <= 0 or > MaxMonthsBack)
        {
            return BadRequest($"monthsBack parametresi 1 ile {MaxMonthsBack} arasında olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        var tz = TimeZones.Resolve(user?.TimeZoneId);

        var habitIds = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .Select(h => h.Id)
            .ToArrayAsync();

        var bookIds = await _context.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.Id)
            .ToArrayAsync();

        var nowLocal = TimeZones.ToLocal(DateTime.UtcNow, tz);
        var cursorMonth = new DateTime(nowLocal.Year, nowLocal.Month, 1);

        // DÜZELTİLDİ (🔴 madde 2 — sınırsız geçmiş yükleme): Önceden
        // LoadHabitMonthlyTotalsAsync/LoadBookMonthlyTotalsAsync
        // habitIds/bookIds'e ait TÜM completion/log geçmişini (tarih
        // filtresi olmadan) çekiyordu; monthsBack=1 istense bile kullanıcının
        // yıllarca birikmiş tüm geçmişi belleğe yükleniyordu. Artık istenen
        // monthsBack kadar bir alt sınır (biraz payla — ay başı yuvarlama
        // hatalarına karşı) SQL seviyesinde hesaplanıp filtre olarak
        // veriliyor. Bu, davranışı DEĞİŞTİRMİYOR (zaten sadece bu aralık
        // gösteriliyordu), sadece gereksiz veri transferini engelliyor.
        var earliestMonthLocal = cursorMonth.AddMonths(-(monthsBack - 1));
        var earliestUtc = TimeZones.ToUtc(earliestMonthLocal, tz);

        var habitMonthly = habitIds.Length == 0
            ? new Dictionary<DateTime, (int Count, int Xp)>()
            : await LoadHabitMonthlyTotalsAsync(habitIds, tz, earliestUtc);

        var bookMonthly = bookIds.Length == 0
            ? new Dictionary<DateTime, (int Count, int Xp)>()
            : await LoadBookMonthlyTotalsAsync(bookIds, tz, earliestUtc);

        var months = new List<MonthlyStatDto>(monthsBack);
        var cursor = cursorMonth;
        for (int i = 0; i < monthsBack; i++)
        {
            var h = habitMonthly.TryGetValue(cursor, out var hv) ? hv : (0, 0);
            var b = bookMonthly.TryGetValue(cursor, out var bv) ? bv : (0, 0);

            months.Add(new MonthlyStatDto
            {
                Month = cursor,
                HabitCompletions = h.Item1,
                BookLogEntries = b.Item1,
                TotalXpEarned = h.Item2 + b.Item2
            });

            cursor = cursor.AddMonths(-1);
        }

        var bestMonth = months
            .OrderByDescending(m => m.TotalXpEarned)
            .ThenByDescending(m => m.HabitCompletions + m.BookLogEntries)
            .FirstOrDefault();

        return new MonthlySummaryDto
        {
            Months = months,
            BestMonth = bestMonth,
            CurrentMonthXp = months.FirstOrDefault(m => m.Month == cursorMonth)?.TotalXpEarned ?? 0,
            TotalXpAllTime = user?.TotalXp ?? 0
        };
    }

    private async Task<Dictionary<DateTime, (int Count, int Xp)>> LoadHabitMonthlyTotalsAsync(
        int[] habitIds, TimeZoneInfo tz, DateTime earliestUtc)
    {
        var rows = await _context.HabitCompletions.AsNoTracking()
            .Where(c => habitIds.Contains(c.HabitId) && c.CompletionDate >= earliestUtc)
            .Select(c => new { c.CompletionDate, c.XpEarned })
            .ToListAsync();

        var result = new Dictionary<DateTime, (int Count, int Xp)>();
        foreach (var row in rows)
        {
            var local = TimeZones.ToLocal(row.CompletionDate, tz);
            var monthKey = new DateTime(local.Year, local.Month, 1);
            var current = result.TryGetValue(monthKey, out var existing) ? existing : (0, 0);
            result[monthKey] = (current.Item1 + 1, current.Item2 + row.XpEarned);
        }

        return result;
    }

    private async Task<Dictionary<DateTime, (int Count, int Xp)>> LoadBookMonthlyTotalsAsync(
        int[] bookIds, TimeZoneInfo tz, DateTime earliestUtc)
    {
        var rows = await _context.BookReadingLogs.AsNoTracking()
            .Where(l => bookIds.Contains(l.BookId) && l.ReadDate >= earliestUtc)
            .Select(l => new { l.ReadDate, l.XpEarned })
            .ToListAsync();

        var result = new Dictionary<DateTime, (int Count, int Xp)>();
        foreach (var row in rows)
        {
            var local = TimeZones.ToLocal(row.ReadDate, tz);
            var monthKey = new DateTime(local.Year, local.Month, 1);
            var current = result.TryGetValue(monthKey, out var existing) ? existing : (0, 0);
            result[monthKey] = (current.Item1 + 1, current.Item2 + row.XpEarned);
        }

        return result;
    }
}