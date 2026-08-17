using System.ComponentModel.DataAnnotations;

namespace Models;

public class Habit
{
    public int Id { get; set; }
    [MinLength(1)]
    public string Name { get; set; } =string.Empty;
    [Range(1, int.MaxValue)]
    public int DailyGoal { get; set; }
    [MinLength(1)]
    public required string Category { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<HabitCompletion> Completions { get; set; } = new List<HabitCompletion>();

    public string UserId { get; set; } = string.Empty;

    public User? User { get; set; }
    public int XpPerUnit { get; set; }

    public int XpBonusForGoal { get; set; }


}