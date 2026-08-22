using System.ComponentModel.DataAnnotations;
using Models;

namespace Dtos;

public class CreateBookDto : IValidatableObject
{
    public const int MaxGoalAmount = 100_000;
    public const int MaxTotalPages = 1_000_000;
    [MinLength(1)]
    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public BookGoalType GoalType { get; set; } = BookGoalType.Pages;

    // YENİ: Kitabın günlük/haftalık/aylık okuma hedefi seçilebiliyor.
    // Verilmezse Daily (önceki davranışla tam uyumlu) kullanılır.
    public HabitPeriod Period { get; set; } = HabitPeriod.Daily;

    [Range(1, MaxGoalAmount)]
    public int DailyGoalAmount { get; set; }

    [Range(1, MaxTotalPages)]
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
