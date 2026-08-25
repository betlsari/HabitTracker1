// HabitTrackerApi/Services/HabitProgressService.cs
using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Configuration;
using Models;

namespace Services;

public class HabitProgressService
{
    private readonly AppDbContext _context;
    private readonly XpService _xpService;
    private readonly int _maxHistoryLookbackDays;

    public HabitProgressService(AppDbContext context, XpService xpService, IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _xpService = xpService;
        _maxHistoryLookbackDays = limits.Value.MaxHistoryLookbackDays;
    }

    public async Task<HabitProgressDto> GetProgressAsync(Habit habit, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var totals = await LoadPeriodTotalsAsync(habit.Id, habit.Period, tz, cancellationToken);
        var truncated = IsHistoryTruncated(habit.CreatedAt);
        return BuildProgress(habit, tz, totals, DateTime.UtcNow, truncated);
    }

    public async Task<List<HabitProgressDto>> GetSummaryAsync(IReadOnlyList<Habit> habits, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;

        if (habits.Count == 0)
        {
            return new List<HabitProgressDto>();
        }

        var totalsByHabit = await LoadPeriodTotalsForHabitsAsync(habits, tz, cancellationToken);

        var result = new List<HabitProgressDto>(habits.Count);
        foreach (var habit in habits)
        {
            var totals = totalsByHabit.TryGetValue(habit.Id, out var t) ? t : new Dictionary<DateTime, int>();
            var truncated = IsHistoryTruncated(habit.CreatedAt);
            result.Add(BuildProgress(habit, tz, totals, now, truncated));
        }

        return result;
    }

    // DÜZELTİLDİ (🔴 madde 2 — bellekte lookback filtreleme): Önceden bu
    // metod HER ZAMAN LoadPeriodTotalsBatchAsync'i MaxHistoryLookbackDays
    // (730 gün) sınırıyla çağırıp, ardından cursor'ı geriye doğru gezerek
    // (while döngüsü) sadece lookbackPeriods kadarını sayıyordu — yani
    // kullanıcı "son 30 gün" istese bile SQL'den 730 günlük tüm veri
    // çekiliyor, filtreleme sadece bellekte yapılıyordu. Artık istenen
    // lookbackPeriods'a göre yaklaşık bir üst sınır hesaplanıp, DB'den
    // MaxHistoryLookbackDays ile bu ikisinin DAHA SIKI (daha kısıtlayıcı)
    // olanı kadar veri çekiliyor. lookbackPeriods küçükse (örn. 30) çekilen
    // veri hacmi önemli ölçüde azalır; lookbackPeriods büyükse
    // (MaxHistoryLookbackDays'i aşıyorsa) davranış öncekiyle birebir aynı
    // kalır (mevcut sınır zaten daha sıkı).
    public async Task<List<HabitComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Habit> habits,
        string? timeZoneId,
        int lookbackPeriods = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;
        var results = new List<HabitComparisonDto>(habits.Count);

        if (habits.Count == 0)
        {
            return results;
        }

        var effectiveCutoffUtc = ComputeEffectiveCutoffUtc(habits, lookbackPeriods, now);
        var totalsByHabit = await LoadPeriodTotalsForHabitsAsync(habits, tz, cancellationToken, effectiveCutoffUtc);

