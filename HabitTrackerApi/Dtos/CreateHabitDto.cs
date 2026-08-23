using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateHabitDto : IValidatableObject
{
    public const int MaxDailyGoal = 100_000;
    public const int MaxNotesLength = 1000;
    public const int MaxNameLength = 200;
    public const int MaxCustomCategoryNameLength = 100;
    public const int MaxCategoryLength = 50;

    [MinLength(1)]
    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Range(1, MaxDailyGoal)]
    public int DailyGoal { get; set; }

    
    [MinLength(1)]
    [MaxLength(MaxCategoryLength)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(MaxCustomCategoryNameLength)]
    public string? CustomCategoryName { get; set; }

    public HabitUnit Unit { get; set; } = HabitUnit.Count;

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;
    public TimeOnly? TargetTime { get; set; }
    public TimeOnly? ReminderTime { get; set; }

    [MaxLength(MaxNotesLength)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(Category?.Trim(), HabitCategories.Other, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(CustomCategoryName))
        {
            yield return new ValidationResult(
                "\"Diğer\" kategorisi seçildiğinde CustomCategoryName (özel etiket) belirtilmelidir.",
                new[] { nameof(CustomCategoryName) });
        }
    }
}