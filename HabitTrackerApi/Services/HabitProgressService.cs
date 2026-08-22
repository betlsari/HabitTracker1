using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class HabitProgressService
{
    private readonly AppDbContext _context;

    public HabitProgressService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HabitProgressDto> GetProgressAsync(Habit habit, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var totals = await LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
        return BuildProgress(habit, tz, totals, DateTime.UtcNow);
    }

    public async Task<List<HabitProgressDto>> GetSummaryAsync(IReadOnlyList<Habit> habits, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var result = new List<HabitProgressDto>(habits.Count);
        foreach (var habit in habits)
        {
            var totals = await LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
            result.Add(BuildProgress(habit, tz, totals, DateTime.UtcNow));
        }

        return result;
    }

    public async Task<List<HabitComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Habit> habits,
        string? timeZoneId,
        int lookbackPeriods = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;
        var results = new List<HabitComparisonDto>(habits.Count);

        foreach (var habit in habits)
        {
            var totals = await LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
            var currentPeriodStart = HabitSchedule.PeriodStartLocal(now, habit.Period, tz);

            var createdAtUtc = habit.CreatedAt.Kind == DateTimeKind.Utc
                ? habit.CreatedAt
                : DateTime.SpecifyKind(habit.CreatedAt, DateTimeKind.Utc);
            var habitStart = HabitSchedule.PeriodStartLocal(createdAtUtc, habit.Period, tz);

            var cursor = currentPeriodStart;
            int periodsConsidered = 0;
            int periodsGoalMet = 0;

            while (cursor >= habitStart && periodsConsidered < lookbackPeriods)
            {
                periodsConsidered++;
                var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
                if (total >= habit.DailyGoal)
                {
                    periodsGoalMet++;
                }

                cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, habit.Period);
            }

            var completionRate = periodsConsidered == 0 ? 0 : (double)periodsGoalMet / periodsConsidered * 100;
            var totalInCurrentPeriod = totals.TryGetValue(currentPeriodStart, out var currentTotal) ? currentTotal : 0;
            var percentageThisPeriod = habit.DailyGoal == 0
                ? 0
                : Math.Min(100, (double)totalInCurrentPeriod / habit.DailyGoal * 100);

            results.Add(new HabitComparisonDto
            {
                HabitId = habit.Id,
                Name = habit.Name,
                Category = habit.Category,
                CurrentStreak = CountStreak(habit, totals, currentPeriodStart, tz),
                CompletionRatePercent = Math.Round(completionRate, 1),
                PercentageCompletedThisPeriod = Math.Round(percentageThisPeriod, 1)
            });
        }

        var ranked = results
            .OrderByDescending(r => r.CompletionRatePercent)
            .ThenByDescending(r => r.CurrentStreak)
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        return ranked;
    }

    // DÜZELTİLDİ: granularity parametresi eklendi. Verilmezse habit'in kendi
    // Period'u kullanılır (önceki davranışla tam uyumlu); verilirse (Daily/
    // Weekly/Monthly) kullanıcı, habit'in kendi periyodundan bağımsız olarak
    // istediği eksende istatistik görebilir — dokümandaki "günlük, haftalık ve
    // aylık başarı grafikleri" maddesi artık her habit için sağlanabiliyor.
    public async Task<List<DailyStatDto>> GetStatsAsync(
        Habit habit,
        string? timeZoneId,
        int periods,
        HabitPeriod? granularity = null,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var effectivePeriod = granularity ?? habit.Period;

        var totals = await LoadPeriodTotalsAsync(habit.Id, effectivePeriod, tz, cancellationToken);
        var now = DateTime.UtcNow;
        var cursor = HabitSchedule.PeriodStartLocal(now, effectivePeriod, tz);
        var stats = new List<DailyStatDto>();

        for (int i = 0; i < periods; i++)
        {
            var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
            stats.Add(new DailyStatDto
            {
                Date = cursor,
                TotalAmount = total,
                GoalReached = total >= habit.DailyGoal
            });
            cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, effectivePeriod);
        }

        return stats;
    }

    public async Task<CompletionSnapshot> GetCompletionSnapshotAsync(
        Habit habit,
        DateTime completionUtc,
        int incomingAmount,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var totals = await LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
        var periodStart = HabitSchedule.PeriodStartLocalOfCompletion(completionUtc, habit.Period, tz);
        var totalBefore = totals.TryGetValue(periodStart, out var current) ? current : 0;
        var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, habit.Period);
        var previousTotal = totals.TryGetValue(previousStart, out var prev) ? prev : 0;
        var totalAfter = totalBefore + incomingAmount;
        var goalJustReached = totalBefore < habit.DailyGoal && totalAfter >= habit.DailyGoal;

        if (goalJustReached)
        {
            totals[periodStart] = totalAfter;
        }

        var streakAfter = goalJustReached
            ? CountStreak(habit, totals, periodStart, tz)
            : CountStreak(habit, totals, HabitSchedule.PeriodStartLocal(DateTime.UtcNow, habit.Period, tz), tz);

        return new CompletionSnapshot
        {
            TotalBeforeInPeriod = totalBefore,
            TotalAfterInPeriod = totalAfter,
            GoalJustReached = goalJustReached,
            PreviousPeriodGoalMet = previousTotal >= habit.DailyGoal,
            StreakAfter = streakAfter,
            PeriodStartLocal = periodStart
        };
    }

    public static bool IsGoalReached(Habit habit, IReadOnlyDictionary<DateTime, int> totals, DateTime periodStartLocal)
    {
        return totals.TryGetValue(periodStartLocal, out var total) && total >= habit.DailyGoal;
    }

    public async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int habitId,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Database.SqlQuery<PeriodTotal>($"""
            SELECT date_trunc({GetDateTruncUnit(period)}, "CompletionDate" AT TIME ZONE {tz.Id}) AS "PeriodStart",
                   COALESCE(SUM("Amount"), 0)::integer AS "Total"
            FROM "HabitCompletions"
            WHERE "HabitId" = {habitId}
            GROUP BY 1
            """).ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PeriodStart, row => row.Total);
    }

    private static string GetDateTruncUnit(HabitPeriod period) => period switch
    {
        HabitPeriod.Daily => "day",
        HabitPeriod.Weekly => "week",
        HabitPeriod.Monthly => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };

    private sealed class PeriodTotal
    {
        public DateTime PeriodStart { get; init; }
        public int Total { get; init; }
    }

    private static HabitProgressDto BuildProgress(
        Habit habit,
        TimeZoneInfo tz,
        IReadOnlyDictionary<DateTime, int> totals,
        DateTime utcNow)
    {
        var periodStart = HabitSchedule.PeriodStartLocal(utcNow, habit.Period, tz);
        var periodEnd = HabitSchedule.NextPeriodStartLocal(periodStart, habit.Period);
        var totalInPeriod = totals.TryGetValue(periodStart, out var total) ? total : 0;
        var isCompleted = totalInPeriod >= habit.DailyGoal;
        var percentage = habit.DailyGoal == 0 ? 0 : (double)totalInPeriod / habit.DailyGoal * 100;

        return new HabitProgressDto
        {
            HabitId = habit.Id,
            DailyGoal = habit.DailyGoal,
            TotalToday = totalInPeriod,
            TotalInPeriod = totalInPeriod,
            IsCompleted = isCompleted,
            PercentageCompleted = percentage,
            CurrentStreak = CountStreak(habit, totals, periodStart, tz),
            Period = habit.Period,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd
        };
    }

    // Public: ReminderBackgroundService gibi dışarıdan da (örn. kırılan streak
    // uzunluğunu bildirime yansıtmak için) çağrılabilmesi için erişilebilir.
    public static int CountStreak(Habit habit, IReadOnlyDictionary<DateTime, int> totals, DateTime currentPeriodStart, TimeZoneInfo tz)
    {
        int streak = 0;
        var cursor = currentPeriodStart;
        var createdAtUtc = habit.CreatedAt.Kind == DateTimeKind.Utc
            ? habit.CreatedAt
            : DateTime.SpecifyKind(habit.CreatedAt, DateTimeKind.Utc);
        var minStart = HabitSchedule.PeriodStartLocal(createdAtUtc, habit.Period, tz);

        while (cursor >= minStart)
        {
            var dailyTotal = totals.TryGetValue(cursor, out var dt) ? dt : 0;
            if (dailyTotal < habit.DailyGoal)
            {
                break;
            }

            streak++;
            cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, habit.Period);
        }

        return streak;
    }
}

public sealed class CompletionSnapshot
{
    public int TotalBeforeInPeriod { get; init; }
    public int TotalAfterInPeriod { get; init; }
    public bool GoalJustReached { get; init; }
    public bool PreviousPeriodGoalMet { get; init; }
    public int StreakAfter { get; init; }
    public DateTime PeriodStartLocal { get; init; }
}
