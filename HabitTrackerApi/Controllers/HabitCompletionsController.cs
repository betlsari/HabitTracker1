using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
using Dtos;
namespace Controllers;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;



[ApiController]
[Route("api/habits/{habitId}/[controller]")]
[Authorize]

public class HabitCompletionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;

    public HabitCompletionsController(AppDbContext context, UserManager<User> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

[HttpPost]
public async Task<ActionResult<HabitCompletionDto>> CompleteHabit(int habitId, CreateCompletionDto dto)

    {
        var habit = await _context.Habits.FindAsync(habitId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (habit == null || habit.UserId != userId)
        {
            return NotFound();
        }
        var totalBeforeThisCompletion = await _context.HabitCompletions
            .Where(c => c.HabitId == habitId && c.CompletionDate.Date == dto.CompletionDate.Date)
            .SumAsync(c => c.Amount);
        var newHabitCompletion = _context.HabitCompletions.Add(new HabitCompletion
{
    HabitId = habitId,
    CompletionDate = DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc),
    Amount = dto.Amount
});

         await _context.SaveChangesAsync();
         var totalAfterThisCompletion = totalBeforeThisCompletion + dto.Amount;
         int xpEarned = dto.Amount * habit.XpPerUnit;
         bool goalJustReacher = totalBeforeThisCompletion < habit.DailyGoal && totalAfterThisCompletion >= habit.DailyGoal;
         if(goalJustReacher)
         {
            xpEarned += habit.XpBonusForGoal;
         }
        
         var user = await _userManager.FindByIdAsync(userId);
         if(user != null)
         {
            user.TotalXp += xpEarned;
            await _userManager.UpdateAsync(user);
         }
          
var completionDto = new HabitCompletionDto
{
    Id = newHabitCompletion.Entity.Id,
    HabitId = newHabitCompletion.Entity.HabitId,
    CompletionDate = newHabitCompletion.Entity.CompletionDate,
    Amount = newHabitCompletion.Entity.Amount
};

        return completionDto;


}

[HttpGet]
public async  Task<ActionResult<IEnumerable<HabitCompletionDto>>>  GetHabitCompletions(int habitId)
    {
    var habit = await _context.Habits.FindAsync(habitId);
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    if(habit == null || habit.UserId != userId)
        {
            return NotFound();
        }

    return await _context.HabitCompletions
    .Where(c => c.HabitId == habitId)
    .Select(c => new HabitCompletionDto
    {
        Id = c.Id,
        HabitId = c.HabitId,
        CompletionDate = c.CompletionDate,
        Amount = c.Amount
    })
    .ToListAsync();
    }


[HttpPut("{id}")]
public async Task<ActionResult<HabitCompletionDto>> UpdateCompletion(int habitId,int id,CreateCompletionDto dto)
    {
        var completion = await _context.HabitCompletions.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (completion == null )
        {
            return NotFound();
        }
        var habit = await _context.Habits.FindAsync(completion.HabitId);
        if(habit == null || habit.UserId != userId)
        {
            return NotFound();
        }
        completion.Amount = dto.Amount;
        completion.CompletionDate=DateTime.SpecifyKind(dto.CompletionDate, DateTimeKind.Utc);

        await _context.SaveChangesAsync();

        var completionDto = new HabitCompletionDto()
        {
            Id = completion.Id,
    HabitId = completion.HabitId,
            Amount=completion.Amount,
            CompletionDate= completion.CompletionDate
        };
        return completionDto;


    }

[HttpDelete("{id}")]
public async  Task<ActionResult> DeleteCompletion(int id) 
    {
        var completion = await _context.HabitCompletions.FindAsync(id);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (completion == null)
        {
            return NotFound();
        }
        var habit = await _context.Habits.FindAsync(completion.HabitId);
        if(habit == null || habit.UserId != userId)
        {
            return NotFound();
        }
          _context.HabitCompletions.Remove(completion);
         await _context.SaveChangesAsync();
         return NoContent();
    }
}
