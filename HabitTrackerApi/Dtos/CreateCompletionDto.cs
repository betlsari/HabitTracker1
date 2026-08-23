using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class CreateCompletionDto : IValidatableObject
{
    public const int MaxAmount = 10_000;

    // YENİ: Geçmişe dönük üst sınır. Önceden sadece gelecek tarihler
    // engelleniyordu; sınırsız geçmiş tarih girilebiliyordu, bu da tarih
    // manipülasyonuyla XP/streak/badge "farming" yapılmasına açık kapı
    // bırakıyordu.
    public const int MaxPastDays = 3650; // 10 yıl

    public DateTime CompletionDate { get; set; }

    [Range(0, MaxAmount)]
    public int Amount { get; set; }

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