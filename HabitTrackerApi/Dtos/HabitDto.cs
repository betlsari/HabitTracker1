using Models;

namespace Dtos;

public class HabitDto
{
    public int Id { get; set; }

    public required string Name { get; set; }
    public required string Category { get; set; }

    public int DailyGoal { get; set; }

    public DateTime CreatedAt { get; set; }

    public int XpPerUnit { get; set; }

    public int XpBonusForGoal { get; set; }

    public HabitPeriod Period { get; set; }

    public TimeOnly? TargetTime { get; set; }

    public TimeOnly? ReminderTime { get; set; }
}
