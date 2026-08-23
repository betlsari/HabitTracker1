using Data;
using Dtos;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

/// <summary>
/// YENİ: Pet aksesuarları (şapka, gözlük, papyon) ve arka planların (ev, orman, sahil)
/// kilit açma/donatma (equip) mantığını yönetir. Aksesuarlar pet'in Level'ına, arka
/// planlar kullanıcının çiçek (Flower) seviyesine bağlı olarak açılır ve kalıcı olarak
/// saklanır — bu sayede pet XP'si sonradan azalsa bile (ör. bir habit completion
/// silindiğinde) daha önce kazanılmış kozmetikler geri alınmaz.
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