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

    public BooksController(AppDbContext context, UserManager<User> userManager, BookService bookService)
    {
        _context = context;
        _userManager = userManager;
        _bookService = bookService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BookDto>>> GetBooks()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var books = await _context.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return books.Select(BookService.ToDto).ToList();
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
        book.TotalPages = dto.TotalPages;
        book.DailyGoalAmount = dto.DailyGoalAmount;

        await _context.SaveChangesAsync();
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

        _context.Books.Remove(book);
        await _context.SaveChangesAsync();
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

        var (log, xpEarned) = await _bookService.AddReadingLogAsync(book, dto);

        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.TotalXp += xpEarned;
            await _userManager.UpdateAsync(user);
        }

        return BookService.ToLogDto(log);
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
                PageReachedAt = l.PageReachedAt
            })
            .ToListAsync();
    }
}