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

    // DÜZELTİLDİ (🟡 N+1 / performans): Önceden bu metod her kullanıcı için
    // AYRI AYRI RecalculateMoodForUserAsync çağırıyordu — her çağrı Pets,
    // Habits ve HabitCompletions için 3 ayrı sorgu atıyordu. Kullanıcı sayısı
    // arttıkça (N kullanıcı = ~3N sorgu) bu job (6 saatte bir çalışsa da)
    // giderek pahalılaşıyordu. Artık TÜM kullanıcılar için sabit sayıda
    // (4) sorgu atılıyor: tüm pet'ler, tüm ilgili habit'ler, tüm ilgili
    // completion'lar tek seferde çekilip bellekte kullanıcı bazında
    // gruplanıyor; tek bir SaveChangesAsync ile kaydediliyor.
    public async Task RecalculateMoodForAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var allPets = await _context.Pets.ToListAsync(cancellationToken);
        if (allPets.Count == 0)
        {
            return;
        }

        var petsByUser = allPets
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var usersWithHatchedPets = petsByUser
            .Where(kv => kv.Value.Any(p => p.Stage == Models.PetStage.Hatched))
            .Select(kv => kv.Key)
            .ToList();

        if (usersWithHatchedPets.Count == 0)
        {
            return;
        }

        var allHabits = await _context.Habits
            .Where(h => usersWithHatchedPets.Contains(h.UserId))
            .Select(h => new { h.Id, h.UserId, h.DailyGoal })
            .ToListAsync(cancellationToken);

        var habitsByUser = allHabits
            .GroupBy(h => h.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var today = DateTime.UtcNow.Date;
        var windowStart = today.AddDays(-LookbackWindowDays);
        var allHabitIds = allHabits.Select(h => h.Id).ToList();

        var dailyTotalsFlat = allHabitIds.Count == 0
            ? new List<(int HabitId, DateTime Date, int Total)>()
            : (await _context.HabitCompletions
                .Where(c => allHabitIds.Contains(c.HabitId) && c.CompletionDate.Date >= windowStart)
                .GroupBy(c => new { c.HabitId, Date = c.CompletionDate.Date })
                .Select(g => new { g.Key.HabitId, g.Key.Date, Total = g.Sum(c => c.Amount) })
                .ToListAsync(cancellationToken))
                .Select(x => (x.HabitId, x.Date, x.Total))
                .ToList();

        var totalsByHabit = dailyTotalsFlat
            .GroupBy(t => t.HabitId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(t => t.Date, t => t.Total));

        foreach (var userId in usersWithHatchedPets)
        {
            var hatchedPets = petsByUser[userId].Where(p => p.Stage == Models.PetStage.Hatched).ToList();

            if (!habitsByUser.TryGetValue(userId, out var userHabits) || userHabits.Count == 0)
            {
                SetMood(hatchedPets, "Happy");
                continue;
            }

            var goalsByHabit = userHabits.ToDictionary(h => h.Id, h => h.DailyGoal);

            int consecutiveMissedDays = 0;
            for (int i = 0; i < LookbackWindowDays; i++)
            {
                var date = today.AddDays(-i);
                bool anyGoalReachedThatDay = userHabits.Any(h =>
                    totalsByHabit.TryGetValue(h.Id, out var habitTotals) &&
                    habitTotals.TryGetValue(date, out var total) &&
                    total >= goalsByHabit[h.Id]);

                if (anyGoalReachedThatDay)
                {
                    break;
                }

                consecutiveMissedDays++;
            }

            var mood = consecutiveMissedDays >= SadAfterConsecutiveMissedDays ? "Sad" : "Happy";
            SetMood(hatchedPets, mood);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void SetMood(List<Models.Pet> pets, string mood)
    {
        foreach (var pet in pets)
        {
            pet.Mood = mood;
        }
    }
}