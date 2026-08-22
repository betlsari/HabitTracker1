using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class LogReadingDto : IValidatableObject
{
    public DateTime ReadDate { get; set; }

   
    [Range(0, int.MaxValue)]
    public int Amount { get; set; }

    
    [Range(0, int.MaxValue)]
    public int? PageReachedAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReadDate > DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "Okuma tarihi gelecekte olamaz.",
                new[] { nameof(ReadDate) });
        }
    }
}