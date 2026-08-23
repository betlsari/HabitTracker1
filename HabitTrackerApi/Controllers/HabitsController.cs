using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Services;
using Microsoft.Extensions.Options;
using Configuration;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
    private const int MaxHabitsPerUser = 100;
    private const int MaxStatsPeriods = 366;
    private const int MaxComparisonPeriods = 366;
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;
    private readonly HabitProgressService _progressService;
    private readonly FlowerService _flowerService;
    private readonly PetGrowthService _petGrowthService;
private readonly int _maxHabitsPerUser;

public HabitsController(
    AppDbContext context,
    UserManager<User> userManager,
    XpService xpService,
    HabitProgressService progressService,
    FlowerService flowerService,
    PetGrowthService petGrowthService,
    IOptions<AppLimitsOptions> limits)
{
    _context = context;
    _userManager = userManager;
    _xpService = xpService;
    _progressService = progressService;
    _flowerService = flowerService;
    _petGrowthService = petGrowthService;
    _maxHabitsPerUser = limits.Value.MaxHabitsPerUser;
}

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<HabitDto>>> GetHabits(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt);

        var totalCount = await query.CountAsync();
        var habits = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<HabitDto>
        {
            Items = habits.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    [HttpGet("categories")]
    public ActionResult<IEnumerable<string>> GetAllowedCategories()
    {
        
        return Ok(HabitCategories.Allowed);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HabitDto>> GetHabit(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        return ToDto(habit);
    }

    [HttpPost]
    public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (await _context.Habits.CountAsync(h => h.UserId == userId) >= _maxHabitsPerUser)
{
    return Conflict($"En fazla {_maxHabitsPerUser} alışkanlık oluşturabilirsiniz.");
}

        
        if (!HabitCategories.IsValid(dto.Category))
        {
            return BadRequest($"Geçersiz kategori. İzin verilen kategoriler: {string.Join(", ", HabitCategories.Allowed)}");
        }

        var normalizedName = dto.Name.Trim();
        var normalizedNameKey = normalizedName.ToUpperInvariant();
        var habitExist = await _context.Habits.AnyAsync(h =>
            h.UserId == userId && h.NormalizedName == normalizedNameKey);
        if (habitExist)
        {
            return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
        }

        var habit = new Habit
        {
            XpPerUnit = 1,
            XpBonusForGoal = 10,
            Name = normalizedName,
            NormalizedName = normalizedNameKey,
            Category = dto.Category,
            DailyGoal = dto.DailyGoal,
            Period = dto.Period,
            TargetTime = dto.TargetTime,
            ReminderTime = dto.ReminderTime,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Habits.Add(habit);
        await _context.SaveChangesAsync();
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.TotalXp += _xpService.GetHabitCreationXp();
            await _userManager.UpdateAsync(user);
        }

        return ToDto(habit);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HabitDto>> UpdateHabit(int id, CreateHabitDto dto)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        if (!HabitCategories.IsValid(dto.Category))
        {
            return BadRequest($"Geçersiz kategori. İzin verilen kategoriler: {string.Join(", ", HabitCategories.Allowed)}");
        }

        var normalizedName = dto.Name.Trim();
        var normalizedNameKey = normalizedName.ToUpperInvariant();
        var nameTakenByAnother = await _context.Habits.AnyAsync(h =>
             h.UserId == userId && h.Id != id && h.NormalizedName == normalizedNameKey);
        if (nameTakenByAnother)
        {
            return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
        }

        habit.Name = normalizedName;
        habit.NormalizedName = normalizedNameKey;
        habit.Category = dto.Category;
        habit.DailyGoal = dto.DailyGoal;
        habit.Period = dto.Period;
        habit.TargetTime = dto.TargetTime;
        habit.ReminderTime = dto.ReminderTime;

        await _context.SaveChangesAsync();
        return ToDto(habit);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteHabit(int id)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        var completions = await _context.HabitCompletions
            .Where(c => c.HabitId == id)
            .ToListAsync();

        var totalXpFromCompletions = completions.Sum(c => c.XpEarned);
        var totalPetStreakBonus = completions.Sum(c => c.PetStreakBonusXp);
        var totalAmount = completions.Sum(c => c.Amount);

        if (HabitCategories.IsWater(habit.Category) && totalAmount != 0)
        {
            await _flowerService.AddWaterAsync(userId!, -totalAmount);
        }

        if (HabitCategories.IsFocus(habit.Category) && totalAmount != 0)
        {
            await _petGrowthService.RemoveFocusXpAsync(userId!, totalAmount);
        }

        if (totalPetStreakBonus > 0)
        {
            await _petGrowthService.RemoveStreakBonusXpAsync(userId!, totalPetStreakBonus);
        }

        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();

        var totalXpToRemove = totalXpFromCompletions + _xpService.GetHabitCreationXp();
        if (totalXpToRemove != 0)
        {
            var user = await _userManager.FindByIdAsync(userId!);
            if (user != null)
            {
                user.TotalXp = Math.Max(0, user.TotalXp - totalXpToRemove);
                await _userManager.UpdateAsync(user);
            }
        }

        return NoContent();
    }

    [HttpGet("{habitId:int}/progress")]
    public async Task<ActionResult<HabitProgressDto>> GetProgress(int habitId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(userId!);
        return await _progressService.GetProgressAsync(habit, user?.TimeZoneId);
    }

    [HttpGet("{habitId:int}/stats")]
    public async Task<ActionResult<IEnumerable<DailyStatDto>>> GetStats(int habitId, int days = 7, string? granularity = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

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

        var user = await _userManager.FindByIdAsync(userId!);
        return await _progressService.GetStatsAsync(habit, user?.TimeZoneId, days, parsedGranularity);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<HabitProgressDto>>> GetSummary()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        var user = await _userManager.FindByIdAsync(userId!);
        var result = await _progressService.GetSummaryAsync(habits, user?.TimeZoneId);
        return Ok(result);
    }


    [HttpGet("comparison")]
    public async Task<ActionResult<IEnumerable<HabitComparisonDto>>> GetComparison(int lookbackPeriods = 30)
    {
        if (lookbackPeriods is <= 0 or > MaxComparisonPeriods)
        {
            return BadRequest($"lookbackPeriods parametresi 1 ile {MaxComparisonPeriods} arasında olmalıdır.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        if (habits.Count == 0)
        {
            return Ok(new List<HabitComparisonDto>());
        }

        var user = await _userManager.FindByIdAsync(userId!);
        var result = await _progressService.GetComparisonAsync(habits, user?.TimeZoneId, lookbackPeriods);
        return Ok(result);
    }

    private static HabitDto ToDto(Habit habit) => new()
    {
        Id = habit.Id,
        Name = habit.Name,
        Category = habit.Category,
        DailyGoal = habit.DailyGoal,
        CreatedAt = habit.CreatedAt,
        XpPerUnit = habit.XpPerUnit,
        XpBonusForGoal = habit.XpBonusForGoal,
        Period = habit.Period,
        TargetTime = habit.TargetTime,
        ReminderTime = habit.ReminderTime
    };
}
