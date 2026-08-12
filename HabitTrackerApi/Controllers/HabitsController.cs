using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;

namespace Controllers;
[ApiController]
[Route("api/[controller]")]
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
     return await _context.Habits.Select(h => new HabitDto
     {
         Id = h.Id,
         Name = h.Name,
         Category = h.Category,
         DailyGoal = h.DailyGoal,
         CreatedAt = h.CreatedAt
     }).ToListAsync();
    
}

[HttpPost]
public async Task<ActionResult<HabitDto>> CreateHabit(Habit habit)
    {
        
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

public async Task<ActionResult<HabitDto>> UpdateHabit(int id, Habit updatedHabit)
    {
        var habit = await _context.Habits.FindAsync(id);
        if (habit == null)
        {
            return NotFound();
        }
        updatedHabit.Id = id;
        _context.Entry(habit).CurrentValues.SetValues(updatedHabit);
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
        if(habit == null)
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
        if (habit == null)
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