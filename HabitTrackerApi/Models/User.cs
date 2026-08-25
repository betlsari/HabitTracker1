using Microsoft.AspNetCore.Identity;

namespace Models;

public class User : IdentityUser
{
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? DisplayName { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(2048)]
    public string? AvatarUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Habit> Habits { get; set; } = new List<Habit>();

    public int TotalXp { get; set; }

    public List<Pet> Pets { get; set; } = new List<Pet>();

    public string TimeZoneId { get; set; } = "Europe/Istanbul";

    public List<UserBadge> UserBadges { get; set; } = new List<UserBadge>();

    public List<UserNotification> Notifications { get; set; } = new List<UserNotification>();

    public List<DeviceToken> DeviceTokens { get; set; } = new List<DeviceToken>();

    public Flower? Flower { get; set; }

    public List<Book> Books { get; set; } = new List<Book>();

    
    public string EquippedBackground { get; set; } = PetBackgrounds.Home;

    public List<UserBackgroundUnlock> BackgroundUnlocks { get; set; } = new();
}