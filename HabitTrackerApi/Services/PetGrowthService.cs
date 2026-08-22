using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;


public class PetGrowthService
{
    // 1 dakika odaklanma = bu kadar pet XP'si
    public const int XpPerFocusMinute = 2;

    private readonly AppDbContext _context;
    private readonly NotificationService _notifications;

    public PetGrowthService(AppDbContext context, NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<List<Pet>> AddFocusXpAsync(string userId, int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes <= 0)
        {
            return new List<Pet>();
        }

        var pets = await _context.Pets
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (pets.Count == 0)
        {
            return pets;
        }

        var xpGain = minutes * XpPerFocusMinute;
        var justHatched = new List<Pet>();

        foreach (var pet in pets)
        {
            pet.Xp += xpGain;

            if (PetHatching.TryHatch(pet))
            {
                justHatched.Add(pet);
            }

            if (pet.Stage == PetStage.Hatched)
            {
                pet.Level = pet.Xp / 100;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var pet in justHatched)
        {
            await _notifications.TryEnqueueAsync(
                userId,
                NotificationTypes.PetHatched,
                "Yumurta çatladı!",
                $"{(pet.Nickname ?? pet.Type)} yumurtasından çıktı!",
                habitId: null,
                dedupKey: $"pethatch:{pet.Id}",
                cancellationToken);
        }

        return pets;
    }

    
    public async Task RemoveFocusXpAsync(string userId, int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes <= 0)
        {
            return;
        }

        var pets = await _context.Pets
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (pets.Count == 0)
        {
            return;
        }

        var xpLoss = minutes * XpPerFocusMinute;

        foreach (var pet in pets)
        {
            pet.Xp = Math.Max(0, pet.Xp - xpLoss);
            if (pet.Stage == PetStage.Hatched)
            {
                pet.Level = pet.Xp / 100;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}