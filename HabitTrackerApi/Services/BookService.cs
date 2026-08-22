using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class BookLogResult
{
    public required BookReadingLog Log { get; init; }
    public int XpEarned { get; init; }

    // YENİ: Bu kayıt, o günün DailyGoalAmount hedefini az önce tutturdu mu?
    public bool GoalJustReachedToday { get; init; }

    // YENİ: Bu kayıt kitabı az önce tamamladı mı (toplam sayfa/dakika hedefi)?
    public bool BookJustCompleted { get; init; }

    // YENİ: Günlük hedefin art arda kaç gündür tutturulduğu (bugün dahil).
    public int StreakAfterDays { get; init; }
}

public class BookService
{
    // Her okuma kaydı için sabit XP (habit completion'lardaki mantığa paralel)
    private const int XpPerLog = 3;

    // Kitap tamamlandığında ekstra bonus XP
    private const int CompletionBonusXp = 25;

    // YENİ: Günlük hedef tutturulduğunda ekstra bonus XP (Habit'teki XpBonusForGoal'e paralel)
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
        var localDate = TimeZones.ToLocal(readDateUtc, tz).Date;

        // YENİ: Bu güne ait, bu kayıttan ÖNCEKİ toplam miktarı hesapla (DailyGoalAmount kontrolü için).
        var totalBeforeToday = await _context.BookReadingLogs
            .Where(l => l.BookId == book.Id && l.ReadDate.Date == localDate)
            .SumAsync(l => (int?)l.Amount, cancellationToken) ?? 0;

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

        var totalAfterToday = totalBeforeToday + dto.Amount;
        var goalJustReachedToday = book.DailyGoalAmount > 0
            && totalBeforeToday < book.DailyGoalAmount
            && totalAfterToday >= book.DailyGoalAmount;

        var xpEarned = XpPerLog;
        if (goalJustReachedToday)
        {
            xpEarned += DailyGoalBonusXp;
        }

        var bookJustCompleted = !wasCompleted && book.IsCompleted;
        if (bookJustCompleted)
        {
            book.CompletedAt = DateTime.UtcNow;
            xpEarned += CompletionBonusXp;
        }

