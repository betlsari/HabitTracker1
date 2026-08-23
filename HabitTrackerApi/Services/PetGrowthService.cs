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

    private readonly PetCosmeticsService _cosmeticsService;


    public PetGrowthService(AppDbContext context, NotificationService notifications,PetCosmeticsService cosmeticsService)
    {
        _context = context;
        _notifications = notifications;
        _cosmeticsService = cosmeticsService;
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

        var xpGain = checked(minutes * XpPerFocusMinute);
        return await ApplyXpGainAsync(userId, pets, xpGain, cancellationToken);
    }

    // DÜZELTİLDİ: AddFocusXpAsync'te olduğu gibi checked aritmetiğe alındı.
    // Önceden burada düz (unchecked) çarpma kullanılıyordu; AddFocusXpAsync
    // int.MaxValue civarında bir minutes değerinde OverflowException fırlatıp
    // isteği reddederken, RemoveFocusXpAsync aynı senaryoda sessizce taşıp
    // (wrap-around) yanlış/negatif bir xpLoss üretebiliyordu — iki yöndeki
    // davranış tutarsızdı.
    public async Task RemoveFocusXpAsync(string userId, int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes <= 0)
        {
            return;
        }

        var xpLoss = checked(minutes * XpPerFocusMinute);
        await RemoveXpAsync(userId, xpLoss, cancellationToken);
    }

        public async Task<List<Pet>> AddStreakBonusXpAsync(string userId, int bonusXp, CancellationToken cancellationToken = default)
    {
        if (bonusXp <= 0)
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

        return await ApplyXpGainAsync(userId, pets, bonusXp, cancellationToken);
    }

    
    public async Task RemoveStreakBonusXpAsync(string userId, int bonusXp, CancellationToken cancellationToken = default)
    {
        if (bonusXp <= 0)
        {
            return;
        }

        await RemoveXpAsync(userId, bonusXp, cancellationToken);
    }

    private async Task<List<Pet>> ApplyXpGainAsync(string userId, List<Pet> pets, int xpGain, CancellationToken cancellationToken)
    {
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
        foreach (var pet in pets.Where(p => p.Stage == PetStage.Hatched))
{
    await _cosmeticsService.EvaluateAccessoryUnlocksAsync(pet, cancellationToken);
}

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

    private async Task RemoveXpAsync(string userId, int xpLoss, CancellationToken cancellationToken)
    {
        var pets = await _context.Pets
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (pets.Count == 0)
        {
            return;
        }

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