using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateCompletionDto : IValidatableObject
{
    public const int MaxAmount = 10_000;
    public const int MaxPastDays = 3650; // 10 yıl
    public const int MaxClientRequestIdLength = 100;

    public DateTime CompletionDate { get; set; }

    [Range(0, MaxAmount)]
    public int Amount { get; set; }

    
    [MaxLength(MaxClientRequestIdLength)]
    public string? ClientRequestId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CompletionDate > DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "Completion date cannot be in the future.",
                new[] { nameof(CompletionDate) });
        }

        if (CompletionDate < DateTime.UtcNow.AddDays(-MaxPastDays))
        {
            yield return new ValidationResult(
                $"Completion date cannot be more than {MaxPastDays} days in the past.",
                new[] { nameof(CompletionDate) });
        }
    }
}