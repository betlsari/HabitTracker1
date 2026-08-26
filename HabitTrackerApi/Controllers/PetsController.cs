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

using Filters;

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
    private readonly PetGrowthService _petGrowthService;
    private readonly ILogger<PetsController> _logger;
    private readonly int _maxPetsPerUser;

    private readonly int _eggCostXp;
    private readonly int _feedCostXp;
    private readonly int _feedXpGain;
    private readonly int _maxPetLevel;

    public PetsController(
        AppDbContext context,
        UserManager<User> userManager,
        NotificationService notificationService,
        PetCosmeticsService petCosmeticsService,
        PetGrowthService petGrowthService,
        IOptions<AppLimitsOptions> limits,
        ILogger<PetsController> logger)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
        _petCosmeticsService = petCosmeticsService;
        _petGrowthService = petGrowthService;
        _maxPetsPerUser = limits.Value.MaxPetsPerUser;
        _eggCostXp = limits.Value.PetEggCostXp;
        _feedCostXp = limits.Value.PetFeedCostXp;
        _feedXpGain = limits.Value.PetFeedXpGain;
        _maxPetLevel = limits.Value.MaxPetLevel;
        _logger = logger;
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
    [SanitizeText]
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
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.TotalXp < _eggCostXp)
            {
                return BadRequest($"Yeterli XP'niz yok. Yeni bir yumurta {_eggCostXp} XP gerektirir.");
            }
            user.TotalXp -= _eggCostXp;
            var updateResult = await _userManager.UpdateAsync(user);
            updateResult.EnsureSucceeded(_logger, "pet-egg-cost", userId);
        }

        var pet = new Pet
        {
            Type = dto.Type,
            Nickname = string.IsNullOrWhiteSpace(dto.Nickname) ? null : dto.Nickname.Trim(),
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

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.TotalXp < _feedCostXp)
        {
            return BadRequest($"Yeterli XP'niz yok. Beslemek için {_feedCostXp} XP gereklidir.");
        }

        user.TotalXp -= _feedCostXp;
        var updateResult = await _userManager.UpdateAsync(user);
        updateResult.EnsureSucceeded(_logger, "pet-feed-cost", userId);

        pet.Xp += _feedXpGain;

        var justHatched = PetHatching.TryHatch(pet);
        if (pet.Stage == PetStage.Hatched)
        {
            PetLeveling.Apply(pet, _maxPetLevel);
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

    // YENİ: Odaklanma alışkanlıklarından biriken FocusXpPool bakiyesinden
    // kullanıcının seçtiği XP kadarını, kullanıcının seçtiği TEK bir pet'e
    // aktarır. Odaklanma XP'si artık otomatik olarak tüm pet'lere dağılmıyor;
    // kullanıcı hangi pet'i büyütmek istediğine kendisi karar veriyor.
    [HttpPost("{id}/grow-from-focus")]
    public async Task<ActionResult<PetDto>> GrowFromFocusPool(int id, GrowPetFromFocusDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var pet = await _context.Pets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (pet == null)
        {
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        if (user.FocusXpPool < dto.Amount)
        {
            return BadRequest($"Yetersiz odaklanma XP'si. Mevcut bakiye: {user.FocusXpPool}");
        }

        user.FocusXpPool -= dto.Amount;
        var updateResult = await _userManager.UpdateAsync(user);
        updateResult.EnsureSucceeded(_logger, "pet-grow-from-focus-pool", userId);

        var grown = await _petGrowthService.GrowSinglePetAsync(userId, id, dto.Amount);
        if (grown == null)
        {
            // Teorik olarak yukarıda pet bulunduğu için buraya düşülmemeli,
            // ama savunmacı olarak XP'yi geri ver.
            user.FocusXpPool += dto.Amount;
            var revertResult = await _userManager.UpdateAsync(user);
            revertResult.EnsureSucceeded(_logger, "pet-grow-from-focus-pool-revert", userId);
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }

        return ToDto(grown);
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
    [SanitizeText]
    public async Task<ActionResult<PetDto>> UpdatePet(int id, UpdatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pet = await _context.Pets.FindAsync(id);
        if (pet == null || pet.UserId != userId)
        {
            return NotFound("Evcil hayvan bulunamadı veya bu evcil hayvana erişim yetkiniz yok.");
        }

        pet.Nickname = string.IsNullOrWhiteSpace(dto.Nickname) ? null : dto.Nickname.Trim();
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
    [HttpGet("types")]
    public ActionResult<IEnumerable<string>> GetAllowedTypes()
    {
        return Ok(PetTypes.Allowed);
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