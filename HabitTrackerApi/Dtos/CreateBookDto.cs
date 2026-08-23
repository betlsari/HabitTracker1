
using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateBookDto : IValidatableObject
{
    public const int MaxGoalAmount = 100_000;
    public const int MaxTotalPages = 1_000_000;
    public const int MaxAuthorLength = 200;
    public const int MaxNotesLength = 1000;

    
    public const int MaxTitleLength = 300;

    [MinLength(1)]
    [MaxLength(MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(MaxAuthorLength)]
    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    [Range(1, MaxGoalAmount)]
    public int DailyGoalAmount { get; set; }

    [Range(1, MaxTotalPages)]
    public int? TotalPages { get; set; }

    [MaxLength(MaxNotesLength)]
    public string? Notes { get; set; }

    [MaxLength(2048)]
    [Url]
    public string? CoverImageUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GoalType == BookGoalType.Pages && !TotalPages.HasValue)
        {
            yield return new ValidationResult(
                "Sayfa bazlı hedef (GoalType = Pages) seçildiğinde TotalPages belirtilmelidir.",
                new[] { nameof(TotalPages) });
        }
    }
}