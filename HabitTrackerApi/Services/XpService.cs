using Models;

namespace Services;

public class XpService
{
    private const int HabitCreationXp = 5;
    private const int StreakKeepBonus = 8;

    public int GetHabitCreationXp() => HabitCreationXp;

    public int GetStreakKeepBonus() => StreakKeepBonus;

    public int CalculateCompletionXp(Habit habit, int amount, int totalBeforeThisCompletion, bool streakKept)
    {
        int totalAfterThisCompletion = totalBeforeThisCompletion + amount;
        int xpEarned = amount * habit.XpPerUnit;

        bool goalJustReached = totalBeforeThisCompletion < habit.DailyGoal
            && totalAfterThisCompletion >= habit.DailyGoal;

        if (goalJustReached)
        {
            xpEarned += habit.XpBonusForGoal;
            if (streakKept)
            {
                xpEarned += StreakKeepBonus;
            }
        }

        return xpEarned;
    }
}
