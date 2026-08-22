using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateBookDto : IValidatableObject
{
    [MinLength(1)]
    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    [Range(1, int.MaxValue)]
    public int DailyGoalAmount { get; set; }

    [Range(1, int.MaxValue)]
    public int? TotalPages { get; set; }

   
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