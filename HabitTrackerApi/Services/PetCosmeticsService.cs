using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

/// <summary>
/// Pet aksesuarları (şapka, gözlük, papyon) ve arka planların (ev, orman, sahil)
/// kilit açma/donatma (equip) mantığını yönetir. Aksesuarlar pet'in Level'ına
/// VEYA kullanıcının kazandığı belirli rozetlere (bkz. BadgeAccessoryUnlocks)
/// bağlı olarak açılır; arka planlar kullanıcının çiçek (Flower) seviyesine
/// bağlı olarak açılır. Kilitler kalıcı olarak saklanır — bu sayede pet
/// XP'si sonradan azalsa bile (ör. bir habit completion silindiğinde) daha
/// önce kazanılmış kozmetikler geri alınmaz.
/// </summary>
public class PetCosmeticsService
{
    private static readonly (int Level, string Accessory)[] AccessoryThresholds =
    {
        (3, PetAccessories.Hat),
        (6, PetAccessories.Glasses),
        (10, PetAccessories.Bowtie)
    };

    private static readonly (int Level, string Background)[] BackgroundThresholds =
    {
        (5, PetBackgrounds.Forest),
        (10, PetBackgrounds.Beach)
    };

    // YENİ: "Rozet veya başarılarla aksesuar açma" — daha önce sadece pet
    // Level'ına bağlı olan aksesuarlar artık ilgili rozet kazanıldığında da
    // (pet'in Level'ı henüz eşiğe ulaşmamış olsa bile) açılabiliyor. Level
    // eşikleriyle çakışmasın diye aynı aksesuar setine bilinçli olarak
    // eşlendi; kullanıcı iki yoldan birinden (streak ile level, ya da streak
    // rozetiyle doğrudan) aynı aksesuara ulaşabilir.
    private static readonly Dictionary<string, string> BadgeAccessoryUnlocks = new(StringComparer.Ordinal)
    {
        [BadgeService.Streak3] = PetAccessories.Hat,
        [BadgeService.Streak7] = PetAccessories.Glasses,
        [BadgeService.Streak30] = PetAccessories.Bowtie,
    };

    private readonly AppDbContext _context;

    public PetCosmeticsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EvaluateAccessoryUnlocksAsync(Pet pet, CancellationToken cancellationToken = default)
    {
        var alreadyUnlocked = await _context.PetAccessoryUnlocks
            .Where(u => u.PetId == pet.Id)
            .Select(u => u.Accessory)
            .ToListAsync(cancellationToken);

        var newUnlocks = false;
        foreach (var (level, accessory) in AccessoryThresholds)
        {
            if (pet.Level < level || alreadyUnlocked.Contains(accessory))
            {
                continue;
            }

            _context.PetAccessoryUnlocks.Add(new PetAccessoryUnlock
            {
                PetId = pet.Id,
                Accessory = accessory,
                UnlockedAt = DateTime.UtcNow
            });
            newUnlocks = true;
        }

        if (newUnlocks)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // YENİ: BadgeService bir rozet verdiğinde çağrılır. İlgili rozet bir
    // aksesuara eşleniyorsa, kullanıcının TÜM pet'leri için (pet Level'ından
    // bağımsız olarak) o aksesuarı açar. Zaten açılmış olan pet'ler atlanır.
    public async Task EvaluateAccessoryUnlocksForBadgeAsync(
        string userId, string badgeCode, CancellationToken cancellationToken = default)
    {
        if (!BadgeAccessoryUnlocks.TryGetValue(badgeCode, out var accessory))
        {
            return;
        }

        var petIds = await _context.Pets.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (petIds.Count == 0)
        {
            return;
        }

        var alreadyUnlockedPetIds = await _context.PetAccessoryUnlocks
            .Where(u => petIds.Contains(u.PetId) && u.Accessory == accessory)
            .Select(u => u.PetId)
            .ToListAsync(cancellationToken);

        var newUnlocks = false;
        foreach (var petId in petIds.Except(alreadyUnlockedPetIds))
        {
            _context.PetAccessoryUnlocks.Add(new PetAccessoryUnlock
            {
                PetId = petId,
                Accessory = accessory,
                UnlockedAt = DateTime.UtcNow
            });
            newUnlocks = true;
        }

        if (newUnlocks)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task EvaluateBackgroundUnlocksAsync(string userId, int flowerLevel, CancellationToken cancellationToken = default)
    {
        var alreadyUnlocked = await _context.UserBackgroundUnlocks
            .Where(u => u.UserId == userId)
            .Select(u => u.Background)
            .ToListAsync(cancellationToken);

        var newUnlocks = false;
        foreach (var (level, background) in BackgroundThresholds)
        {
            if (flowerLevel < level || alreadyUnlocked.Contains(background))
            {
                continue;
            }

            _context.UserBackgroundUnlocks.Add(new UserBackgroundUnlock
            {
                UserId = userId,
                Background = background,
                UnlockedAt = DateTime.UtcNow
            });
            newUnlocks = true;
        }

        if (newUnlocks)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<PetAccessoryDto>> GetCatalogForPetAsync(Pet pet, CancellationToken cancellationToken = default)
    {
        var unlocked = await _context.PetAccessoryUnlocks
            .AsNoTracking()
            .Where(u => u.PetId == pet.Id)
            .Select(u => u.Accessory)
            .ToListAsync(cancellationToken);

        return PetAccessories.Allowed.Select(a => new PetAccessoryDto
        {
            Code = a,
            Unlocked = unlocked.Contains(a),
            Equipped = string.Equals(pet.EquippedAccessory, a, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    public async Task<List<BackgroundDto>> GetCatalogForUserAsync(string userId, string equippedBackground, CancellationToken cancellationToken = default)
    {
        var unlocked = await _context.UserBackgroundUnlocks
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.Background)
            .ToListAsync(cancellationToken);

        return PetBackgrounds.Allowed.Select(b => new BackgroundDto
        {
            Code = b,
            Unlocked = b == PetBackgrounds.Home || unlocked.Contains(b),
            Equipped = string.Equals(equippedBackground, b, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    public async Task<bool> TryEquipAccessoryAsync(Pet pet, string? accessory, CancellationToken cancellationToken = default)
    {
        if (accessory == null)
        {
            pet.EquippedAccessory = null;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (!PetAccessories.IsValid(accessory))
        {
            return false;
        }

        var unlocked = await _context.PetAccessoryUnlocks
            .AnyAsync(u => u.PetId == pet.Id && u.Accessory == accessory, cancellationToken);

        if (!unlocked)
        {
            return false;
        }

        pet.EquippedAccessory = accessory;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryEquipBackgroundAsync(User user, string background, CancellationToken cancellationToken = default)
    {
        if (!PetBackgrounds.IsValid(background))
        {
            return false;
        }

        if (background != PetBackgrounds.Home)
        {
            var unlocked = await _context.UserBackgroundUnlocks
                .AnyAsync(u => u.UserId == user.Id && u.Background == background, cancellationToken);

            if (!unlocked)
            {
                return false;
            }
        }

        user.EquippedBackground = background;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}