        foreach (var habit in habits)
        {
            var totals = totalsByHabit.TryGetValue(habit.Id, out var t) ? t : new Dictionary<DateTime, int>();
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

            // YENİ (🔵): habit'in oluşturulma tarihi, bu sorgu için kullanılan
            // etkin cutoff'tan öncesindeyse, gerçek geçmişin bir kısmı hiç
            // okunmadı demektir — completion rate düşük görünüyor olabilir.
            var truncated = createdAtUtc < effectiveCutoffUtc;

            results.Add(new HabitComparisonDto
            {
                HabitId = habit.Id,
                Name = habit.Name,
                Category = habit.Category,
                CurrentStreak = CountStreak(habit, totals, currentPeriodStart, tz),
                CompletionRatePercent = Math.Round(completionRate, 1),
                PercentageCompletedThisPeriod = Math.Round(percentageThisPeriod, 1),
                HistoryTruncated = truncated
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
        var cutoffUtc = DateTime.UtcNow.AddDays(-_maxHistoryLookbackDays);
        return await LoadPeriodTotalsAsync(habitId, period, tz, cutoffUtc, cancellationToken);
    }

    // YENİ: cutoff'u dışarıdan alabilen overload; GetComparisonAsync gibi
    // daha dar bir aralık isteyen çağıranlar bunu kullanır.
    private async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int habitId,
        HabitPeriod period,
        TimeZoneInfo tz,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var rows = await _context.HabitCompletions
            .AsNoTracking()
            .Where(c => c.HabitId == habitId && c.CompletionDate >= cutoffUtc)
            .Select(c => new { c.CompletionDate, c.Amount })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<DateTime, int>();
        foreach (var row in rows)
        {
            var periodStart = HabitSchedule.PeriodStartLocal(row.CompletionDate, period, tz);
            result[periodStart] = result.TryGetValue(periodStart, out var existing)
                ? existing + row.Amount
                : row.Amount;
        }

        return result;
    }

    public async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsBatchAsync(
        IReadOnlyList<int> habitIds,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-_maxHistoryLookbackDays);
        return await LoadPeriodTotalsBatchAsync(habitIds, period, tz, cutoffUtc, cancellationToken);
    }

