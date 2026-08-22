using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Services;

namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly XpService _xpService;

    public HabitsController(AppDbContext context, UserManager<User> userManager, XpService xpService)
    {
        _context = context;
        _userManager = userManager;
        _xpService = xpService;
    }


    [HttpGet]
    public async Task<ActionResult<IEnumerable<HabitDto>>> GetHabits()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await _context.Habits.AsNoTracking().Where(h => h.UserId == userId).Select(h => new HabitDto
        {
            Id = h.Id,
            Name = h.Name,
            Category = h.Category,
            DailyGoal = h.DailyGoal,
            CreatedAt = h.CreatedAt,
            XpPerUnit = h.XpPerUnit,
            XpBonusForGoal = h.XpBonusForGoal
        }).ToListAsync();
    }

    // YENİ: tekil habit getirme (önceden sadece liste vardı).
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

        return new HabitDto
        {
            Id = habit.Id,
            Name = habit.Name,
            Category = habit.Category,
            DailyGoal = habit.DailyGoal,
            CreatedAt = habit.CreatedAt,
            XpPerUnit = habit.XpPerUnit,
            XpBonusForGoal = habit.XpBonusForGoal
        };
    }

    [HttpPost]
    public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var habitExist = await _context.Habits.AnyAsync(h => h.UserId == userId && h.Name == dto.Name);
        if (habitExist)
        {
            return BadRequest("Bu isimde zaten bir alışkanlığınız var.");
        }

        var habit = new Habit
        {
            XpPerUnit = 1,
            XpBonusForGoal = 10,
            Name = dto.Name,
            Category = dto.Category,
            DailyGoal = dto.DailyGoal,
        };
        habit.UserId = userId;
        habit.CreatedAt = DateTime.UtcNow;

        _context.Habits.Add(habit);
        await _context.SaveChangesAsync();
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.TotalXp += _xpService.GetHabitCreationXp();
            await _userManager.UpdateAsync(user);
        }

        var habitDto = new HabitDto
        {
            Id = habit.Id,
            Name = habit.Name,
            Category = habit.Category,
            DailyGoal = habit.DailyGoal,
            CreatedAt = habit.CreatedAt,
            XpPerUnit = habit.XpPerUnit,
            XpBonusForGoal = habit.XpBonusForGoal
        };
        return habitDto;
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

        habit.Name = dto.Name;
        habit.Category = dto.Category;
        habit.DailyGoal = dto.DailyGoal;

        await _context.SaveChangesAsync();

        var habitDto = new HabitDto
        {
            Id = habit.Id,
            Name = habit.Name,
            Category = habit.Category,
            DailyGoal = habit.DailyGoal,
            CreatedAt = habit.CreatedAt,
            XpPerUnit = habit.XpPerUnit,
            XpBonusForGoal = habit.XpBonusForGoal
        };
        return habitDto;
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
        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DÜZELTİLDİ: eskiden habit'in oluşturulduğu günden bugüne kadar HER GÜN
    // için ayrı bir SumAsync sorgusu atılıyordu (N+1). Artık tek bir GroupBy
    // sorgusuyla tüm günlük toplamlar çekiliyor, streak hesaplaması bellekte yapılıyor.
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

        var today = DateTime.UtcNow.Date;

        var dailyTotals = await _context.HabitCompletions
            .Where(c => c.HabitId == habitId && c.CompletionDate.Date >= habit.CreatedAt.Date)
            .GroupBy(c => c.CompletionDate.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(g => g.Date, g => g.Total);

        int totalToday = dailyTotals.TryGetValue(today, out var todayTotal) ? todayTotal : 0;
        double percentage = habit.DailyGoal == 0 ? 0 : (double)totalToday / habit.DailyGoal * 100;
        bool isCompleted = totalToday >= habit.DailyGoal;

        int streak = 0;
        var currentDate = today;
        while (currentDate >= habit.CreatedAt.Date)
        {
            var dailyTotal = dailyTotals.TryGetValue(currentDate, out var dt) ? dt : 0;
            if (dailyTotal < habit.DailyGoal)
            {
                break;
            }
            streak++;
            currentDate = currentDate.AddDays(-1);
        }

        var progressDto = new HabitProgressDto
        {
            HabitId = habitId,
            DailyGoal = habit.DailyGoal,
            TotalToday = totalToday,
            PercentageCompleted = percentage,
            IsCompleted = isCompleted,
            CurrentStreak = streak
        };

        return progressDto;
    }

    // DÜZELTİLDİ: aynı N+1 sorunu burada da vardı, aynı yöntemle çözüldü.
    [HttpGet("{habitId:int}/stats")]
    public async Task<ActionResult<IEnumerable<DailyStatDto>>> GetStats(int habitId, int days = 7)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habit = await _context.Habits.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId);

        if (habit == null)
        {
            return NotFound();
        }

        if (days <= 0)
        {
            return BadRequest("days parametresi 1 veya daha büyük olmalıdır.");
        }

        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-(days - 1));

        var dailyTotals = await _context.HabitCompletions
            .Where(c => c.HabitId == habitId && c.CompletionDate.Date >= windowStart && c.CompletionDate.Date <= today)
            .GroupBy(c => c.CompletionDate.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(g => g.Date, g => g.Total);

        var stats = new List<DailyStatDto>();
        for (int i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var dailyTotal = dailyTotals.TryGetValue(date, out var dt) ? dt : 0;
            stats.Add(new DailyStatDto
            {
                Date = date,
                TotalAmount = dailyTotal,
                GoalReached = dailyTotal >= habit.DailyGoal
            });
        }

        return stats;
    }

    // YENİ: dokümanda geçen "hangi alışkanlık daha iyi sürdürülüyor" analizi için
    // tüm habit'lerin bugünkü özetini tek çağrıda döner. Not: performans için
    // CurrentStreak burada hesaplanmıyor (0 döner) — streak detayı gerekiyorsa
    // /api/habits/{id}/progress kullanılmalı.
    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<HabitProgressDto>>> GetSummary()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == userId)
            .ToListAsync();

        if (!habits.Any())
        {
            return Ok(Enumerable.Empty<HabitProgressDto>());
        }

        var today = DateTime.UtcNow.Date;
        var habitIds = habits.Select(h => h.Id).ToList();

        var totalsToday = await _context.HabitCompletions
            .Where(c => habitIds.Contains(c.HabitId) && c.CompletionDate.Date == today)
            .GroupBy(c => c.HabitId)
            .Select(g => new { HabitId = g.Key, Total = g.Sum(c => c.Amount) })
            .ToDictionaryAsync(g => g.HabitId, g => g.Total);

        var result = habits.Select(h =>
        {
            var totalToday = totalsToday.TryGetValue(h.Id, out var t) ? t : 0;
            return new HabitProgressDto
            {
                HabitId = h.Id,
                DailyGoal = h.DailyGoal,
                TotalToday = totalToday,
                PercentageCompleted = h.DailyGoal == 0 ? 0 : (double)totalToday / h.DailyGoal * 100,
                IsCompleted = totalToday >= h.DailyGoal,
                CurrentStreak = 0
            };
        });

        return Ok(result);
    }
}
