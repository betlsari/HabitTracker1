using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
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

    public BookService(AppDbContext context)
    {
        _context = context;
    }
    public async Task<BookLogResult> AddReadingLogAsync(
        Book book,
        LogReadingDto dto,
        string? timeZoneId,
        string? clientRequestId = null,
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
            PageReachedAt = dto.PageReachedAt,
            // YENİ (madde 6): idempotency anahtarı log ile birlikte kalıcı.
            ClientRequestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId
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
            PeriodEnd = periodEnd
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

    // DÜZELTİLDİ (N+1): Önceden her kitap için ayrı LoadPeriodTotalsAsync
    // çağrılıyordu. Artık kitaplar Period'a göre gruplanıp her grup için TEK
    // toplu sorgu atılıyor (HabitProgressService.GetComparisonAsync ile aynı
    // desen) — 200 kitap olsa bile en fazla 3 sorgu koşar.
    public async Task<List<BookComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Book> books,
        string? timeZoneId,
        int lookbackDays = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var results = new List<BookComparisonDto>(books.Count);

        if (books.Count == 0)
        {
            return results;
        }

        var totalsByBook = new Dictionary<int, Dictionary<DateTime, int>>();
        foreach (var group in books.GroupBy(b => b.Period))
        {
            var bookIds = group.Select(b => b.Id).ToArray();
            var batch = await LoadPeriodTotalsBatchAsync(bookIds, group.Key, tz, cancellationToken);
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

            results.Add(new BookComparisonDto
            {
                BookId = book.Id,
                Title = book.Title,
                CurrentStreak = CountStreakFromTotals(totals, bookToday, book.DailyGoalAmount, book.CreatedAt, book.Period, tz),
                CompletionRatePercent = Math.Round(completionRate, 1)
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

    // DÜZELTİLDİ: public yapıldı — ReminderBackgroundService.SendBookMissedAsync
    // kitapların dönemsel toplamlarını okuyabilmek için buna ihtiyaç duyuyor
    // (HabitProgressService.LoadPeriodTotalsAsync'in zaten public olmasıyla
    // aynı desen).
    public async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int bookId,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Database.SqlQuery<PeriodTotal>($"""
            SELECT date_trunc({GetDateTruncUnit(period)}, "ReadDate" AT TIME ZONE {tz.Id}) AS "PeriodStart",
                   COALESCE(SUM("Amount"), 0)::integer AS "Total"
            FROM "BookReadingLogs"
            WHERE "BookId" = {bookId}
            GROUP BY 1
            """).ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.PeriodStart, row => row.Total);
    }

    // YENİ: Birden fazla kitap için TEK sorguda dönemsel toplamları yükler.
    // HabitProgressService.LoadPeriodTotalsBatchAsync ile birebir aynı desen.
    public async Task<Dictionary<int, Dictionary<DateTime, int>>> LoadPeriodTotalsBatchAsync(
        IReadOnlyList<int> bookIds,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken = default)
    {
        var result = bookIds.ToDictionary(id => id, _ => new Dictionary<DateTime, int>());
        if (bookIds.Count == 0)
        {
            return result;
        }

        var idsArray = bookIds.ToArray();
        var rows = await _context.Database.SqlQuery<BookPeriodTotal>($"""
            SELECT "BookId",
                   date_trunc({GetDateTruncUnit(period)}, "ReadDate" AT TIME ZONE {tz.Id}) AS "PeriodStart",
                   COALESCE(SUM("Amount"), 0)::integer AS "Total"
            FROM "BookReadingLogs"
            WHERE "BookId" = ANY({idsArray})
            GROUP BY "BookId", 2
            """).ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.BookId, out var dict))
            {
                dict = new Dictionary<DateTime, int>();
                result[row.BookId] = dict;
            }

            dict[row.PeriodStart] = row.Total;
        }

        return result;
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

    private sealed class BookPeriodTotal
    {
        public int BookId { get; init; }
        public DateTime PeriodStart { get; init; }
        public int Total { get; init; }
    }

    private async Task<int> CountStreakPeriodsAsync(Book book, TimeZoneInfo tz, DateTime fromPeriodStart, CancellationToken cancellationToken)
    {
        var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
        return CountStreakFromTotals(totals, fromPeriodStart, book.DailyGoalAmount, book.CreatedAt, book.Period, tz);
    }

    // DÜZELTİLDİ: public static yapıldı — ReminderBackgroundService'in bir
    // önceki dönemdeki streak'i (bozulan zincir uzunluğunu) hesaplayabilmesi
    // için (HabitProgressService.CountStreak ile aynı desen).
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