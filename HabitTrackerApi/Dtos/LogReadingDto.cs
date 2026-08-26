using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class LogReadingDto : IValidatableObject
{
    public const int MaxAmount = 10_000;
    public const int MaxPageReached = 1_000_000;

    public const int MaxPastDays = 3650;

    public DateTime ReadDate { get; set; }

    [Range(0, MaxAmount)]
    public int Amount { get; set; }

    [Range(0, MaxPageReached)]
    public int? PageReachedAt { get; set; }

   [MaxLength(100)]
public string? ClientRequestId { get; set; }
    

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReadDate > DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "Okuma tarihi gelecekte olamaz.",
                new[] { nameof(ReadDate) });
        }

        if (ReadDate < DateTime.UtcNow.AddDays(-MaxPastDays))
        {
            yield return new ValidationResult(
                $"Okuma tarihi en fazla {MaxPastDays} gün öncesine ait olabilir.",
                new[] { nameof(ReadDate) });
        }
    }
}