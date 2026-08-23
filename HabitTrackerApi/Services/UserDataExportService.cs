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

        // YENİ: Pet aksesuar kilitleri.
        var petAccessoryUnlocks = petIds.Count == 0
            ? new List<PetAccessoryUnlock>()
            : await _context.PetAccessoryUnlocks.AsNoTracking()
                .Where(u => petIds.Contains(u.PetId))
                .ToListAsync(cancellationToken);

        // YENİ: Arka plan kilitleri.
        var backgroundUnlocks = await _context.UserBackgroundUnlocks.AsNoTracking()
            .Where(u => u.UserId == user.Id)
            .Select(u => u.Background)
            .ToListAsync(cancellationToken);

        // YENİ: Bildirim tercihleri.
        var notificationPreference = await _context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

        // YENİ: Bu kullanıcıya ait auth audit olayları (login/2FA/şifre
        // geçmişi). Hem doğrudan UserId ile eşleşenler hem de (ör. hesap
        // henüz oluşturulmadan önceki bazı olaylarda olduğu gibi) email
        // üzerinden eşleşenler dahil edilir — AuthController.GetAuditLog
        // ile aynı sorgu deseni.
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
                CreatedAt = user.CreatedAt,
                TotalXp = user.TotalXp,
                TimeZoneId = user.TimeZoneId,
                TwoFactorEnabled = user.TwoFactorEnabled,
                EquippedBackground = user.EquippedBackground
            },
            Habits = habits.Select(h => new HabitExportDto
            {
                Id = h.Id,
                Name = h.Name,
                Category = h.Category,
                DailyGoal = h.DailyGoal,
                Period = h.Period.ToString(),
                CreatedAt = h.CreatedAt
            }).ToList(),
            HabitCompletions = completions.Select(c => new HabitCompletionExportDto
            {
                HabitId = c.HabitId,
                CompletionDate = c.CompletionDate,
                Amount = c.Amount,
                XpEarned = c.XpEarned
            }).ToList(),
            Books = books.Select(b => new BookExportDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                GoalType = b.GoalType.ToString(),
                TotalPages = b.TotalPages,
                CurrentPage = b.CurrentPage,
                IsCompleted = b.IsCompleted,
                CreatedAt = b.CreatedAt
            }).ToList(),
            BookReadingLogs = readingLogs.Select(l => new BookReadingLogExportDto
            {
                BookId = l.BookId,
                ReadDate = l.ReadDate,
                Amount = l.Amount,
                XpEarned = l.XpEarned
            }).ToList(),
            Pets = pets.Select(p => new PetExportDto
            {
                Id = p.Id,
                Type = p.Type,
                Nickname = p.Nickname,
                Level = p.Level,
                Xp = p.Xp,
                Stage = p.Stage.ToString(),
                CreatedAt = p.CreatedAt
            }).ToList(),
            Badges = badges.Select(ub => new BadgeExportDto
            {
                Code = ub.Badge?.Code ?? string.Empty,
                Name = ub.Badge?.Name ?? string.Empty,
                EarnedAt = ub.EarnedAt
            }).ToList(),
            Notifications = notifications.Select(n => new NotificationExportDto
            {
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
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