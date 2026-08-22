using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class BookLogResult
{
    public required BookReadingLog Log { get; init; }
    public int XpEarned { get; init; }

    // DÜZELTİLDİ: "GoalJustReachedToday" -> "GoalJustReachedInPeriod" olarak
    // yeniden adlandırıldı. Artık kitabın DailyGoalAmount hedefi, sadece "gün"
    // değil Book.Period'a (Daily/Weekly/Monthly) göre bir dönem için
    // değerlendiriliyor.
    public bool GoalJustReachedInPeriod { get; init; }

    public bool BookJustCompleted { get; init; }

    // Dönemsel hedefin art arda kaç dönemdir (bugün dahil) tutturulduğu.
    public int StreakAfterDays { get; init; }

    // YENİ: Bildirim dedup key'i için bu kaydın ait olduğu dönemin (Period'a
    // göre) yerel başlangıç tarihi.
    public DateTime PeriodStartLocal { get; init; }
}

public class BookService
{
    // Her okuma kaydı için sabit XP (habit completion'lardaki mantığa paralel)
    private const int XpPerLog = 3;

    // Kitap tamamlandığında ekstra bonus XP.
    public const int CompletionBonusXp = 25;

    // Dönemsel hedef tutturulduğunda ekstra bonus XP (Habit'teki XpBonusForGoal'e paralel)
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
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var readDateUtc = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);

        // DÜZELTİLDİ: Artık "gün" yerine kitabın kendi Period'una (Daily/Weekly/
        // Monthly) göre dönem başlangıcı kullanılıyor — Habit'teki HabitSchedule
        // mantığıyla birebir tutarlı. Böylece "ay veya hafta için okuma hedefi"
        // senaryosu gerçekten desteklenmiş oluyor.
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

    public async Task<int> RecalculateBookAsync(Book book, CancellationToken cancellationToken = default)
    {
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

        // NOT: RecalculateBookAsync'e kullanıcı saat dilimi parametre olarak
        // geçmiyor (BookReadingLog'lar UTC saklanıyor). Period sınırlarını
        // hesaplarken burada UTC tabanlı bir gruplama kullanılıyor; bu, kullanıcı
        // saat dilimine göre dönem sınırında ±1 günlük kaymalara yol açabilir.
        // Tam doğruluk isteniyorsa bu metoda timeZoneId parametresi eklenip
        // AddReadingLogAsync'teki gibi TimeZones.Resolve ile gerçek tz kullanılmalı.
        var dailyTotals = new Dictionary<DateTime, int>();

        foreach (var l in logs)
        {
            var periodKey = HabitSchedule.PeriodStartLocal(l.ReadDate, book.Period, TimeZoneInfo.Utc);
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

    // DÜZELTİLDİ: granularity eklendi. Verilmezse kitabın kendi Period'u
    // kullanılır; verilirse (Daily/Weekly/Monthly) kullanıcı, kitabın hedef
    // periyodundan bağımsız olarak istediği eksende istatistik görebilir
    // (dokümandaki "haftalık ve aylık grafiklerle gösterilir" maddesi — önceden
    // sadece kitabın kendi sabit periyoduna göre veri dönüyordu).
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

    public async Task<List<BookComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Book> books,
        string? timeZoneId,
        int lookbackDays = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var results = new List<BookComparisonDto>(books.Count);

        foreach (var book in books)
        {
            var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
            var today = HabitSchedule.PeriodStartLocal(DateTime.UtcNow, book.Period, tz);
            var createdLocal = book.CreatedAt.Kind == DateTimeKind.Utc
                ? book.CreatedAt
                : DateTime.SpecifyKind(book.CreatedAt, DateTimeKind.Utc);
            var bookStart = HabitSchedule.PeriodStartLocal(createdLocal, book.Period, tz);

            var cursor = today;
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
                CurrentStreak = CountStreakFromTotals(totals, today, book.DailyGoalAmount, book.CreatedAt, book.Period, tz),
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

    private async Task<Dictionary<DateTime, int>> LoadPeriodTotalsAsync(
        int bookId,
        HabitPeriod period,
        TimeZoneInfo tz,
        CancellationToken cancellationToken)
    {
        var rows = await _context.BookReadingLogs
            .AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Select(l => new { l.ReadDate, l.Amount })
            .ToListAsync(cancellationToken);

        var totals = new Dictionary<DateTime, int>();
        foreach (var row in rows)
        {
            var key = HabitSchedule.PeriodStartLocal(row.ReadDate, period, tz);
            totals[key] = totals.TryGetValue(key, out var existing) ? existing + row.Amount : row.Amount;
        }

        return totals;
    }

    private async Task<int> CountStreakPeriodsAsync(Book book, TimeZoneInfo tz, DateTime fromPeriodStart, CancellationToken cancellationToken)
    {
        var totals = await LoadPeriodTotalsAsync(book.Id, book.Period, tz, cancellationToken);
        return CountStreakFromTotals(totals, fromPeriodStart, book.DailyGoalAmount, book.CreatedAt, book.Period, tz);
    }

    private static int CountStreakFromTotals(
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
        CompletedAt = book.CompletedAt
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