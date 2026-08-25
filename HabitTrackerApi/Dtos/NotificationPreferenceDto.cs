using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class NotificationPreferenceDto
{
    public List<string> DisabledTypes { get; set; } = new();
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    public bool DigestEnabled { get; set; }
    public int DigestHourUtc { get; set; }
}

public class UpdateNotificationPreferenceDto : IValidatableObject
{
    // Boş liste = hiçbir tür kapalı değil (varsayılan, hepsi açık).
    public List<string> DisabledTypes { get; set; } = new();

    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    public bool DigestEnabled { get; set; }
    [Range(0, 23)]
    public int DigestHourUtc { get; set; } = 19;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (QuietHoursStart.HasValue != QuietHoursEnd.HasValue)
        {
            yield return new ValidationResult(
                "QuietHoursStart ve QuietHoursEnd birlikte belirtilmeli ya da ikisi de boş bırakılmalı.",
                new[] { nameof(QuietHoursStart), nameof(QuietHoursEnd) });
        }

        var validTypes = new[]
        {
            Models.NotificationTypes.Reminder, Models.NotificationTypes.Missed,
            Models.NotificationTypes.GoalReached, Models.NotificationTypes.BadgeEarned,
            Models.NotificationTypes.PetHatched, Models.NotificationTypes.BookGoalReached,
            Models.NotificationTypes.BookCompleted, Models.NotificationTypes.BookMissed,
            Models.NotificationTypes.BookStreakBroken, Models.NotificationTypes.FlowerStageUp,
            Models.NotificationTypes.StreakBroken
        };

        foreach (var type in DisabledTypes.Distinct())
        {
            if (!validTypes.Contains(type, StringComparer.Ordinal))
            {
                yield return new ValidationResult(
                    $"Geçersiz bildirim türü: {type}",
                    new[] { nameof(DisabledTypes) });
            }
        }
    }
}