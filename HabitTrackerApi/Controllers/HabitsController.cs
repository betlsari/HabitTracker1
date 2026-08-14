using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HabitsController : ControllerBase
{
private readonly AppDbContext _context;

public HabitsController(AppDbContext context)
{
    _context = context;

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
         CreatedAt = h.CreatedAt
     }).ToListAsync();
    
}

[HttpPost]
public async Task<ActionResult<HabitDto>> CreateHabit(CreateHabitDto dto)
    {
        var userId= User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        
        var habit = new Habit
        {
            Name = dto.Name,
            Category = dto.Category,
            DailyGoal = dto.DailyGoal,
        };
        habit.UserId = userId;
        habit.CreatedAt = DateTime.UtcNow;
        _context.Habits.Add(habit);
        await _context.SaveChangesAsync();

        var habitDto = new HabitDto
        {
            Id = habit.Id,
            Name = habit.Name,
            Category = habit.Category,
            DailyGoal = habit.DailyGoal,
            CreatedAt = habit.CreatedAt
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
            CreatedAt = habit.CreatedAt
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
        var dto = new HabitProgressDto
        {
            HabitId = habitId,
            DailyGoal = habit.DailyGoal,
            TotalToday = totalToday,
            PercentageCompleted = percentage,
            IsCompleted = isCompleted
        };

        return dto;
    }





    }