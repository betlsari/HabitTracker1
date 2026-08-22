using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class FlowerService
{
    public const int UnitsPerLevel = 10;

    private readonly AppDbContext _context;

    // YENİ: Çiçek yeni bir evreye (Seed/Seedling/Sprout/Bloom) geçtiğinde
    // bildirim göndermek için eklendi.
    private readonly NotificationService _notifications;

    public FlowerService(AppDbContext context, NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<Flower> GetOrCreateAsync(string userId, CancellationToken cancellationToken = default)
    {
        var flower = await _context.Flowers.FirstOrDefaultAsync(f => f.UserId == userId, cancellationToken);
        if (flower != null)
        {
            return flower;
        }

        flower = new Flower
        {
            UserId = userId,
            WaterAmount = 0,
            Level = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Flowers.Add(flower);
        await _context.SaveChangesAsync(cancellationToken);
        return flower;
    }

    // DÜZELTİLDİ: Level yükselip yeni bir evreye (StageName değişimi) geçildiğinde
    // FlowerStageUp bildirimi gönderiliyor. amount negatifse (ör. bir habit
    // completion'ı silinirken suyun geri alınması) Level artamayacağı için bu
    // durumda hiçbir bildirim tetiklenmez — koşul zaten "yeni seviye eski
    // seviyeden büyükse" olduğundan ayrı bir "sadece artışta" bayrağına gerek yok.
    public async Task<Flower> AddWaterAsync(string userId, int amount, CancellationToken cancellationToken = default)
    {
        var flower = await GetOrCreateAsync(userId, cancellationToken);
        var oldLevel = flower.Level;

        flower.WaterAmount = Math.Max(0, flower.WaterAmount + amount);
        flower.Level = flower.WaterAmount / UnitsPerLevel;
        flower.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        if (flower.Level > oldLevel)
        {
            var oldStage = StageName(oldLevel);
            var newStage = StageName(flower.Level);
            if (!string.Equals(oldStage, newStage, StringComparison.Ordinal))
            {
                await _notifications.TryEnqueueAsync(
                    userId,
                    NotificationTypes.FlowerStageUp,
                    "Çiçeğin büyüdü!",
                    $"Çiçeğin artık '{newStage}' evresinde.",
                    habitId: null,
                    dedupKey: $"flowerstage:{userId}:{flower.Level}",
                    cancellationToken);
            }
        }

        return flower;
    }

    public static FlowerDto ToDto(Flower flower) => new()
    {
        Id = flower.Id,
        WaterAmount = flower.WaterAmount,
        Level = flower.Level,
        Stage = StageName(flower.Level),
        CreatedAt = flower.CreatedAt,
        UpdatedAt = flower.UpdatedAt
    };

    public static string StageName(int level)
    {
        if (level >= 10) return "Bloom";
        if (level >= 5) return "Sprout";
        if (level >= 1) return "Seedling";
        return "Seed";
    }
}