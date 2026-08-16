using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateHabitDto
{
    [MinLength(1)]
    public string Name { get; set; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int DailyGoal { get; set; }
    [MinLength(1)]
    public string Category { get; set; } = string.Empty;
}