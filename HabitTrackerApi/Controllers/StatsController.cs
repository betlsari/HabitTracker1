using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Services;
using System.Security.Claims;

namespace Controllers;

[ApiController]
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

    // DÜZELTİLDİ (🔴 SQL provider bağımlılığı): Bu uç nokta önceden ham SQL
    // içinde Postgres'e özgü `date_trunc('month', "CompletionDate" AT TIME
    // ZONE tzId)` sözdizimini kullanıyordu. `AT TIME ZONE` SQLite'ta
    // desteklenmediği için testlerde "near \"AT\": syntax error" ile
    // patlıyordu. Artık habit completion'lar ve book reading log'lar ham
    // (tarih, miktar, xp) olarak çekilip kullanıcının saat diliminde ay
    // başlangıcına (bkz. HabitSchedule.PeriodStartLocal ile aynı mantık,
    // burada doğrudan yıl/ay bazında) bellek içinde bucketleniyor —
    // Postgres/SQLite arasında davranış farkı kalmıyor.
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

        var habitMonthly = habitIds.Length == 0
            ? new Dictionary<DateTime, (int Count, int Xp)>()
            : await LoadHabitMonthlyTotalsAsync(habitIds, tz);

        var bookMonthly = bookIds.Length == 0
            ? new Dictionary<DateTime, (int Count, int Xp)>()
            : await LoadBookMonthlyTotalsAsync(bookIds, tz);

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
        int[] habitIds, TimeZoneInfo tz)
    {
        var rows = await _context.HabitCompletions.AsNoTracking()
            .Where(c => habitIds.Contains(c.HabitId))
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
        int[] bookIds, TimeZoneInfo tz)
    {
        var rows = await _context.BookReadingLogs.AsNoTracking()
            .Where(l => bookIds.Contains(l.BookId))
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