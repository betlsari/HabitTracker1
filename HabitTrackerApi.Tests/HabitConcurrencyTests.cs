using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Xunit;

namespace HabitTrackerApi.Tests;

// YENİ (🟡 test kapsamı): Concurrency testi önceden sadece Book için vardı.
// AppDbContext.OnModelCreating içindeki IHasConcurrencyToken taraması Habit
// için de aynı davranışı üretmesi gerektiğinden, aynı desende bir test.
public class HabitConcurrencyTests
{
    private static DbContextOptions<AppDbContext> BuildOptions(string dbName) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task Concurrent_habit_edits_are_detected_via_ConcurrencyToken()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = BuildOptions(dbName);

        int habitId;
        await using (var seedContext = new AppDbContext(options))
        {
            var habit = new Habit
            {
                Name = "Concurrency Habit",
                NormalizedName = "CONCURRENCY HABIT",
                Category = HabitCategories.Water,
                UserId = "user-1",
                DailyGoal = 5,
                CreatedAt = DateTime.UtcNow
            };
            seedContext.Habits.Add(habit);
            await seedContext.SaveChangesAsync();
            habitId = habit.Id;
        }

        await using var contextA = new AppDbContext(options);
        var habitA = await contextA.Habits.FirstAsync(h => h.Id == habitId);

        await using (var contextB = new AppDbContext(options))
        {
            var habitB = await contextB.Habits.FirstAsync(h => h.Id == habitId);
            habitB.DailyGoal = 10;
            await contextB.SaveChangesAsync();
        }

        contextA.Entry(habitA).Property(h => h.ConcurrencyToken).IsModified = true;
        habitA.Notes = "İstek A tarafından değiştirildi";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync());
    }
}