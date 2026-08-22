using Models;

namespace Services;


public class XpService
{
    private const int HabitCreationXp = 5;

    public int GetHabitCreationXp() => HabitCreationXp;

    
    public int CalculateCompletionXp(Habit habit, int amount, int totalBeforeThisCompletion)
    {
        int totalAfterThisCompletion = totalBeforeThisCompletion + amount;
        int xpEarned = amount * habit.XpPerUnit;

        bool goalJustReached = totalBeforeThisCompletion < habit.DailyGoal
            && totalAfterThisCompletion >= habit.DailyGoal;

        if (goalJustReached)
        {
            xpEarned += habit.XpBonusForGoal;
        }

        return xpEarned;
    }
}
