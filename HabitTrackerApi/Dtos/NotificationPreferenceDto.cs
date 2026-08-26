using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class NotificationPreferenceDto
{
    public List<string> DisabledTypes { get; set; } = new();
}

public class UpdateNotificationPreferenceDto : IValidatableObject
{
    
    public List<string> DisabledTypes { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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