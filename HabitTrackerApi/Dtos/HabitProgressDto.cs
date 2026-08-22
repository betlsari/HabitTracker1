using Models;

namespace Dtos;

public class HabitProgressDto
{
    public int HabitId { get; set; }

    public int DailyGoal { get; set; }

    public int TotalToday { get; set; }

    public int TotalInPeriod { get; set; }

    public bool IsCompleted { get; set; }

    public double PercentageCompleted { get; set; }

    public int CurrentStreak { get; set; }

    public HabitPeriod Period { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }
}
