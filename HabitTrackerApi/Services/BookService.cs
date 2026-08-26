using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Configuration;
using Models;

namespace Services;

public sealed class BookLogResult
{
    public required BookReadingLog Log { get; init; }
    public int XpEarned { get; init; }

    public bool GoalJustReachedInPeriod { get; init; }

    public bool BookJustCompleted { get; init; }

    public int StreakAfterDays { get; init; }

    public DateTime PeriodStartLocal { get; init; }
}

public class BookService
{
    private const int XpPerLog = 3;

    public const int CompletionBonusXp = 25;

    private const int DailyGoalBonusXp = 5;

    private readonly AppDbContext _context;
    private readonly int _maxHistoryLookbackDays;

    public BookService(AppDbContext context)
        : this(context, Options.Create(new AppLimitsOptions()))
    {
    }

    public BookService(AppDbContext context, IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _maxHistoryLookbackDays = limits.Value.MaxHistoryLookbackDays;
    }

    public async Task<BookLogResult> AddReadingLogAsync(
        Book book,
        LogReadingDto dto,
        string? timeZoneId,
       
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var readDateUtc = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);

        var periodStart = HabitSchedule.PeriodStartLocal(readDateUtc, book.Period, tz);

        var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
        var totalBeforePeriod = totals.TryGetValue(periodStart, out var existing) ? existing : 0;

        var log = new BookReadingLog
        {
            BookId = book.Id,
            ReadDate = readDateUtc,
            Amount = dto.Amount,
            PageReachedAt = dto.PageReachedAt
            
        };

        var wasCompleted = book.IsCompleted;

        if (book.GoalType == BookGoalType.Pages)
        {
            if (dto.PageReachedAt.HasValue)
            {
                book.CurrentPage = dto.PageReachedAt.Value;
            }
            else
            {
                book.CurrentPage += dto.Amount;
            }

            if (book.TotalPages.HasValue && book.CurrentPage >= book.TotalPages.Value)
            {
                book.CurrentPage = book.TotalPages.Value;
                book.IsCompleted = true;
            }
        }
        else
        {
            book.TotalMinutesRead += dto.Amount;
        }

        var totalAfterPeriod = totalBeforePeriod + dto.Amount;
        var goalJustReachedInPeriod = book.DailyGoalAmount > 0
            && totalBeforePeriod < book.DailyGoalAmount
            && totalAfterPeriod >= book.DailyGoalAmount;

        var xpEarned = XpPerLog;
        if (goalJustReachedInPeriod)
        {
            xpEarned += DailyGoalBonusXp;
        }

        var bookJustCompleted = !wasCompleted && book.IsCompleted;
        if (bookJustCompleted)
        {
            book.CompletedAt = DateTime.UtcNow;
            book.CompletionBonusAwarded = true;
            xpEarned += CompletionBonusXp;
        }

