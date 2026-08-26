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

        var timeZoneId = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var tz = TimeZones.Resolve(timeZoneId);
        var todayLocal = TimeZones.ToLocal(DateTime.UtcNow, tz).Date;

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

        var windowStartUtc = TimeZones.ToUtc(todayLocal.AddDays(-LookbackWindowDays), tz);
        var habitIds = habits.Select(h => h.Id).ToList();
        var goalsByHabit = habits.ToDictionary(h => h.Id, h => h.DailyGoal);

        var completions = await _context.HabitCompletions
            .Where(c => habitIds.Contains(c.HabitId) && c.CompletionDate >= windowStartUtc)
            .Select(c => new { c.HabitId, c.CompletionDate, c.Amount })
            .ToListAsync(cancellationToken);

        // DÜZELTİLDİ: Gün sınırı artık kullanıcının local timezone'una göre
        // hesaplanıyor (önceden DateTime.UtcNow.Date kullanılıyordu, bu da
        // UTC+3 gibi dilimlerde gece yarısına yakın saatlerde yanlış güne
        // düşülmesine yol açıyordu).
        var dailyTotals = completions
            .GroupBy(c => new { c.HabitId, Date = TimeZones.ToLocal(c.CompletionDate, tz).Date })
            .Select(g => new { g.Key.HabitId, g.Key.Date, Total = g.Sum(c => c.Amount) })
            .ToList();

        int consecutiveMissedDays = 0;
        for (int i = 0; i < LookbackWindowDays; i++)
        {
            var date = todayLocal.AddDays(-i);
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
        var allPets = await _context.Pets.ToListAsync(cancellationToken);
        if (allPets.Count == 0)
        {
            return;
        }

        var petsByUser = allPets.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var usersWithHatchedPets = petsByUser
            .Where(kv => kv.Value.Any(p => p.Stage == Models.PetStage.Hatched))
            .Select(kv => kv.Key)
            .ToList();

        if (usersWithHatchedPets.Count == 0)
        {
            return;
        }

        var timeZonesByUser = await _context.Users.AsNoTracking()
            .Where(u => usersWithHatchedPets.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.TimeZoneId, cancellationToken);

        var allHabits = await _context.Habits
            .Where(h => usersWithHatchedPets.Contains(h.UserId))
            .Select(h => new { h.Id, h.UserId, h.DailyGoal })
            .ToListAsync(cancellationToken);

        var habitsByUser = allHabits.GroupBy(h => h.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var allHabitIds = allHabits.Select(h => h.Id).ToList();

        var utcNow = DateTime.UtcNow;
        // Kullanıcılar farklı timezone'larda olabildiğinden pencereyi 1 gün
        // pay bırakarak geniş çekiyoruz; asıl gün eşleştirmesi aşağıda her
        // kullanıcının kendi local tarihine göre yapılıyor.
        var broadWindowStartUtc = utcNow.AddDays(-(LookbackWindowDays + 1));

        var completionsByHabit = allHabitIds.Count == 0
            ? new Dictionary<int, List<(DateTime CompletionUtc, int Amount)>>()
            : (await _context.HabitCompletions
                .Where(c => allHabitIds.Contains(c.HabitId) && c.CompletionDate >= broadWindowStartUtc)
                .Select(c => new { c.HabitId, c.CompletionDate, c.Amount })
                .ToListAsync(cancellationToken))
                .GroupBy(c => c.HabitId)
                .ToDictionary(g => g.Key, g => g.Select(c => (CompletionUtc: c.CompletionDate, Amount: c.Amount)).ToList());

        foreach (var userId in usersWithHatchedPets)
        {
            var hatchedPets = petsByUser[userId].Where(p => p.Stage == Models.PetStage.Hatched).ToList();

            if (!habitsByUser.TryGetValue(userId, out var userHabits) || userHabits.Count == 0)
            {
                SetMood(hatchedPets, "Happy");
                continue;
            }

            timeZonesByUser.TryGetValue(userId, out var timeZoneId);
            var tz = TimeZones.Resolve(timeZoneId);
            var todayLocal = TimeZones.ToLocal(utcNow, tz).Date;
            var goalsByHabit = userHabits.ToDictionary(h => h.Id, h => h.DailyGoal);

            var userDailyTotals = userHabits
                .Where(h => completionsByHabit.ContainsKey(h.Id))
                .SelectMany(h => completionsByHabit[h.Id].Select(c => new
                {
                    HabitId = h.Id,
                    Date = TimeZones.ToLocal(c.CompletionUtc, tz).Date,
                    c.Amount
                }))
                .GroupBy(x => new { x.HabitId, x.Date })
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            int consecutiveMissedDays = 0;
            for (int i = 0; i < LookbackWindowDays; i++)
            {
                var date = todayLocal.AddDays(-i);
                bool anyGoalReachedThatDay = userHabits.Any(h =>
                    userDailyTotals.TryGetValue(new { HabitId = h.Id, Date = date }, out var total) &&
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