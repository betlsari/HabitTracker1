namespace Services;


public static class UserLevelService
{
    private const int XpPerLevelUnit = 50;

    public static int GetLevel(int totalXp)
    {
        if (totalXp <= 0) return 1;

        int level = 1;
        while (XpRequiredForLevel(level + 1) <= totalXp)
        {
            level++;
        }
        return level;
    }

    public static int XpRequiredForLevel(int level)
    {
        if (level <= 1) return 0;
        var n = level - 1;
        return XpPerLevelUnit * n * (n + 1) / 2;
    }

    public static (int Level, int CurrentLevelXp, int XpForNextLevel, double ProgressPercent) GetLevelProgress(int totalXp)
    {
        var level = GetLevel(totalXp);
        var currentThreshold = XpRequiredForLevel(level);
        var nextThreshold = XpRequiredForLevel(level + 1);
        var currentLevelXp = totalXp - currentThreshold;
        var xpForNextLevel = nextThreshold - currentThreshold;
        var progress = xpForNextLevel == 0 ? 100 : Math.Min(100, (double)currentLevelXp / xpForNextLevel * 100);

        return (level, currentLevelXp, xpForNextLevel, Math.Round(progress, 1));
    }
}