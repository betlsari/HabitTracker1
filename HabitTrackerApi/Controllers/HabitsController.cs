using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;

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
public async Task<ActionResult<IEnumerable<Habit>>> GetHabits()
{
     return await _context.Habits.ToListAsync();
    
}

[HttpPost]
public async Task<ActionResult<Habit>> CreateHabit(Habit habit)
    {
        
        habit.CreatedAt = DateTime.UtcNow;
        _context.Habits.Add(habit);
        await _context.SaveChangesAsync();

        return habit;
    }

[HttpPut("{id}")]

public async Task<ActionResult<Habit>> UpdateHabit(int id, Habit updatedHabit)
    {
        var habit = await _context.Habits.FindAsync(id);
        if (habit == null)
        {
            return NotFound();
        }
        updatedHabit.Id = id;
        _context.Entry(habit).CurrentValues.SetValues(updatedHabit);
        await _context.SaveChangesAsync();
        return habit;
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
    }