using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

/// <summary>
/// GDPR/KVKK veri taşınabilirliği için kullanıcının tüm verisini tek bir
/// JSON yapısında toplar.
/// </summary>
public class UserDataExportService
{
    private readonly AppDbContext _context;

    public UserDataExportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserDataExportDto> ExportAsync(User user, CancellationToken cancellationToken = default)
    {
        var habits = await _context.Habits.AsNoTracking()
            .Where(h => h.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var habitIds = habits.Select(h => h.Id).ToList();

        var completions = await _context.HabitCompletions.AsNoTracking()
            .Where(c => habitIds.Contains(c.HabitId))
            .ToListAsync(cancellationToken);

        var books = await _context.Books.AsNoTracking()
            .Where(b => b.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var bookIds = books.Select(b => b.Id).ToList();

        var readingLogs = await _context.BookReadingLogs.AsNoTracking()
            .Where(l => bookIds.Contains(l.BookId))
            .ToListAsync(cancellationToken);

        var pets = await _context.Pets.AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var petIds = pets.Select(p => p.Id).ToList();

        var badges = await _context.UserBadges.AsNoTracking()
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var notifications = await _context.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var flower = await _context.Flowers.AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == user.Id, cancellationToken);

        var deviceTokens = await _context.DeviceTokens.AsNoTracking()
            .Where(d => d.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var petAccessoryUnlocks = petIds.Count == 0
            ? new List<PetAccessoryUnlock>()
            : await _context.PetAccessoryUnlocks.AsNoTracking()
                .Where(u => petIds.Contains(u.PetId))
                .ToListAsync(cancellationToken);

        var backgroundUnlocks = await _context.UserBackgroundUnlocks.AsNoTracking()
            .Where(u => u.UserId == user.Id)
            .Select(u => u.Background)
            .ToListAsync(cancellationToken);

        var notificationPreference = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        var authAuditEvents = await _context.AuthAuditEvents.AsNoTracking()
            .Where(e => e.UserId == user.Id || (user.Email != null && e.Email == user.Email))
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return new UserDataExportDto
        {
            ExportedAt = DateTime.UtcNow,
            Account = new UserAccountExportDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                CreatedAt = user.CreatedAt,
                TotalXp = user.TotalXp,
                TimeZoneId = user.TimeZoneId,
                TwoFactorEnabled = user.TwoFactorEnabled,
                EquippedBackground = user.EquippedBackground
            },
            // DÜZELTİLDİ: CustomCategoryName, Unit, TargetTime, ReminderTime,
            // Notes, IsArchived, ArchivedAt eklendi.
            Habits = habits.Select(h => new HabitExportDto
            {
                Id = h.Id,
                Name = h.Name,
                Category = h.Category,
                CustomCategoryName = h.CustomCategoryName,
                Unit = h.Unit.ToString(),
                DailyGoal = h.DailyGoal,
                Period = h.Period.ToString(),
                TargetTime = h.TargetTime,
                ReminderTime = h.ReminderTime,
                Notes = h.Notes,
                IsArchived = h.IsArchived,
                ArchivedAt = h.ArchivedAt,
                CreatedAt = h.CreatedAt
            }).ToList(),
            // DÜZELTİLDİ: IsOnTime, PetStreakBonusXp eklendi.
            HabitCompletions = completions.Select(c => new HabitCompletionExportDto
            {
                HabitId = c.HabitId,
                CompletionDate = c.CompletionDate,
                Amount = c.Amount,
                XpEarned = c.XpEarned,
                IsOnTime = c.IsOnTime,
                PetStreakBonusXp = c.PetStreakBonusXp
            }).ToList(),
            // DÜZELTİLDİ: Period, DailyGoalAmount, Notes, CoverImageUrl,
            // IsArchived, ArchivedAt, CompletedAt eklendi.
            Books = books.Select(b => new BookExportDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                GoalType = b.GoalType.ToString(),
                Period = b.Period.ToString(),
                DailyGoalAmount = b.DailyGoalAmount,
                TotalPages = b.TotalPages,
                CurrentPage = b.CurrentPage,
                TotalMinutesRead = b.TotalMinutesRead,
                IsCompleted = b.IsCompleted,
                CreatedAt = b.CreatedAt,
                CompletedAt = b.CompletedAt,
                IsArchived = b.IsArchived,
                ArchivedAt = b.ArchivedAt,
                Notes = b.Notes,
                CoverImageUrl = b.CoverImageUrl
            }).ToList(),
            // DÜZELTİLDİ: PageReachedAt eklendi.
            BookReadingLogs = readingLogs.Select(l => new BookReadingLogExportDto
            {
                BookId = l.BookId,
                ReadDate = l.ReadDate,
                Amount = l.Amount,
                PageReachedAt = l.PageReachedAt,
                XpEarned = l.XpEarned
            }).ToList(),
            // DÜZELTİLDİ: Mood, EquippedAccessory, HatchedAt eklendi.
            Pets = pets.Select(p => new PetExportDto
            {
                Id = p.Id,
                Type = p.Type,
                Nickname = p.Nickname,
                Level = p.Level,
                Xp = p.Xp,
                Mood = p.Mood,
                Stage = p.Stage.ToString(),
                HatchedAt = p.HatchedAt,
                EquippedAccessory = p.EquippedAccessory,
                CreatedAt = p.CreatedAt
            }).ToList(),
            Badges = badges.Select(ub => new BadgeExportDto
            {
                Code = ub.Badge?.Code ?? string.Empty,
                Name = ub.Badge?.Name ?? string.Empty,
                EarnedAt = ub.EarnedAt
            }).ToList(),
            // DÜZELTİLDİ: HabitId, DedupKey eklendi.
            Notifications = notifications.Select(n => new NotificationExportDto
            {
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                HabitId = n.HabitId,
                DedupKey = n.DedupKey,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            }).ToList(),
            Flower = flower == null ? null : new FlowerExportDto
            {
                WaterAmount = flower.WaterAmount,
                Level = flower.Level,
                CreatedAt = flower.CreatedAt
            },
            DeviceTokens = deviceTokens.Select(d => new DeviceTokenExportDto
            {
                Platform = d.Platform,
                CreatedAt = d.CreatedAt,
                LastSeenAt = d.LastSeenAt
            }).ToList(),
            PetAccessoryUnlocks = petAccessoryUnlocks.Select(u => new PetAccessoryUnlockExportDto
            {
                PetId = u.PetId,
                Accessory = u.Accessory,
                UnlockedAt = u.UnlockedAt
            }).ToList(),
            BackgroundUnlocks = backgroundUnlocks,
            NotificationPreference = notificationPreference == null ? null : new NotificationPreferenceExportDto
            {
                DisabledTypes = string.IsNullOrWhiteSpace(notificationPreference.DisabledTypes)
                    ? new List<string>()
                    : notificationPreference.DisabledTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                QuietHoursStart = notificationPreference.QuietHoursStart,
                QuietHoursEnd = notificationPreference.QuietHoursEnd
            },
            AuthAuditEvents = authAuditEvents.Select(e => new AuthAuditEventExportDto
            {
                EventType = e.EventType,
                Succeeded = e.Succeeded,
                IpAddress = e.IpAddress,
                UserAgent = e.UserAgent,
                Detail = e.Detail,
                CreatedAt = e.CreatedAt
            }).ToList()
        };
    }
}