        log.XpEarned = xpEarned;
        _context.BookReadingLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);

        var streakAfterDays = goalJustReachedInPeriod
            ? await CountStreakPeriodsAsync(book, tz, periodStart, cancellationToken)
            : 0;

        return new BookLogResult
        {
            Log = log,
            XpEarned = xpEarned,
            GoalJustReachedInPeriod = goalJustReachedInPeriod,
            BookJustCompleted = bookJustCompleted,
            StreakAfterDays = streakAfterDays,
            PeriodStartLocal = periodStart
        };
    }

    public async Task<int> CompleteManuallyAsync(Book book, CancellationToken cancellationToken = default)
    {
        if (book.IsCompleted)
        {
            return 0;
        }

        book.IsCompleted = true;
        book.ManuallyCompleted = true;
        book.CompletedAt = DateTime.UtcNow;

        var xpEarned = 0;
        if (!book.CompletionBonusAwarded)
        {
            book.CompletionBonusAwarded = true;
            xpEarned = CompletionBonusXp;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return xpEarned;
    }

    // NOT (madde 1): RecalculateBookAsync BİLİNÇLİ OLARAK
    // LoadPeriodTotalsAsync KULLANMIYOR — kendi sorgusuyla TÜM geçmişi
    // okuyor. XP'yi doğru yeniden hesaplamak için bu şart;
    // MaxHistoryLookbackDays sınırı burada uygulanmıyor.
    public async Task<int> RecalculateBookAsync(Book book, string? timeZoneId = null, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);

        var logs = await _context.BookReadingLogs
            .Where(l => l.BookId == book.Id)
            .OrderBy(l => l.ReadDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var oldTotalXp = logs.Sum(l => l.XpEarned);

        int currentPage = 0;
        int totalMinutes = 0;
        bool isCompleted = false;
        DateTime? completedAt = null;

        var dailyTotals = new Dictionary<DateTime, int>();

        foreach (var l in logs)
        {
            var periodKey = HabitSchedule.PeriodStartLocal(l.ReadDate, book.Period, tz);
            var beforePeriod = dailyTotals.TryGetValue(periodKey, out var existing) ? existing : 0;
            var afterPeriod = beforePeriod + l.Amount;
            dailyTotals[periodKey] = afterPeriod;

            var wasCompletedBeforeThisLog = isCompleted;

            if (book.GoalType == BookGoalType.Pages)
            {
                if (l.PageReachedAt.HasValue)
                {
                    currentPage = l.PageReachedAt.Value;
                }
                else
                {
                    currentPage += l.Amount;
                }

                if (book.TotalPages.HasValue && currentPage >= book.TotalPages.Value)
                {
                    currentPage = book.TotalPages.Value;
                    isCompleted = true;
                }
            }
            else
            {
                totalMinutes += l.Amount;
            }

            var goalJustReachedInPeriod = book.DailyGoalAmount > 0
                && beforePeriod < book.DailyGoalAmount
                && afterPeriod >= book.DailyGoalAmount;

            var bookJustCompletedByThisLog = !wasCompletedBeforeThisLog && isCompleted;

            var recalculatedXp = XpPerLog;
            if (goalJustReachedInPeriod) recalculatedXp += DailyGoalBonusXp;
            if (bookJustCompletedByThisLog)
            {
                recalculatedXp += CompletionBonusXp;
                completedAt = l.ReadDate;
            }

            l.XpEarned = recalculatedXp;
        }

        if (book.ManuallyCompleted)
        {
            isCompleted = true;
            completedAt ??= book.CompletedAt;
        }

        book.CurrentPage = currentPage;
        book.TotalMinutesRead = totalMinutes;
        book.IsCompleted = isCompleted;
        book.CompletedAt = isCompleted ? completedAt : null;

        var newTotalXp = logs.Sum(l => l.XpEarned);
        await _context.SaveChangesAsync(cancellationToken);

        return newTotalXp - oldTotalXp;
    }

    // DÜZELTİLDİ (🔵 madde 6 — sessiz veri kırpma sinyali): HistoryTruncated
    // artık DTO'ya set ediliyor. Book, AppLimits:MaxHistoryLookbackDays
    // sınırından daha eski bir tarihte oluşturulmuşsa, LoadPeriodTotalsAsync
    // tarafından okunan geçmiş book'un tüm geçmişini kapsamıyor demektir.
    public async Task<BookProgressDto> GetProgressAsync(Book book, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;

        var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
        var periodStart = HabitSchedule.PeriodStartLocal(now, book.Period, tz);
        var periodEnd = HabitSchedule.NextPeriodStartLocal(periodStart, book.Period);

        var periodAmount = totals.TryGetValue(periodStart, out var amount) ? amount : 0;
        var isGoalReached = book.DailyGoalAmount > 0 && periodAmount >= book.DailyGoalAmount;
        var percentage = book.DailyGoalAmount == 0 ? 0 : Math.Min(100, (double)periodAmount / book.DailyGoalAmount * 100);

        var cutoffUtc = now.AddDays(-_maxHistoryLookbackDays);
        var createdAtUtc = book.CreatedAt.Kind == DateTimeKind.Utc
            ? book.CreatedAt
            : DateTime.SpecifyKind(book.CreatedAt, DateTimeKind.Utc);

        return new BookProgressDto
        {
            BookId = book.Id,
            DailyGoalAmount = book.DailyGoalAmount,
            TodayAmount = periodAmount,
            IsGoalReachedToday = isGoalReached,
            PercentageCompletedToday = percentage,
            CurrentStreak = CountStreakFromTotals(totals, periodStart, book.DailyGoalAmount, book.CreatedAt, book.Period, tz),
            IsCompleted = book.IsCompleted,
            OverallPercentageCompleted = book.GoalType == BookGoalType.Pages && book.TotalPages is > 0
                ? Math.Min(100, (double)book.CurrentPage / book.TotalPages.Value * 100)
                : null,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            HistoryTruncated = createdAtUtc < cutoffUtc
        };
    }

    public async Task<List<BookDailyStatDto>> GetStatsAsync(
        Book book,
        string? timeZoneId,
        int periodsCount,
        HabitPeriod? granularity = null,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var effectivePeriod = granularity ?? book.Period;

        var totals = await LoadPeriodTotalsAsync(book.Id, effectivePeriod, tz, cancellationToken);
        var cursor = HabitSchedule.PeriodStartLocal(DateTime.UtcNow, effectivePeriod, tz);
        var stats = new List<BookDailyStatDto>();

        for (int i = 0; i < periodsCount; i++)
        {
            var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
            stats.Add(new BookDailyStatDto
            {
                Date = cursor,
                TotalAmount = total,
                GoalReached = book.DailyGoalAmount > 0 && total >= book.DailyGoalAmount
            });
            cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, effectivePeriod);
        }

        return stats;
    }

    // DÜZELTİLDİ (🔴 madde 2 — bellekte lookback filtreleme): Önceden bu
    // metod HER ZAMAN LoadPeriodTotalsBatchAsync'i MaxHistoryLookbackDays
    // (730 gün varsayılan) sınırıyla çağırıp, ardından cursor'ı geriye doğru
    // gezerek (while döngüsü) sadece lookbackDays kadarını sayıyordu — yani
    // kullanıcı "son 30 gün" istese bile SQL'den tüm MaxHistoryLookbackDays
    // penceresi çekiliyor, gerçek filtreleme sadece bellekte yapılıyordu.
    // Artık istenen lookbackDays'e göre yaklaşık bir üst sınır hesaplanıp,
    // DB'den MaxHistoryLookbackDays ile bu ikisinin DAHA SIKI (daha az veri
    // çeken) olanı kadar veri çekiliyor. lookbackDays küçükse çekilen veri
    // hacmi önemli ölçüde azalır; lookbackDays büyükse (MaxHistoryLookbackDays'i
    // aşıyorsa) davranış öncekiyle birebir aynı kalır.
    //
    // YENİ (🔵 madde 6): Her book için HistoryTruncated artık, book'un
    // oluşturulma tarihinin bu sorgu için kullanılan etkin cutoff'tan önce
    // olup olmadığına göre işaretleniyor — cutoff öncesi günler sessizce 0
    // sayıldığından completion rate gerçek olandan düşük görünebilir.
    public async Task<List<BookComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Book> books,
        string? timeZoneId,
        int lookbackDays = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;
        var results = new List<BookComparisonDto>(books.Count);

        if (books.Count == 0)
        {
            return results;
        }

        var effectiveCutoffUtc = ComputeEffectiveCutoffUtc(books, lookbackDays, now);

        var totalsByBook = new Dictionary<int, Dictionary<DateTime, int>>();
        foreach (var group in books.GroupBy(b => b.Period))
        {
            var bookIds = group.Select(b => b.Id).ToArray();
            var batch = await LoadPeriodTotalsBatchAsync(bookIds, group.Key, tz, effectiveCutoffUtc, cancellationToken);
            foreach (var kv in batch)
            {
                totalsByBook[kv.Key] = kv.Value;
            }
        }

        foreach (var book in books)
        {
            var totals = totalsByBook.TryGetValue(book.Id, out var t) ? t : new Dictionary<DateTime, int>();
            var bookToday = HabitSchedule.PeriodStartLocal(DateTime.UtcNow, book.Period, tz);
            var createdLocal = book.CreatedAt.Kind == DateTimeKind.Utc
                ? book.CreatedAt
                : DateTime.SpecifyKind(book.CreatedAt, DateTimeKind.Utc);
            var bookStart = HabitSchedule.PeriodStartLocal(createdLocal, book.Period, tz);

            var cursor = bookToday;
            int periodsConsidered = 0;
            int periodsGoalMet = 0;

            while (cursor >= bookStart && periodsConsidered < lookbackDays)
            {
                periodsConsidered++;
                var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
                if (book.DailyGoalAmount > 0 && total >= book.DailyGoalAmount)
                {
                    periodsGoalMet++;
                }

                cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, book.Period);
            }

            var completionRate = periodsConsidered == 0 ? 0 : (double)periodsGoalMet / periodsConsidered * 100;

            var truncated = createdLocal < effectiveCutoffUtc;

            results.Add(new BookComparisonDto
            {
                BookId = book.Id,
                Title = book.Title,
                CurrentStreak = CountStreakFromTotals(totals, bookToday, book.DailyGoalAmount, book.CreatedAt, book.Period, tz),
                CompletionRatePercent = Math.Round(completionRate, 1),
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

    // DÜZELTİLDİ (🔴 madde 1): MaxHistoryLookbackDays sınırı eklendi (bkz.
    // HabitProgressService.LoadPeriodTotalsAsync üzerindeki açıklama — aynı
    // gerekçe/desen burada da geçerli). Artık genel MaxHistoryLookbackDays
    // cutoff'unu hesaplayıp private overload'a devrediyor.
    public async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int bookId,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-_maxHistoryLookbackDays);
        return await LoadPeriodTotalsAsync(bookId, period, tz, cutoffUtc, cancellationToken);
    }

    // YENİ (🔴 madde 2): cutoff'u dışarıdan alabilen overload. GetComparisonAsync
    // gibi daha dar bir aralık isteyen çağıranlar bunu kullanır, böylece
    // MaxHistoryLookbackDays'ten daha az veri SQL'den çekilebilir.
    private async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int bookId,
        HabitPeriod period,
        TimeZoneInfo tz,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var rows = await _context.BookReadingLogs
            .AsNoTracking()
            .Where(l => l.BookId == bookId && l.ReadDate >= cutoffUtc)
            .Select(l => new { l.ReadDate, l.Amount })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<DateTime, int>();
        foreach (var row in rows)
        {
            var periodStart = HabitSchedule.PeriodStartLocal(row.ReadDate, period, tz);
            result[periodStart] = result.TryGetValue(periodStart, out var existing)
                ? existing + row.Amount
                : row.Amount;
        }

        return result;
    }

    // DÜZELTİLDİ (🔴 madde 1): MaxHistoryLookbackDays sınırı eklendi. Artık
    // genel cutoff'u hesaplayıp private overload'a devrediyor.
    public async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsBatchAsync(
        IReadOnlyList<int> bookIds,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-_maxHistoryLookbackDays);
        return await LoadPeriodTotalsBatchAsync(bookIds, period, tz, cutoffUtc, cancellationToken);
    }

    // YENİ (🔴 madde 2): cutoff'u dışarıdan alabilen overload; GetComparisonAsync
    // bunu daha dar (lookbackDays'e göre hesaplanmış) bir cutoff ile çağırır.
    private async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsBatchAsync(
        IReadOnlyList<int> bookIds,
        HabitPeriod period,
        TimeZoneInfo tz,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var result = bookIds.ToDictionary(id => id, _ => new Dictionary<DateTime, int>());
        if (bookIds.Count == 0)
        {
            return result;
        }

        var idsArray = bookIds.ToArray();
        var rows = await _context.BookReadingLogs
            .AsNoTracking()
            .Where(l => idsArray.Contains(l.BookId) && l.ReadDate >= cutoffUtc)
            .Select(l => new { l.BookId, l.ReadDate, l.Amount })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.BookId, out var dict))
            {
                dict = new Dictionary<DateTime, int>();
                result[row.BookId] = dict;
            }

            var periodStart = HabitSchedule.PeriodStartLocal(row.ReadDate, period, tz);
            dict[periodStart] = dict.TryGetValue(periodStart, out var existing)
                ? existing + row.Amount
                : row.Amount;
        }

        return result;
    }

    private async Task<int> CountStreakPeriodsAsync(Book book, TimeZoneInfo tz, DateTime fromPeriodStart, CancellationToken cancellationToken)
    {
        var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
        return CountStreakFromTotals(totals, fromPeriodStart, book.DailyGoalAmount, book.CreatedAt, book.Period, tz);
    }

    // YENİ (🔴 madde 2 yardımcı metodu): lookbackDays ve book'ların period
    // tiplerine göre yaklaşık bir "bu kadar günden eskisi gerekmiyor" tarihi
    // hesaplar. En kısıtlayıcı (en uzun) period tipi baz alınır (Monthly ~31
    // gün) — bu sayede farklı period'lardaki tüm book'lar için güvenle tek
    // bir cutoff kullanılabilir. Sonuç, mevcut MaxHistoryLookbackDays
    // cutoff'u ile karşılaştırılıp İKİSİNDEN DAHA YAKIN (daha az veri çeken)
    // olan seçilir; MaxHistoryLookbackDays sınırı hiçbir zaman aşılmaz.
    private DateTime ComputeEffectiveCutoffUtc(IReadOnlyList<Book> books, int lookbackDays, DateTime now)
    {
        var maxPeriod = books.Max(b => b.Period);
        var approxDaysPerPeriod = maxPeriod switch
        {
            HabitPeriod.Monthly => 31,
            HabitPeriod.Weekly => 7,
            _ => 1
        };

        // +2 pay: hafta/ay başlangıcı yuvarlamalarından kaynaklanabilecek
        // sınır hatalarına karşı küçük bir tolerans.
        var lookbackBasedCutoff = now.AddDays(-((long)approxDaysPerPeriod * lookbackDays + 2));
        var hardCutoff = now.AddDays(-_maxHistoryLookbackDays);

        // İki cutoff'tan DAHA GEÇ (yani daha az veri çeken, daha kısıtlayıcı)
        // olanı kullanılır.
        return lookbackBasedCutoff > hardCutoff ? lookbackBasedCutoff : hardCutoff;
    }

    public static int CountStreakFromTotals(
        IReadOnlyDictionary<DateTime, int> totals,
        DateTime fromPeriodStart,
        int dailyGoalAmount,
        DateTime createdAtUtc,
        HabitPeriod period,
        TimeZoneInfo tz)
    {
        if (dailyGoalAmount <= 0)
        {
            return 0;
        }

        var minStart = HabitSchedule.PeriodStartLocal(
            createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
            period, tz);

        int streak = 0;
        var cursor = fromPeriodStart;
        while (cursor >= minStart)
        {
            var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
            if (total < dailyGoalAmount)
            {
                break;
            }

            streak++;
            cursor = HabitSchedule.PreviousPeriodStartLocal(cursor, period);
        }

        return streak;
    }



public static BookDto ToDto(Book book) => new()
{
    Id = book.Id,
    Title = book.Title,
    Author = book.Author,
    GoalType = book.GoalType,
    Period = book.Period,
    TotalPages = book.TotalPages,
    DailyGoalAmount = book.DailyGoalAmount,
    CurrentPage = book.CurrentPage,
    TotalMinutesRead = book.TotalMinutesRead,
    IsCompleted = book.IsCompleted,
    PercentageCompleted = book.GoalType == BookGoalType.Pages && book.TotalPages is > 0
        ? Math.Min(100, (double)book.CurrentPage / book.TotalPages.Value * 100)
        : null,
    CreatedAt = book.CreatedAt,
    CompletedAt = book.CompletedAt,
    IsArchived = book.IsArchived,
    ArchivedAt = book.ArchivedAt,
    Notes = book.Notes,
    CoverImageUrl = book.CoverImageUrl
};

    public static BookReadingLogDto ToLogDto(BookReadingLog log) => new()
    {
        Id = log.Id,
        BookId = log.BookId,
        ReadDate = log.ReadDate,
        Amount = log.Amount,
        PageReachedAt = log.PageReachedAt,
        XpEarned = log.XpEarned
    };
}