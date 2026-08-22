using Data;
using Dtos;
using Models;

namespace Services;

public class BookService
{
    // Her okuma kaydı için sabit XP (habit completion'lardaki mantığa paralel)
    private const int XpPerLog = 3;

    // Kitap tamamlandığında ekstra bonus XP
    private const int CompletionBonusXp = 25;

    private readonly AppDbContext _context;

    public BookService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(BookReadingLog Log, int XpEarned)> AddReadingLogAsync(
        Book book,
        LogReadingDto dto,
        CancellationToken cancellationToken = default)
    {
        var readDateUtc = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);

        var log = new BookReadingLog
        {
            BookId = book.Id,
            ReadDate = readDateUtc,
            Amount = dto.Amount,
            PageReachedAt = dto.PageReachedAt
        };
        _context.BookReadingLogs.Add(log);

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

        var xpEarned = XpPerLog;
        if (!wasCompleted && book.IsCompleted)
        {
            book.CompletedAt = DateTime.UtcNow;
            xpEarned += CompletionBonusXp;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return (log, xpEarned);
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
        PageReachedAt = log.PageReachedAt
    };
}