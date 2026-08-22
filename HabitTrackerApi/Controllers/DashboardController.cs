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

// YENİ: Ana ekran için Habits + Books + Pets + Flower + TotalXp + okunmamış
// bildirim sayısını TEK istekte döner. Önceden istemci bu veriyi toplamak için
// /api/habits/summary, /api/books, /api/pets, /api/flowers, /api/auth/me,
// /api/notifications gibi 5-6 ayrı istek atmak zorundaydı.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly HabitProgressService _habitProgressService;
    private readonly FlowerService _flowerService;

    public DashboardController(
        AppDbContext context,
        UserManager<User> userManager,
        HabitProgressService habitProgressService,
        FlowerService flowerService)
    {
        _context = context;
        _userManager = userManager;
        _habitProgressService = habitProgressService;
        _flowerService = flowerService;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        var books = await _context.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .ToListAsync();

        var pets = await _context.Pets.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var unreadCount = await _context.UserNotifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead);

        var flower = await _context.Flowers.AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId);

        var habitProgress = await _habitProgressService.GetSummaryAsync(habits, user.TimeZoneId);

        var dashboard = new DashboardDto
        {
            TotalXp = user.TotalXp,
            Habits = habitProgress,
            Books = books.Select(BookService.ToDto).ToList(),
            Pets = pets.Select(ToPetDto).ToList(),
            Flower = flower != null ? FlowerService.ToDto(flower) : null,
            UnreadNotificationCount = unreadCount
        };

        return Ok(dashboard);
    }

    private static PetDto ToPetDto(Pet pet) => new()
    {
        Id = pet.Id,
        Type = pet.Type,
        Level = pet.Level,
        Xp = pet.Xp,
        Mood = pet.Mood,
        CreatedAt = pet.CreatedAt,
        Nickname = pet.Nickname,
        Stage = pet.Stage.ToString(),
        HatchedAt = pet.HatchedAt,
        IsEgg = pet.Stage == PetStage.Egg
    };
}