        log.XpEarned = xpEarned;
        _context.BookReadingLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);

        var streakAfterDays = goalJustReachedToday
            ? await CountStreakDaysAsync(book, tz, localDate, cancellationToken)
            : 0;

        return new BookLogResult
        {
            Log = log,
            XpEarned = xpEarned,
            GoalJustReachedToday = goalJustReachedToday,
            BookJustCompleted = bookJustCompleted,
            StreakAfterDays = streakAfterDays
        };
    }

    /// <summary>
    /// YENİ: Bir okuma kaydı güncellendiğinde veya silindiğinde kitabın CurrentPage /
    /// TotalMinutesRead / IsCompleted / CompletedAt alanlarını TÜM kayıtlardan sıfırdan
    /// yeniden hesaplar ve kullanıcının TotalXp'sinde gerekli düzeltmeyi (delta) döner.
    /// </summary>
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

        // Günlük toplamlar (yeni XpEarned/goal hesaplaması için)
        var dailyTotals = new Dictionary<DateTime, int>();

        foreach (var l in logs)
        {
            var localDate = l.ReadDate.Date;
            var beforeToday = dailyTotals.TryGetValue(localDate, out var existing) ? existing : 0;
            var afterToday = beforeToday + l.Amount;
            dailyTotals[localDate] = afterToday;

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

            var goalJustReachedToday = book.DailyGoalAmount > 0
                && beforeToday < book.DailyGoalAmount
                && afterToday >= book.DailyGoalAmount;

            var bookJustCompletedByThisLog = !wasCompletedBeforeThisLog && isCompleted;

            var recalculatedXp = XpPerLog;
            if (goalJustReachedToday) recalculatedXp += DailyGoalBonusXp;
            if (bookJustCompletedByThisLog)
            {
                recalculatedXp += CompletionBonusXp;
                completedAt = l.ReadDate;
            }

            l.XpEarned = recalculatedXp;
        }

        book.CurrentPage = currentPage;
        book.TotalMinutesRead = totalMinutes;
        book.IsCompleted = isCompleted;
        book.CompletedAt = isCompleted ? completedAt : null;

        var newTotalXp = logs.Sum(l => l.XpEarned);
        await _context.SaveChangesAsync(cancellationToken);

        // Kullanıcının TotalXp'sine uygulanması gereken fark (pozitif ya da negatif).
        return newTotalXp - oldTotalXp;
    }

    public async Task<BookProgressDto> GetProgressAsync(Book book, string? timeZoneId, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var now = DateTime.UtcNow;
        var todayLocal = TimeZones.ToLocal(now, tz).Date;

        var totals = await LoadDailyTotalsAsync(book.Id, cancellationToken);
        var todayAmount = totals.TryGetValue(todayLocal, out var amount) ? amount : 0;
        var isGoalReachedToday = book.DailyGoalAmount > 0 && todayAmount >= book.DailyGoalAmount;
        var percentageToday = book.DailyGoalAmount == 0 ? 0 : Math.Min(100, (double)todayAmount / book.DailyGoalAmount * 100);

        return new BookProgressDto
        {
            BookId = book.Id,
            DailyGoalAmount = book.DailyGoalAmount,
            TodayAmount = todayAmount,
            IsGoalReachedToday = isGoalReachedToday,
            PercentageCompletedToday = percentageToday,
            CurrentStreak = CountStreakFromTotals(totals, todayLocal, book.DailyGoalAmount, book.CreatedAt, tz),
            IsCompleted = book.IsCompleted,
            OverallPercentageCompleted = book.GoalType == BookGoalType.Pages && book.TotalPages is > 0
                ? Math.Min(100, (double)book.CurrentPage / book.TotalPages.Value * 100)
                : null,
            PeriodStart = todayLocal,
            PeriodEnd = todayLocal.AddDays(1)
        };
    }

    public async Task<List<BookDailyStatDto>> GetStatsAsync(Book book, string? timeZoneId, int days, CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var totals = await LoadDailyTotalsAsync(book.Id, cancellationToken);
        var cursor = TimeZones.ToLocal(DateTime.UtcNow, tz).Date;
        var stats = new List<BookDailyStatDto>();

        for (int i = 0; i < days; i++)
        {
            var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
            stats.Add(new BookDailyStatDto
            {
                Date = cursor,
                TotalAmount = total,
                GoalReached = book.DailyGoalAmount > 0 && total >= book.DailyGoalAmount
            });
            cursor = cursor.AddDays(-1);
        }

        return stats;
    }

    /// <summary>
    /// YENİ: Kullanıcının kitaplarını, son <paramref name="lookbackDays"/> gündeki
    /// günlük hedef tutturma oranına göre "en iyi sürdürülenden en kötüye" sıralar
    /// (Habit'teki GetComparisonAsync ile paralel mantık).
    /// </summary>
    public async Task<List<BookComparisonDto>> GetComparisonAsync(
        IReadOnlyList<Book> books,
        string? timeZoneId,
        int lookbackDays = 30,
        CancellationToken cancellationToken = default)
    {
        var tz = TimeZones.Resolve(timeZoneId);
        var today = TimeZones.ToLocal(DateTime.UtcNow, tz).Date;
        var results = new List<BookComparisonDto>(books.Count);

        foreach (var book in books)
        {
            var totals = await LoadDailyTotalsAsync(book.Id, cancellationToken);
            var createdLocal = TimeZones.ToLocal(
                book.CreatedAt.Kind == DateTimeKind.Utc ? book.CreatedAt : DateTime.SpecifyKind(book.CreatedAt, DateTimeKind.Utc),
                tz).Date;

            var cursor = today;
            int periodsConsidered = 0;
            int periodsGoalMet = 0;

            while (cursor >= createdLocal && periodsConsidered < lookbackDays)
            {
                periodsConsidered++;
                var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
                if (book.DailyGoalAmount > 0 && total >= book.DailyGoalAmount)
                {
                    periodsGoalMet++;
                }

                cursor = cursor.AddDays(-1);
            }

            var completionRate = periodsConsidered == 0 ? 0 : (double)periodsGoalMet / periodsConsidered * 100;

            results.Add(new BookComparisonDto
            {
                BookId = book.Id,
                Title = book.Title,
                CurrentStreak = CountStreakFromTotals(totals, today, book.DailyGoalAmount, book.CreatedAt, tz),
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

    private async Task<Dictionary<DateTime, int>> LoadDailyTotalsAsync(int bookId, CancellationToken cancellationToken)
    {
        var rows = await _context.BookReadingLogs
            .AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Select(l => new { l.ReadDate, l.Amount })
            .ToListAsync(cancellationToken);

        var totals = new Dictionary<DateTime, int>();
        foreach (var row in rows)
        {
            var key = row.ReadDate.Date;
            totals[key] = totals.TryGetValue(key, out var existing) ? existing + row.Amount : row.Amount;
        }

        return totals;
    }

    private async Task<int> CountStreakDaysAsync(Book book, TimeZoneInfo tz, DateTime fromLocalDate, CancellationToken cancellationToken)
    {
        var totals = await LoadDailyTotalsAsync(book.Id, cancellationToken);
        return CountStreakFromTotals(totals, fromLocalDate, book.DailyGoalAmount, book.CreatedAt, tz);
    }

    private static int CountStreakFromTotals(
        IReadOnlyDictionary<DateTime, int> totals,
        DateTime fromLocalDate,
        int dailyGoalAmount,
        DateTime createdAtUtc,
        TimeZoneInfo tz)
    {
        if (dailyGoalAmount <= 0)
        {
            return 0;
        }

        var minDate = TimeZones.ToLocal(
            createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
            tz).Date;

        int streak = 0;
        var cursor = fromLocalDate;
        while (cursor >= minDate)
        {
            var total = totals.TryGetValue(cursor, out var amount) ? amount : 0;
            if (total < dailyGoalAmount)
            {
                break;
            }

            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    public static BookDto ToDto(Book book) => new()
    {
        Id = book.Id,
        Title = book.Title,
        Author = book.Author,
        GoalType = book.GoalType,
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