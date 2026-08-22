using Models;
using Services;
using Xunit;

namespace HabitTrackerApi.Tests;

public class XpServiceTests
{
    private static Habit CreateHabit(int dailyGoal, int xpPerUnit, int xpBonusForGoal, TimeOnly? targetTime = null) => new()
    {
        Id = 1,
        Name = "Test",
        Category = HabitCategories.Water,
        DailyGoal = dailyGoal,
        XpPerUnit = xpPerUnit,
        XpBonusForGoal = xpBonusForGoal,
        TargetTime = targetTime,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void CalculateCompletionXp_BelowGoal_OnlyPerUnitXp()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 10, xpPerUnit: 2, xpBonusForGoal: 5);

        var xp = service.CalculateCompletionXp(habit, amount: 3, totalBeforeThisCompletion: 0, streakKept: false);

        Assert.Equal(6, xp); // 3 * 2
    }

    [Fact]
    public void CalculateCompletionXp_GoalJustReached_AddsBonus()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 10, xpPerUnit: 1, xpBonusForGoal: 5);

        // 8'de iken 3 eklenince 11'e çıkıyor, hedef (10) bu completion'da aşılıyor.
        var xp = service.CalculateCompletionXp(habit, amount: 3, totalBeforeThisCompletion: 8, streakKept: false);

        Assert.Equal(3 + 5, xp); // 3 * xpPerUnit + bonus
    }

    [Fact]
    public void CalculateCompletionXp_GoalAlreadyReachedBefore_NoBonusAgain()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 10, xpPerUnit: 1, xpBonusForGoal: 5);

        // Zaten 10'a ulaşılmışken tekrar 2 eklenirse bonus tekrar verilmemeli.
        var xp = service.CalculateCompletionXp(habit, amount: 2, totalBeforeThisCompletion: 10, streakKept: false);

        Assert.Equal(2, xp);
    }

    [Fact]
    public void CalculateCompletionXp_StreakKept_AddsStreakBonus()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 5, xpPerUnit: 1, xpBonusForGoal: 5);

        var xp = service.CalculateCompletionXp(habit, amount: 5, totalBeforeThisCompletion: 0, streakKept: true);

        Assert.Equal(5 + 5 + service.GetStreakKeepBonus(), xp);
    }

    [Fact]
    public void CalculateCompletionXp_OnTimeWithTargetTime_AddsTargetTimeBonus()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 5, xpPerUnit: 1, xpBonusForGoal: 0, targetTime: new TimeOnly(9, 0));

        var xp = service.CalculateCompletionXp(habit, amount: 1, totalBeforeThisCompletion: 0, streakKept: false, isOnTime: true);

        Assert.Equal(1 + service.GetTargetTimeBonus(), xp);
    }

    [Fact]
    public void CalculateCompletionXp_NotOnTime_NoTargetTimeBonus()
    {
        var service = new XpService();
        var habit = CreateHabit(dailyGoal: 5, xpPerUnit: 1, xpBonusForGoal: 0, targetTime: new TimeOnly(9, 0));

        var xp = service.CalculateCompletionXp(habit, amount: 1, totalBeforeThisCompletion: 0, streakKept: false, isOnTime: false);

        Assert.Equal(1, xp);
    }
}