namespace Models;

public class Habit
{
    public int Id { get; set; }
    public string Name { get; set; } =string.Empty;

    public int DailyGoal { get; set; }

    public required string Category { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<HabitCompletion> Completions { get; set; } = new List<HabitCompletion>();// Navigation property to the HabitCompletion entity


}