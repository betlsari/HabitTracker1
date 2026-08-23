using Microsoft.AspNetCore.Mvc;
using Data;
using Models;
using Microsoft.AspNetCore.Authorization;
using Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Services;
using Microsoft.Extensions.Options;
using Configuration;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly NotificationService _notificationService;
    private readonly PetCosmeticsService _petCosmeticsService;

    // DÜZELTİLDİ: Sabit (hardcoded) "MaxPetsPerUser = 5" yerine, HabitsController
    // ve DevicesController ile aynı desende IOptions<AppLimitsOptions> üzerinden
    // config'ten okunuyor. Böylece appsettings.json'daki AppLimits:MaxPetsPerUser
    // artık gerçekten etkili oluyor.
    private readonly int _maxPetsPerUser;

    public PetsController(
        AppDbContext context,
        UserManager<User> userManager,
        NotificationService notificationService,
        PetCosmeticsService petCosmeticsService,
        IOptions<AppLimitsOptions> limits)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
        _petCosmeticsService = petCosmeticsService;
        _maxPetsPerUser = limits.Value.MaxPetsPerUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<PetDto>>> GetPets(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var query = _context.Pets.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt);

        var totalCount = await query.CountAsync();
        var pets = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<PetDto>
        {
            Items = pets.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    [HttpPost]
    public async Task<ActionResult<PetDto>> CreatePet(CreatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (!PetTypes.IsValid(dto.Type))
        {
            return BadRequest($"Geçersiz evcil hayvan türü. İzin verilen türler: {string.Join(", ", PetTypes.Allowed)}");
        }

        var existingPetCount = await _context.Pets.CountAsync(p => p.UserId == userId);

        if (existingPetCount >= _maxPetsPerUser)
        {
            return BadRequest($"En fazla {_maxPetsPerUser} evcil hayvana sahip olabilirsiniz.");
        }

        if (existingPetCount > 0)
        {
            const int eggCost = 50;
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.TotalXp < eggCost)
            {
                return BadRequest($"Yeterli XP'niz yok. Yeni bir yumurta {eggCost} XP gerektirir.");
            }
            user.TotalXp -= eggCost;
            await _userManager.UpdateAsync(user);
        }

        var pet = new Pet
        {
            Type = dto.Type,
            Level = 0,
            Xp = 0,
            Mood = "Egg",
            Stage = PetStage.Egg,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Pets.Add(pet);
        await _context.SaveChangesAsync();

        return ToDto(pet);
    }

    [HttpPost("{id}/feed")]
    public async Task<ActionResult<PetDto>> FeedPet(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }

        const int feedCost = 3;
        const int petXpGain = 20;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.TotalXp < feedCost)
        {
            return BadRequest($"Yeterli XP'niz yok. Beslemek için {feedCost} XP gereklidir.");
        }

        user.TotalXp -= feedCost;

        await _userManager.UpdateAsync(user);

        pet.Xp += petXpGain;

        var justHatched = PetHatching.TryHatch(pet);
        if (pet.Stage == PetStage.Hatched)
        {
            pet.Level = pet.Xp / 100;
        }

        await _context.SaveChangesAsync();
        await _petCosmeticsService.EvaluateAccessoryUnlocksAsync(pet);

        if (justHatched)
        {
            await _notificationService.TryEnqueueAsync(
                userId,
                NotificationTypes.PetHatched,
                "Yumurta çatladı!",
                $"{(pet.Nickname ?? pet.Type)} yumurtasından çıktı!",
                habitId: null,
                dedupKey: $"pethatch:{pet.Id}");
        }

        return ToDto(pet);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PetDto>> GetPet(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }
        return ToDto(pet);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PetDto>> UpdatePet(int id, UpdatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }

        pet.Nickname = dto.Nickname;
        await _context.SaveChangesAsync();

        return ToDto(pet);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePet(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);

        if (pet == null || pet.UserId != userId)
        {
            return NotFound();
        }
        _context.Pets.Remove(pet);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/accessories")]
    public async Task<ActionResult<List<PetAccessoryDto>>> GetAccessories(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound();
        }

        return await _petCosmeticsService.GetCatalogForPetAsync(pet);
    }

    [HttpPut("{id}/accessory")]
    public async Task<ActionResult<PetDto>> EquipAccessory(int id, EquipAccessoryDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound();
        }

        var ok = await _petCosmeticsService.TryEquipAccessoryAsync(pet, dto.Accessory);
        if (!ok)
        {
            return BadRequest("Bu aksesuar henüz açılmamış veya geçersiz.");
        }

        return ToDto(pet);
    }

    private static PetDto ToDto(Pet pet) => new()
    {
        Id = pet.Id,
        Type = pet.Type,
        Level = pet.Level,
        Xp = pet.Xp,
        Mood = pet.Mood,
        CreatedAt = pet.CreatedAt,
        Nickname = pet.Nickname,
        Stage = pet.Stage.ToString(),
        HatchedAt = pet.HatchedAt,
        IsEgg = pet.Stage == PetStage.Egg,
        EquippedAccessory = pet.EquippedAccessory
    };
}