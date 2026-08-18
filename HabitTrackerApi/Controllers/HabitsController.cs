using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
private readonly AppDbContext _context;
private readonly UserManager<User> _userManager;

public HabitsController(AppDbContext context, UserManager<User> userManager)
{
    _context = context;
    _userManager = userManager;

}


[HttpGet]
public async Task<ActionResult<IEnumerable<HabitDto>>> GetHabits()
{
    
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
     return await _context.Habits.Where(h => h.UserId == userId).Select(h => new HabitDto
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

[HttpPost]
public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var userId= User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var habitExist = await _context.Habits.AnyAsync(h => h.UserId == userId && h.Name == dto.Name);
        if(habitExist)
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
        if(user != null)
        {
            user.TotalXp +=5;
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

[HttpPut("{id}")]

public async Task<ActionResult<HabitDto>> UpdateHabit(int id, CreateHabitDto dto)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(habit == null || habit.UserId != userId)
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


[HttpDelete("{id}")]
public  async Task<ActionResult> DeleteHabit(int id)
    {
        var habit = await _context.Habits.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(habit == null || habit.UserId != userId)
        {
            return NotFound();
        }
        _context.Habits.Remove(habit);
        await _context.SaveChangesAsync();
        return NoContent();
    }


[HttpGet("{habitId}/progress")]
public async Task<ActionResult<HabitProgressDto>> GetProgress(int habitId)
    {
        var habit = await _context.Habits.FindAsync(habitId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }
        var totalToday = await _context.HabitCompletions
    .Where(c => c.HabitId == habitId && c.CompletionDate.Date == DateTime.UtcNow.Date)
    .SumAsync(c => c.Amount);
    double percentage = (double)totalToday / habit.DailyGoal * 100;
       bool isCompleted = totalToday >= habit.DailyGoal;

       int streak =0;
       var currentDate = DateTime.UtcNow.Date;
       while (currentDate >= habit.CreatedAt.Date)
        {
            var dailyTotal = await _context.HabitCompletions
            .Where(c => c.HabitId== habitId && c.CompletionDate.Date == currentDate)
            .SumAsync(c => c.Amount);

            if(dailyTotal <habit.DailyGoal)
            {
                break;
            }
            streak++;
            currentDate = currentDate.AddDays(-1);
        }
        var dto = new HabitProgressDto
        {
            HabitId = habitId,
            DailyGoal = habit.DailyGoal,
            TotalToday = totalToday,
            PercentageCompleted = percentage,
            IsCompleted = isCompleted,
            CurrentStreak = streak
        };

        return dto;
    }

    [HttpGet("{habitId}/stats")]
    public async Task<ActionResult<IEnumerable<DailyStatDto>>> GetStats(int habitId,int days = 7)
    {
        var habit = await _context.Habits.FindAsync(habitId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

        var stats = new List<DailyStatDto>();
        var currentDate = DateTime.UtcNow.Date;
        for(int i = 0; i < days; i++)
        {
            var dailyTotal = await _context.HabitCompletions
                .Where(c => c.HabitId == habitId && c.CompletionDate.Date == currentDate)
                .SumAsync(c => c.Amount);
            stats.Add(new DailyStatDto
            {
                Date = currentDate,
                TotalAmount = dailyTotal,
                GoalReached = dailyTotal >= habit.DailyGoal
            });
            currentDate = currentDate.AddDays(-1);
        }
        return stats;
    }






    }