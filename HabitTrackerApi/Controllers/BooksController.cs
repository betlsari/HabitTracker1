using Data;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using System.Security.Claims;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BooksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly BookService _bookService;
    private readonly BadgeService _badgeService;
    private readonly NotificationService _notificationService;

    public BooksController(
        AppDbContext context,
        UserManager<User> userManager,
        BookService bookService,
        BadgeService badgeService,
        NotificationService notificationService)
    {
        _context = context;
        _userManager = userManager;
        _bookService = bookService;
        _badgeService = badgeService;
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<BookDto>>> GetBooks(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = _context.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt);

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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var book = new Book
        {
            Title = dto.Title,
            Author = dto.Author,
            GoalType = dto.GoalType,
            // YENİ: Kitabın hedef dönemi (Daily/Weekly/Monthly) artık oluşturma
            // sırasında kaydediliyor.
            Period = dto.Period,
            TotalPages = dto.TotalPages,
            DailyGoalAmount = dto.DailyGoalAmount,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        return BookService.ToDto(book);
    }

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
        // YENİ: Period de güncellenebiliyor; değişirse RecalculateBookAsync
        // ilerlemeyi/streak'leri yeni Period'a göre baştan hesaplar.
        book.Period = dto.Period;
        book.TotalPages = dto.TotalPages;
        book.DailyGoalAmount = dto.DailyGoalAmount;

        await _context.SaveChangesAsync();

        // GoalType/TotalPages/Period değişmiş olabileceğinden ilerlemeyi yeniden hesapla.
        var xpDelta = await _bookService.RecalculateBookAsync(book);
        if (xpDelta != 0)
        {
            await ApplyXpDeltaAsync(userId, xpDelta);
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

        var xpEarned = await _bookService.CompleteManuallyAsync(book);
        if (xpEarned != 0)
        {
            await ApplyXpDeltaAsync(userId, xpEarned);
        }

        // DÜZELTİLDİ: Sabit tek bir mesaj yerine, çeşitlendirilmiş motivasyon
        // mesajlarından biri rastgele seçiliyor.
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

        return NoContent();
    }

    [HttpPost("{id:int}/reading-logs")]
    public async Task<ActionResult<BookReadingLogDto>> LogReading(int id, LogReadingDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId);
        var result = await _bookService.AddReadingLogAsync(book, dto, user?.TimeZoneId);

        if (user != null && result.XpEarned != 0)
        {
            user.TotalXp += result.XpEarned;
            await _userManager.UpdateAsync(user);
        }

        await _badgeService.EvaluateAfterBookLogAsync(userId, result.StreakAfterDays);

        // DÜZELTİLDİ: result.GoalJustReachedToday -> result.GoalJustReachedInPeriod
        // (artık gün değil, kitabın Period'una göre dönem). Dedup key de log
        // tarihinin günü yerine dönemin gerçek başlangıcına göre kuruluyor,
        // aksi halde haftalık/aylık hedefte aynı dönem içinde farklı günlerde
        // birden fazla "hedef tamamlandı" bildirimi gidebilirdi.
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

        return BookService.ToLogDto(result.Log);
    }

    [HttpGet("{id:int}/reading-logs")]
    public async Task<ActionResult<IEnumerable<BookReadingLogDto>>> GetReadingLogs(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (book == null)
        {
            return NotFound();
        }

        return await _context.BookReadingLogs.AsNoTracking()
            .Where(l => l.BookId == id)
            .OrderByDescending(l => l.ReadDate)
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
    }

    [HttpPut("{id:int}/reading-logs/{logId:int}")]
    public async Task<ActionResult<BookReadingLogDto>> UpdateReadingLog(int id, int logId, LogReadingDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var log = await _context.BookReadingLogs.FirstOrDefaultAsync(l => l.Id == logId && l.BookId == id);
        if (log == null)
        {
            return NotFound();
        }

        log.ReadDate = DateTime.SpecifyKind(dto.ReadDate, DateTimeKind.Utc);
        log.Amount = dto.Amount;
        log.PageReachedAt = dto.PageReachedAt;
        await _context.SaveChangesAsync();

        var xpDelta = await _bookService.RecalculateBookAsync(book);
        if (xpDelta != 0)
        {
            await ApplyXpDeltaAsync(userId, xpDelta);
        }

        return BookService.ToLogDto(log);
    }

    [HttpDelete("{id:int}/reading-logs/{logId:int}")]
    public async Task<ActionResult> DeleteReadingLog(int id, int logId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var book = await _context.Books.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (book == null)
        {
            return NotFound();
        }

        var log = await _context.BookReadingLogs.FirstOrDefaultAsync(l => l.Id == logId && l.BookId == id);
        if (log == null)
        {
            return NotFound();
        }

        _context.BookReadingLogs.Remove(log);
        await _context.SaveChangesAsync();

        var xpDelta = await _bookService.RecalculateBookAsync(book);
        if (xpDelta != 0)
        {
            await ApplyXpDeltaAsync(userId, xpDelta);
        }

        return NoContent();
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

    // DÜZELTİLDİ: granularity (Daily/Weekly/Monthly) parametresi eklendi.
    // Verilmezse kitabın kendi Period'u kullanılır (önceki davranış); verilirse
    // kitap haftalık hedefli olsa bile günlük eksende, ya da tam tersi
    // görüntülenebilir.
    [HttpGet("{id:int}/stats")]
    public async Task<ActionResult<IEnumerable<BookDailyStatDto>>> GetStats(int id, int days = 7, string? granularity = null)
    {
        if (days <= 0)
        {
            return BadRequest("days parametresi 1 veya daha büyük olmalıdır.");
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
    public async Task<ActionResult<IEnumerable<BookComparisonDto>>> GetComparison(int lookbackDays = 30)
    {
        if (lookbackDays <= 0)
        {
            return BadRequest("lookbackDays parametresi 1 veya daha büyük olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var books = await _context.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .ToListAsync();

        if (books.Count == 0)
        {
            return Ok(new List<BookComparisonDto>());
        }

        var user = await _userManager.FindByIdAsync(userId);
        var result = await _bookService.GetComparisonAsync(books, user?.TimeZoneId, lookbackDays);
        return Ok(result);
    }

    private async Task ApplyXpDeltaAsync(string userId, int xpDelta)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return;
        }

        user.TotalXp = Math.Max(0, user.TotalXp + xpDelta);
        await _userManager.UpdateAsync(user);
    }
}