    // YENİ: cutoff'u dışarıdan alabilen overload.
    private async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsBatchAsync(
        IReadOnlyList<int> habitIds,
        HabitPeriod period,
        TimeZoneInfo tz,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var result = habitIds.ToDictionary(id => id, _ => new Dictionary<DateTime, int>());
        if (habitIds.Count == 0)
        {
            return result;
        }

        var idsArray = habitIds.ToArray();
        var rows = await _context.HabitCompletions
            .AsNoTracking()
            .Where(c => idsArray.Contains(c.HabitId) && c.CompletionDate >= cutoffUtc)
            .Select(c => new { c.HabitId, c.CompletionDate, c.Amount })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.HabitId, out var dict))
            {
                dict = new Dictionary<DateTime, int>();
                result[row.HabitId] = dict;
            }

            var periodStart = HabitSchedule.PeriodStartLocal(row.CompletionDate, period, tz);
            dict[periodStart] = dict.TryGetValue(periodStart, out var existing)
                ? existing + row.Amount
                : row.Amount;
        }

        return result;
    }

    private async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsForHabitsAsync(
        IReadOnlyList<Habit> habits,
        TimeZoneInfo tz,
        CancellationToken cancellationToken,
        DateTime? cutoffUtcOverride = null)
    {
        var result = new Dictionary<int, Dictionary<DateTime, int>>();

        foreach (var group in habits.GroupBy(h => h.Period))
        {
            var habitIds = group.Select(h => h.Id).ToArray();
            var batch = cutoffUtcOverride.HasValue
                ? await LoadPeriodTotalsBatchAsync(habitIds, group.Key, tz, cutoffUtcOverride.Value, cancellationToken)
                : await LoadPeriodTotalsBatchAsync(habitIds, group.Key, tz, cancellationToken);
            foreach (var kv in batch)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    // YENİ (🔴 madde 2 yardımcı metodu): lookbackPeriods ve habit'lerin
    // period tiplerine göre yaklaşık bir "bu kadar günden eskisi gerekmiyor"
    // tarihi hesaplar. En kısıtlayıcı (en uzun) period tipi baz alınır
    // (Monthly ~31 gün) — bu sayede tüm habit'ler için güvenle tek bir
    // cutoff kullanılabilir. Sonuç, mevcut MaxHistoryLookbackDays cutoff'u
    // ile karşılaştırılıp İKİSİNDEN DAHA YAKIN (daha az veri çeken) olan
    // seçilir; MaxHistoryLookbackDays sınırı hiçbir zaman aşılmaz.
    private DateTime ComputeEffectiveCutoffUtc(IReadOnlyList<Habit> habits, int lookbackPeriods, DateTime now)
    {
        var maxPeriod = habits.Max(h => h.Period);
        var approxDaysPerPeriod = maxPeriod switch
        {
            HabitPeriod.Monthly => 31,
            HabitPeriod.Weekly => 7,
            _ => 1
        };

        // +2 pay: hafta/ay başlangıcı yuvarlamalarından kaynaklanabilecek
        // sınır hatalarına karşı küçük bir tolerans.
        var lookbackBasedCutoff = now.AddDays(-((long)approxDaysPerPeriod * lookbackPeriods + 2));
        var hardCutoff = now.AddDays(-_maxHistoryLookbackDays);

        // İki cutoff'tan DAHA GEÇ (yani daha az veri çeken, daha kısıtlayıcı)
        // olanı kullanılır.
        return lookbackBasedCutoff > hardCutoff ? lookbackBasedCutoff : hardCutoff;
    }

    // YENİ (🔵): Habit'in oluşturulma tarihi genel MaxHistoryLookbackDays
    // sınırından daha eskiyse, LoadPeriodTotalsAsync tarafından okunan
    // veri o habit'in tüm geçmişini kapsamıyor demektir.
    private bool IsHistoryTruncated(DateTime habitCreatedAtUtc)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-_maxHistoryLookbackDays);
        var createdAtUtc = habitCreatedAtUtc.Kind == DateTimeKind.Utc
            ? habitCreatedAtUtc
            : DateTime.SpecifyKind(habitCreatedAtUtc, DateTimeKind.Utc);
        return createdAtUtc < cutoffUtc;
    }

    public async Task<HabitRecalculationResult> RecalculateHabitAsync(
        Habit habit,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);

        var completions = await _context.HabitCompletions
            .Where(c => c.HabitId == habit.Id)
            .OrderBy(c => c.CompletionDate)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var oldTotalXp = completions.Sum(c => c.XpEarned);
        var oldTotalPetStreakBonus = completions.Sum(c => c.PetStreakBonusXp);

        var periodTotals = new Dictionary<DateTime, int>();
        var periodGoalMet = new Dictionary<DateTime, bool>();

        foreach (var completion in completions)
        {
            var periodStart = HabitSchedule.PeriodStartLocalOfCompletion(completion.CompletionDate, habit.Period, tz);
            var totalBefore = periodTotals.TryGetValue(periodStart, out var existing) ? existing : 0;
            var totalAfter = totalBefore + completion.Amount;
            periodTotals[periodStart] = totalAfter;

            var goalJustReached = totalBefore < habit.DailyGoal && totalAfter >= habit.DailyGoal;

            var previousPeriodGoalMet = false;
            if (goalJustReached)
            {
                var previousStart = HabitSchedule.PreviousPeriodStartLocal(periodStart, habit.Period);
                previousPeriodGoalMet = periodGoalMet.TryGetValue(previousStart, out var met) && met;
            }

            var streakKept = goalJustReached && previousPeriodGoalMet;
            var isOnTime = HabitSchedule.IsWithinTargetTime(habit, completion.CompletionDate, tz);

            completion.XpEarned = _xpService.CalculateCompletionXp(habit, completion.Amount, totalBefore, streakKept, isOnTime);
            completion.PetStreakBonusXp = streakKept ? _xpService.GetStreakKeepBonus() : 0;
            completion.IsOnTime = isOnTime;

            if (totalAfter >= habit.DailyGoal)
            {
                periodGoalMet[periodStart] = true;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var newTotalXp = completions.Sum(c => c.XpEarned);
        var newTotalPetStreakBonus = completions.Sum(c => c.PetStreakBonusXp);

        return new HabitRecalculationResult
        {
            XpDelta = newTotalXp - oldTotalXp,
            PetStreakBonusDelta = newTotalPetStreakBonus - oldTotalPetStreakBonus
        };
    }

    private static HabitProgressDto BuildProgress(
        Habit habit,
        TimeZoneInfo tz,
        IReadOnlyDictionary<DateTime, int> totals,
        DateTime utcNow,
        bool historyTruncated)
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
            PeriodEnd = periodEnd,
            HistoryTruncated = historyTruncated
        };
    }

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

public sealed class HabitRecalculationResult
{
    public int XpDelta { get; init; }
    public int PetStreakBonusDelta { get; init; }
}