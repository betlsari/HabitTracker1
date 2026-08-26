using System.ComponentModel.DataAnnotations;
using Dtos;

namespace Models;

public class Habit 
{
    public int Id { get; set; }
    [MinLength(1)]
    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    

    [Range(1, CreateHabitDto.MaxDailyGoal)]
    public int DailyGoal { get; set; }
    [MinLength(1)]
    [MaxLength(CreateHabitDto.MaxCategoryLength)]
    public required string Category { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<HabitCompletion> Completions { get; set; } = new List<HabitCompletion>();

    public string UserId { get; set; } = string.Empty;

    public User? User { get; set; }
    public int XpPerUnit { get; set; }

    public int XpBonusForGoal { get; set; }

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    public TimeOnly? TargetTime { get; set; }

    public TimeOnly? ReminderTime { get; set; }

    
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    
    [MaxLength(1000)]
    public string? Notes { get; set; }

  

    [MaxLength(100)]
public string? CustomCategoryName { get; set; }
public HabitUnit Unit { get; set; } = HabitUnit.Count;
}