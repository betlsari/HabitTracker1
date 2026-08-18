using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateCompletionDto : IValidatableObject
{
    public DateTime CompletionDate { get; set; }

    [Range(0, int.MaxValue)]
    public int Amount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CompletionDate > DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "Completion date cannot be in the future.",
                new[] { nameof(CompletionDate) });
        }
    }
}