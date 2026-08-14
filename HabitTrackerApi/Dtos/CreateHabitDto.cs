namespace Dtos;

public class CreateHabitDto
{
    public string Name { get; set; } = string.Empty;
    public int DailyGoal { get; set; }
    public string Category { get; set; } = string.Empty;
}