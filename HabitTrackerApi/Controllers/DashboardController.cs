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
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly HabitProgressService _habitProgressService;
    private readonly FlowerService _flowerService;
    private readonly DashboardCacheService _dashboardCacheService;

    public DashboardController(
        AppDbContext context,
        UserManager<User> userManager,
        HabitProgressService habitProgressService,
        FlowerService flowerService,
        DashboardCacheService dashboardCacheService)
    {
        _context = context;
        _userManager = userManager;
        _habitProgressService = habitProgressService;
        _flowerService = flowerService;
        _dashboardCacheService = dashboardCacheService;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var cached = await _dashboardCacheService.GetAsync(userId);
        if (cached is not null)
        {
            return Ok(cached);
        }

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

        await _dashboardCacheService.SetAsync(userId, dashboard);

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