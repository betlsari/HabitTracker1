using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class HabitProgressServiceStreakTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static Habit CreateHabit(DateTime createdAtUtc, int dailyGoal = 1) => new()
    {
        Id = 1,
        Name = "Test",
        Category = HabitCategories.Water,
        DailyGoal = dailyGoal,
        CreatedAt = createdAtUtc,
        UserId = "user-1"
    };

    [Fact]
    public void CountStreak_NoCompletions_ReturnsZero()
    {
        var habit = CreateHabit(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var totals = new Dictionary<DateTime, int>();
        var currentPeriod = new DateTime(2026, 8, 22);

        var streak = HabitProgressService.CountStreak(habit, totals, currentPeriod, Istanbul);

        Assert.Equal(0, streak);
    }

    [Fact]
    public void CountStreak_ConsecutiveDaysGoalMet_CountsCorrectly()
    {
        var habit = CreateHabit(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), dailyGoal: 1);
        var totals = new Dictionary<DateTime, int>
        {
            [new DateTime(2026, 8, 20)] = 1,
            [new DateTime(2026, 8, 21)] = 1,
            [new DateTime(2026, 8, 22)] = 1
        };

        var streak = HabitProgressService.CountStreak(habit, totals, new DateTime(2026, 8, 22), Istanbul);

        Assert.Equal(3, streak);
    }

    [Fact]
    public void CountStreak_BreaksOnMissedDay()
    {
        var habit = CreateHabit(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), dailyGoal: 1);
        var totals = new Dictionary<DateTime, int>
        {
            [new DateTime(2026, 8, 22)] = 1,
            [new DateTime(2026, 8, 21)] = 0, // hedef tutturulamadı -> zincir burada kesilir
            [new DateTime(2026, 8, 20)] = 1
        };

        var streak = HabitProgressService.CountStreak(habit, totals, new DateTime(2026, 8, 22), Istanbul);

        Assert.Equal(1, streak);
    }

    [Fact]
    public void CountStreak_StopsAtHabitCreationDate()
    {
        // Habit 20 Ağustos'ta oluşturuldu; öncesine ait (varsayımsal) günler sayılmamalı.
        var habit = CreateHabit(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), dailyGoal: 1);
        var totals = new Dictionary<DateTime, int>
        {
            [new DateTime(2026, 8, 22)] = 1,
            [new DateTime(2026, 8, 21)] = 1,
            [new DateTime(2026, 8, 20)] = 1,
            [new DateTime(2026, 8, 19)] = 1 // habit henüz yokken, teorik olarak sayılmamalı
        };

        var streak = HabitProgressService.CountStreak(habit, totals, new DateTime(2026, 8, 22), Istanbul);

        Assert.Equal(3, streak);
    }
}