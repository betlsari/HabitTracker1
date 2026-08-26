using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateCompletionDto : IValidatableObject
{
    public const int MaxAmount = 10_000;
    public const int MaxPastDays = 3650; // 10 yıl
   

    public DateTime CompletionDate { get; set; }

    [Range(0, MaxAmount)]
    public int Amount { get; set; }



  
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CompletionDate > DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "Tamamlama tarihi gelecekte olamaz.",
                new[] { nameof(CompletionDate) });
        }

        if (CompletionDate < DateTime.UtcNow.AddDays(-MaxPastDays))
        {
            yield return new ValidationResult(
                $"Tamamlama tarihi en fazla {MaxPastDays} gün öncesine ait olabilir.",
                new[] { nameof(CompletionDate) });
        }
    }
}