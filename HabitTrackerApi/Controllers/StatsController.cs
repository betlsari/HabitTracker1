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
            : (await _context.Database.SqlQuery<MonthlyRow>($"""
                SELECT date_trunc('month', "CompletionDate" AT TIME ZONE {tz.Id}) AS "Month",
                       COUNT(*)::integer AS "Count",
                       COALESCE(SUM("XpEarned"), 0)::integer AS "Xp"
                FROM "HabitCompletions"
                WHERE "HabitId" = ANY({habitIds})
                GROUP BY 1
                """).ToListAsync())
                .ToDictionary(r => r.Month, r => (r.Count, r.Xp));

        var bookMonthly = bookIds.Length == 0
            ? new Dictionary<DateTime, (int Count, int Xp)>()
            : (await _context.Database.SqlQuery<MonthlyRow>($"""
                SELECT date_trunc('month', "ReadDate" AT TIME ZONE {tz.Id}) AS "Month",
                       COUNT(*)::integer AS "Count",
                       COALESCE(SUM("XpEarned"), 0)::integer AS "Xp"
                FROM "BookReadingLogs"
                WHERE "BookId" = ANY({bookIds})
                GROUP BY 1
                """).ToListAsync())
                .ToDictionary(r => r.Month, r => (r.Count, r.Xp));

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

    private sealed class MonthlyRow
    {
        public DateTime Month { get; init; }
        public int Count { get; init; }
        public int Xp { get; init; }
    }
}