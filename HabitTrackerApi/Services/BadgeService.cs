using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class BadgeService
{
    public const string FirstCompletion = "FIRST_COMPLETION";
    public const string Streak3 = "STREAK_3";
    public const string Streak7 = "STREAK_7";
    public const string Streak30 = "STREAK_30";
    public const string ReadingStreak7 = "READING_STREAK_7";
    public const string WaterGrowth5 = "WATER_GROWTH_5";
    public const string WaterGrowth10 = "WATER_GROWTH_10";

    private readonly AppDbContext _context;
    private readonly NotificationService _notifications;

    // YENİ: Rozet kazanıldığında ilgili pet aksesuarını da açabilmek için.
    private readonly PetCosmeticsService _petCosmeticsService;

    public BadgeService(AppDbContext context, NotificationService notifications, PetCosmeticsService petCosmeticsService)
    {
        _context = context;
        _notifications = notifications;
        _petCosmeticsService = petCosmeticsService;
    }

    public async Task<List<BadgeDto>> GetCatalogForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var badges = await _context.Badges.AsNoTracking().OrderBy(b => b.Id).ToListAsync(cancellationToken);
        var earned = await _context.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .ToDictionaryAsync(ub => ub.BadgeId, ub => ub.EarnedAt, cancellationToken);

        return badges.Select(b => new BadgeDto
        {
            Id = b.Id,
            Code = b.Code,
            Name = b.Name,
            Description = b.Description,
            Earned = earned.ContainsKey(b.Id),
            EarnedAt = earned.TryGetValue(b.Id, out var at) ? at : null
        }).ToList();
    }

    public async Task EvaluateAfterCompletionAsync(
        string userId,
        Habit habit,
        CompletionSnapshot snapshot,
        Flower? flower,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.TotalAfterInPeriod > 0)
        {
            await AwardAsync(userId, FirstCompletion, cancellationToken);
        }

        if (snapshot.GoalJustReached)
        {
            if (snapshot.StreakAfter >= 3) await AwardAsync(userId, Streak3, cancellationToken);
            if (snapshot.StreakAfter >= 7) await AwardAsync(userId, Streak7, cancellationToken);
            if (snapshot.StreakAfter >= 30) await AwardAsync(userId, Streak30, cancellationToken);
            if (HabitCategories.IsReading(habit.Category) && snapshot.StreakAfter >= 7)
            {
                await AwardAsync(userId, ReadingStreak7, cancellationToken);
            }
        }

        if (flower != null)
        {
            if (flower.Level >= 5) await AwardAsync(userId, WaterGrowth5, cancellationToken);
            if (flower.Level >= 10) await AwardAsync(userId, WaterGrowth10, cancellationToken);
        }
    }

    /// <summary>
    /// Book/BookReadingLog akışı için rozet değerlendirmesi. Böylece
    /// "Kitap kurdu" (READING_STREAK_7) rozeti artık sadece Habit tabanlı
    /// "Okuma" kategorisine değil, gerçek kitap okuma günlük hedefine bağlı
    /// olarak da kazanılabiliyor.
    /// </summary>
    public async Task EvaluateAfterBookLogAsync(
        string userId,
        int streakAfterDays,
        CancellationToken cancellationToken = default)
    {
        if (streakAfterDays >= 7)
        {
            await AwardAsync(userId, ReadingStreak7, cancellationToken);
        }
    }

    private async Task AwardAsync(string userId, string code, CancellationToken cancellationToken)
    {
        var badge = await _context.Badges.FirstOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (badge == null)
        {
            return;
        }

        var alreadyEarned = await _context.UserBadges
            .AnyAsync(ub => ub.UserId == userId && ub.BadgeId == badge.Id, cancellationToken);
        if (alreadyEarned)
        {
            return;
        }

        _context.UserBadges.Add(new UserBadge
        {
            UserId = userId,
            BadgeId = badge.Id,
            EarnedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(cancellationToken);

        await _notifications.TryEnqueueAsync(
            userId,
            NotificationTypes.BadgeEarned,
            "Rozet kazandın",
            $"{badge.Name}: {badge.Description}",
            habitId: null,
            dedupKey: $"badge:{userId}:{badge.Code}",
            cancellationToken);

        
        await _petCosmeticsService.EvaluateAccessoryUnlocksForBadgeAsync(userId, code, cancellationToken);
    }
}