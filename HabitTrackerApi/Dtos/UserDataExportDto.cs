namespace Dtos;

public class UserDataExportDto
{
    public DateTime ExportedAt { get; set; }
    public required UserAccountExportDto Account { get; set; }
    public required List<HabitExportDto> Habits { get; set; }
    public required List<HabitCompletionExportDto> HabitCompletions { get; set; }
    public required List<BookExportDto> Books { get; set; }
    public required List<BookReadingLogExportDto> BookReadingLogs { get; set; }
    public required List<PetExportDto> Pets { get; set; }
    public required List<BadgeExportDto> Badges { get; set; }
    public required List<NotificationExportDto> Notifications { get; set; }
    public FlowerExportDto? Flower { get; set; }
    public required List<DeviceTokenExportDto> DeviceTokens { get; set; }

    // YENİ: GDPR/KVKK veri taşınabilirliği kapsamında önceden export'a hiç
    // dahil edilmeyen veri türleri eklendi.
    public required List<PetAccessoryUnlockExportDto> PetAccessoryUnlocks { get; set; }
    public required List<string> BackgroundUnlocks { get; set; }
    public NotificationPreferenceExportDto? NotificationPreference { get; set; }
    public required List<AuthAuditEventExportDto> AuthAuditEvents { get; set; }
}

public class UserAccountExportDto
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalXp { get; set; }
    public required string TimeZoneId { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public required string EquippedBackground { get; set; }
}

public class HabitExportDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public int DailyGoal { get; set; }
    public required string Period { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HabitCompletionExportDto
{
    public int HabitId { get; set; }
    public DateTime CompletionDate { get; set; }
    public int Amount { get; set; }
    public int XpEarned { get; set; }
}

public class BookExportDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    public required string GoalType { get; set; }
    public int? TotalPages { get; set; }
    public int CurrentPage { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookReadingLogExportDto
{
    public int BookId { get; set; }
    public DateTime ReadDate { get; set; }
    public int Amount { get; set; }
    public int XpEarned { get; set; }
}

public class PetExportDto
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public string? Nickname { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }
    public required string Stage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BadgeExportDto
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public DateTime EarnedAt { get; set; }
}

public class NotificationExportDto
{
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FlowerExportDto
{
    public int WaterAmount { get; set; }
    public int Level { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DeviceTokenExportDto
{
    public required string Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

// YENİ
public class PetAccessoryUnlockExportDto
{
    public int PetId { get; set; }
    public required string Accessory { get; set; }
    public DateTime UnlockedAt { get; set; }
}

// YENİ
public class NotificationPreferenceExportDto
{
    public required List<string> DisabledTypes { get; set; }
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
}

// YENİ: Sadece bu kullanıcıya ait audit olayları (login, 2FA, şifre işlemleri vb.)
public class AuthAuditEventExportDto
{
    public required string EventType { get; set; }
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}