using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Configuration;
using Asp.Versioning;

namespace Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Authorize]
public class BooksController : ControllerBase
{
    private const int MaxStatsPeriods = 366;
    private const int MaxComparisonPeriods = 366;

    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt", "title", "author"
    };

    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly BookService _bookService;
    private readonly BadgeService _badgeService;
    private readonly NotificationService _notificationService;
    private readonly IRecalculationQueue _recalculationQueue;
    private readonly ILogger<BooksController> _logger;
    private readonly int _maxBooksPerUser;

    public BooksController(
        AppDbContext context,
        UserManager<User> userManager,
        BookService bookService,
        BadgeService badgeService,
        NotificationService notificationService,
        IRecalculationQueue recalculationQueue,
        IOptions<AppLimitsOptions> limits,
        ILogger<BooksController> logger)
    {
        _context = context;
        _userManager = userManager;
        _bookService = bookService;
        _badgeService = badgeService;
        _notificationService = notificationService;
        _recalculationQueue = recalculationQueue;
        _maxBooksPerUser = limits.Value.MaxBooksPerUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<BookDto>>> GetBooks(
        int page = 1,
        int pageSize = 50,
        string? search = null,
        bool includeArchived = false,
        string sortBy = "createdAt",
        bool sortDesc = true)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;
        if (!AllowedSortFields.Contains(sortBy)) sortBy = "createdAt";

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = _context.Books.AsNoTracking().Where(b => b.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(b => !b.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b =>
                EF.Functions.ILike(b.Title, $"%{term}%") ||
                (b.Author != null && EF.Functions.ILike(b.Author, $"%{term}%")));
        }

        query = (sortBy.ToLowerInvariant(), sortDesc) switch
        {
            ("title", true) => query.OrderByDescending(b => b.Title),
            ("title", false) => query.OrderBy(b => b.Title),
            ("author", true) => query.OrderByDescending(b => b.Author),
            ("author", false) => query.OrderBy(b => b.Author),
            (_, false) => query.OrderBy(b => b.CreatedAt),
            _ => query.OrderByDescending(b => b.CreatedAt)
        };

        var totalCount = await query.CountAsync();
        var books = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<BookDto>
        {
            Items = books.Select(BookService.ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookDto>> GetBook(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (book == null)
        {
            return NotFound();
        }

        return BookService.ToDto(book);
    }

    [HttpPost]
    public async Task<ActionResult<BookDto>> CreateBook(CreateBookDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<BookDto>>(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"book:" + userId}))");

            if (await _context.Books.CountAsync(b => b.UserId == userId) >= _maxBooksPerUser)
            {
                return Conflict($"En fazla {_maxBooksPerUser} kitap oluşturabilirsiniz.");
            }

            var book = new Book
            {
                Title = dto.Title,
                Author = dto.Author,
                GoalType = dto.GoalType,
                Period = dto.Period,
                TotalPages = dto.TotalPages,
                DailyGoalAmount = dto.DailyGoalAmount,
                Notes = dto.Notes,
                CoverImageUrl = dto.CoverImageUrl,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Books.Add(book);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
            return BookService.ToDto(book);
        });
    }

    // DÜZELTİLDİ (🔴 madde 1): RecalculateBookAsync artık senkron çağrılmıyor.
    // BookDto zaten XP delta'sını dönmüyor, bu yüzden işi arka plana almak
    // kontratı bozmuyor (bkz. HabitsController.UpdateHabit üzerindeki
    // açıklama).
    [HttpPut("{id:int}")]
    public async Task<ActionResult<BookDto>> UpdateBook(int id, CreateBookDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        book.Title = dto.Title;
        book.Author = dto.Author;
        book.GoalType = dto.GoalType;
        book.Period = dto.Period;
        book.TotalPages = dto.TotalPages;
        book.DailyGoalAmount = dto.DailyGoalAmount;
        book.Notes = dto.Notes;
        book.CoverImageUrl = dto.CoverImageUrl;

        await _context.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId);
        _recalculationQueue.EnqueueBookRecalculation(book.Id, userId, user?.TimeZoneId);

        return BookService.ToDto(book);
    }

    [HttpPut("{id:int}/archive")]
    public async Task<ActionResult<BookDto>> ArchiveBook(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        if (!book.IsArchived)
        {
            book.IsArchived = true;
            book.ArchivedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return BookService.ToDto(book);
    }

    [HttpPut("{id:int}/unarchive")]
    public async Task<ActionResult<BookDto>> UnarchiveBook(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        if (book.IsArchived)
        {
            book.IsArchived = false;
            book.ArchivedAt = null;
            await _context.SaveChangesAsync();
        }

        return BookService.ToDto(book);
    }

    [HttpPost("{id:int}/complete")]
    public async Task<ActionResult<BookDto>> CompleteBook(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        if (book.IsCompleted)
        {
            return BadRequest("Bu kitap zaten tamamlanmış.");
        }

        if (book.GoalType == BookGoalType.Pages && book.TotalPages.HasValue)
        {
            return BadRequest("Sayfa bazlı ve toplam sayfa sayısı belirtilmiş kitaplar, hedef sayfaya ulaşıldığında otomatik olarak tamamlanır.");
        }

        // NOT: CompleteManuallyAsync O(1) — bilinen sabit bir bonus XP
        // ekliyor, geçmişi taramıyor. Bu yüzden senkron kalıyor (recalculation
        // kuyruğuna alınmıyor).
        var xpEarned = await _bookService.CompleteManuallyAsync(book);
        if (xpEarned != 0)
        {
            await ApplyXpDeltaAsync(userId, xpEarned);
        }

        await _notificationService.TryEnqueueAsync(
            userId,
            NotificationTypes.BookCompleted,
            "Kitap tamamlandı",
            MotivationMessages.BookCompleted(book.Title),
            habitId: null,
            dedupKey: $"bookcompleted:{book.Id}");

        return BookService.ToDto(book);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteBook(int id)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var totalXpFromLogs = await _context.BookReadingLogs
            .Where(l => l.BookId == id)
            .SumAsync(l => (int?)l.XpEarned) ?? 0;

        var manualCompletionXp = book.ManuallyCompleted && book.CompletionBonusAwarded
            ? BookService.CompletionBonusXp
            : 0;
        var totalXpToRemove = totalXpFromLogs + manualCompletionXp;

        _context.Books.Remove(book);
        await _context.SaveChangesAsync();

        if (totalXpToRemove != 0)
        {
            await ApplyXpDeltaAsync(userId, -totalXpToRemove);
        }

        await transaction.CommitAsync();
        return NoContent();
        });
    }

   [HttpPost("{id:int}/reading-logs")]
    public async Task<ActionResult<BookReadingLogDto>> LogReading(int id, LogReadingDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<BookReadingLogDto>>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (!string.IsNullOrWhiteSpace(dto.ClientRequestId))
        {
            var existingLog = await _context.BookReadingLogs.AsNoTracking()
                .FirstOrDefaultAsync(l => l.BookId == id && l.ClientRequestId == dto.ClientRequestId);
            if (existingLog != null)
            {
                return BookService.ToLogDto(existingLog);
            }
        }

        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var readDateUtc = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);
        if (readDateUtc.Date < book.CreatedAt.Date)
        {
            return BadRequest("Okuma tarihi, kitabın oluşturulma tarihinden önce olamaz.");
        }

        _context.Entry(book).Property(b => b.ConcurrencyToken).IsModified = true;

        var user = await _userManager.FindByIdAsync(userId);

        // NOT: AddReadingLogAsync burada BİLİNÇLİ OLARAK senkron kalıyor —
        // recalculation'ın aksine (tüm geçmişi yeniden işlemiyor), sadece
        // TEK bir yeni log ekliyor ve o log için XP hesaplıyor. O(1)'e yakın
        // bir işlem, arka plana alınmasına gerek yok.
        BookLogResult result;
        try
        {
            result = await _bookService.AddReadingLogAsync(book, dto, user?.TimeZoneId, clientRequestId: dto.ClientRequestId);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(dto.ClientRequestId))
        {
            var raced = await _context.BookReadingLogs.AsNoTracking()
                .FirstOrDefaultAsync(l => l.BookId == id && l.ClientRequestId == dto.ClientRequestId);
            if (raced != null)
            {
                return BookService.ToLogDto(raced);
            }
            throw;
        }

        if (user != null && result.XpEarned != 0)
        {
            user.TotalXp += result.XpEarned;
            var updateResult = await _userManager.UpdateAsync(user);
            updateResult.EnsureSucceeded(_logger, "book-reading-log-xp", userId);
        }

        await _badgeService.EvaluateAfterBookLogAsync(userId, result.StreakAfterDays);

        if (result.GoalJustReachedInPeriod)
        {
            var periodKey = result.PeriodStartLocal.ToString("yyyy-MM-dd");
            await _notificationService.TryEnqueueAsync(
                userId,
                NotificationTypes.BookGoalReached,
                "Okuma hedefi tamamlandı",
                MotivationMessages.BookGoalReached(book.Title),
                habitId: null,
                dedupKey: $"bookgoal:{book.Id}:{periodKey}");
        }

        if (result.BookJustCompleted)
        {
            await _notificationService.TryEnqueueAsync(
                userId,
                NotificationTypes.BookCompleted,
                "Kitap tamamlandı",
                MotivationMessages.BookCompleted(book.Title),
                habitId: null,
                dedupKey: $"bookcompleted:{book.Id}");
        }

        await transaction.CommitAsync();
        return BookService.ToLogDto(result.Log);
        });
    }

    [HttpGet("{id:int}/reading-logs")]
    public async Task<ActionResult<PagedResultDto<BookReadingLogDto>>> GetReadingLogs(
        int id, int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (book == null)
        {
            return NotFound();
        }

        var query = _context.BookReadingLogs.AsNoTracking()
            .Where(l => l.BookId == id)
            .OrderByDescending(l => l.ReadDate);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new BookReadingLogDto
            {
                Id = l.Id,
                BookId = l.BookId,
                ReadDate = l.ReadDate,
                Amount = l.Amount,
                PageReachedAt = l.PageReachedAt,
                XpEarned = l.XpEarned
            })
            .ToListAsync();

        return new PagedResultDto<BookReadingLogDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    [HttpGet("{id:int}/reading-logs/{logId:int}")]
    public async Task<ActionResult<BookReadingLogDto>> GetReadingLog(int id, int logId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var log = await _context.BookReadingLogs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == logId && l.BookId == id);
        if (log == null)
        {
            return NotFound();
        }

        return BookService.ToLogDto(log);
    }

    // DÜZELTİLDİ (🔴 madde 1): RecalculateBookAsync artık senkron çağrılmıyor;
    // sadece kuyruğa yazmak için gereken timezone bilgisi commit'ten önce
    // okunuyor, gerçek recalculation transaction dışında arka planda
    // işleniyor.
    [HttpPut("{id:int}/reading-logs/{logId:int}")]
    public async Task<ActionResult<BookReadingLogDto>> UpdateReadingLog(int id, int logId, LogReadingDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<BookReadingLogDto>>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var newReadDateUtc = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);
        if (newReadDateUtc.Date < book.CreatedAt.Date)
        {
            return BadRequest("Okuma tarihi, kitabın oluşturulma tarihinden önce olamaz.");
        }

        _context.Entry(book).Property(b => b.ConcurrencyToken).IsModified = true;

        var log = await _context.BookReadingLogs.FirstOrDefaultAsync(l => l.Id == logId && l.BookId == id);
        if (log == null)
        {
            return NotFound();
        }

        log.ReadDate = newReadDateUtc;
        log.Amount = dto.Amount;
        log.PageReachedAt = dto.PageReachedAt;
        await _context.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId);
        var userTimeZoneId = user?.TimeZoneId;

        await transaction.CommitAsync();

        _recalculationQueue.EnqueueBookRecalculation(book.Id, userId, userTimeZoneId);

        return BookService.ToLogDto(log);
        });
    }


    // DÜZELTİLDİ (🔴 madde 1): Silme sonrası da RecalculateBookAsync artık
    // arka plana alınıyor.
    [HttpDelete("{id:int}/reading-logs/{logId:int}")]
    public async Task<ActionResult> DeleteReadingLog(int id, int logId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        _context.Entry(book).Property(b => b.ConcurrencyToken).IsModified = true;

        var log = await _context.BookReadingLogs.FirstOrDefaultAsync(l => l.Id == logId && l.BookId == id);
        if (log == null)
        {
            return NotFound();
        }

        _context.BookReadingLogs.Remove(log);
        await _context.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(userId);
        var userTimeZoneId = user?.TimeZoneId;

        await transaction.CommitAsync();

        _recalculationQueue.EnqueueBookRecalculation(book.Id, userId, userTimeZoneId);

        return NoContent();
        });
    }

    [HttpGet("{id:int}/progress")]
    public async Task<ActionResult<BookProgressDto>> GetProgress(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId);
        return await _bookService.GetProgressAsync(book, user?.TimeZoneId);
    }

    [HttpGet("{id:int}/stats")]
    public async Task<ActionResult<IEnumerable<BookDailyStatDto>>> GetStats(int id, int days = 7, string? granularity = null)
    {
        if (days is <= 0 or > MaxStatsPeriods)
        {
            return BadRequest($"days parametresi 1 ile {MaxStatsPeriods} arasında olmalıdır.");
        }

        HabitPeriod? parsedGranularity = null;
        if (!string.IsNullOrWhiteSpace(granularity))
        {
            if (!Enum.TryParse<HabitPeriod>(granularity, true, out var g))
            {
                return BadRequest("granularity parametresi Daily, Weekly veya Monthly olmalıdır.");
            }
            parsedGranularity = g;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId);
        return await _bookService.GetStatsAsync(book, user?.TimeZoneId, days, parsedGranularity);
    }

    [HttpGet("comparison")]
    public async Task<ActionResult<IEnumerable<BookComparisonDto>>> GetComparison(int lookbackDays = 30, bool includeArchived = false)
    {
        if (lookbackDays is <= 0 or > MaxComparisonPeriods)
        {
            return BadRequest($"lookbackDays parametresi 1 ile {MaxComparisonPeriods} arasında olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = _context.Books.AsNoTracking().Where(b => b.UserId == userId);
        if (!includeArchived)
        {
            query = query.Where(b => !b.IsArchived);
        }
        var books = await query.ToListAsync();

        if (books.Count == 0)
        {
            return Ok(new List<BookComparisonDto>());
        }

        var user = await _userManager.FindByIdAsync(userId);
        var result = await _bookService.GetComparisonAsync(books, user?.TimeZoneId, lookbackDays);
        return Ok(result);
    }

    private async Task ApplyXpDeltaAsync(string userId, int xpDelta, User? preloadedUser = null)
    {
        var user = preloadedUser ?? await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return;
        }

        user.TotalXp = Math.Max(0, user.TotalXp + xpDelta);
        var updateResult = await _userManager.UpdateAsync(user);
        updateResult.EnsureSucceeded(_logger, "book-xp-delta", userId);
    }
}