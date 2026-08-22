using Models;

namespace Services;

public class XpService
{
    private const int HabitCreationXp = 5;
    private const int StreakKeepBonus = 8;

    // YENİ: Habit.TargetTime alanı DB'de tutuluyor ve DTO'larda vardı ama
    // hiçbir davranışsal karşılığı yoktu. Artık kullanıcı belirlediği hedef
    // saatte veya öncesinde tamamlarsa küçük bir bonus XP kazanıyor
    // (dokümandaki "Hedef saat" alanının işlevsizliğini giderir).
    private const int TargetTimeBonus = 3;

    public int GetHabitCreationXp() => HabitCreationXp;

    public int GetStreakKeepBonus() => StreakKeepBonus;

    public int GetTargetTimeBonus() => TargetTimeBonus;

    public int CalculateCompletionXp(
        Habit habit,
        int amount,
        int totalBeforeThisCompletion,
        bool streakKept,
        bool isOnTime = false)
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

       
        if (habit.TargetTime.HasValue && isOnTime)
        {
            xpEarned += TargetTimeBonus;
        }

        return xpEarned;
    }
}