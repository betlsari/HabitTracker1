using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public class FlowerService
{
    public const int UnitsPerLevel = 10;

    private readonly AppDbContext _context;

    public FlowerService(AppDbContext context)
    {
        _context = context;
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

    public async Task<Flower> AddWaterAsync(string userId, int amount, CancellationToken cancellationToken = default)
    {
        var flower = await GetOrCreateAsync(userId, cancellationToken);
        flower.WaterAmount = Math.Max(0, flower.WaterAmount + amount);
        flower.Level = flower.WaterAmount / UnitsPerLevel;
        flower.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
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
