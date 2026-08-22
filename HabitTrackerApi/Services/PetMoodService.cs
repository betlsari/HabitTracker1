using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;


public class PetMoodService
{
    private readonly AppDbContext _context;

    
    private const int SadAfterConsecutiveMissedDays = 2;

   
    private const int LookbackWindowDays = 14;

    public PetMoodService(AppDbContext context)
    {
        _context = context;
    }

    public async Task RecalculateMoodForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var pets = await _context.Pets
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (!pets.Any())
        {
            return;
        }

        var hatchedPets = pets.Where(p => p.Stage == Models.PetStage.Hatched).ToList();
        if (!hatchedPets.Any())
        {
            return;
        }

        var habits = await _context.Habits
            .Where(h => h.UserId == userId)
            .Select(h => new { h.Id, h.DailyGoal })
            .ToListAsync(cancellationToken);

        if (!habits.Any())
        {
           
            SetMood(hatchedPets, "Happy");
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-LookbackWindowDays);
        var habitIds = habits.Select(h => h.Id).ToList();
        var goalsByHabit = habits.ToDictionary(h => h.Id, h => h.DailyGoal);

        // TEK sorgu: son N gün için habit bazlı günlük toplamlar.
        var dailyTotals = await _context.HabitCompletions
            .Where(c => habitIds.Contains(c.HabitId) && c.CompletionDate.Date >= windowStart)
            .GroupBy(c => new { c.HabitId, Date = c.CompletionDate.Date })
            .Select(g => new { g.Key.HabitId, g.Key.Date, Total = g.Sum(c => c.Amount) })
            .ToListAsync(cancellationToken);

        int consecutiveMissedDays = 0;
        for (int i = 0; i < LookbackWindowDays; i++)
        {
            var date = today.AddDays(-i);
            bool anyGoalReachedThatDay = dailyTotals.Any(d =>
                d.Date == date && goalsByHabit.TryGetValue(d.HabitId, out var goal) && d.Total >= goal);

            if (anyGoalReachedThatDay)
            {
                break;
            }

            consecutiveMissedDays++;
        }

        var mood = consecutiveMissedDays >= SadAfterConsecutiveMissedDays ? "Sad" : "Happy";
        SetMood(hatchedPets, mood);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecalculateMoodForAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var userIds = await _context.Pets
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            await RecalculateMoodForUserAsync(userId, cancellationToken);
        }
    }

    private static void SetMood(List<Models.Pet> pets, string mood)
    {
        foreach (var pet in pets)
        {
            pet.Mood = mood;
        }
    }
}