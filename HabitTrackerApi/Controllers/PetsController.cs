// HabitTrackerApi/Controllers/PetsController.cs
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
    private readonly ILogger<PetsController> _logger;
    private readonly int _maxPetsPerUser;

    // DÜZELTİLDİ (🟡 magic number): eggCost/feedCost/petXpGain artık
    // AppLimitsOptions üzerinden konfigüre edilebiliyor.
    private readonly int _eggCostXp;
    private readonly int _feedCostXp;
    private readonly int _feedXpGain;

    public PetsController(
        AppDbContext context,
        UserManager<User> userManager,
        NotificationService notificationService,
        PetCosmeticsService petCosmeticsService,
        IOptions<AppLimitsOptions> limits,
        ILogger<PetsController> logger)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
        _petCosmeticsService = petCosmeticsService;
        _maxPetsPerUser = limits.Value.MaxPetsPerUser;
        _eggCostXp = limits.Value.PetEggCostXp;
        _feedCostXp = limits.Value.PetFeedCostXp;
        _feedXpGain = limits.Value.PetFeedXpGain;
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

    // DÜZELTİLDİ (transaction eksikliği): yumurta maliyeti için kullanıcı
    // XP'sinin düşülmesi ile yeni Pet oluşturulması ayrı SaveChanges'lerdi.
    // Artık tek transaction.
    //
    // DÜZELTİLDİ (🔴 race condition): "mevcut pet sayısını say -> limiti
    // aşıyorsa reddet -> yeni pet ekle" adımları arasında hiçbir eşzamanlılık
    // koruması yoktu. Kullanıcıya özel bir Postgres advisory lock
    // (pg_advisory_xact_lock) eklendi; kilit transaction sonunda otomatik
    // serbest kalır.
    [HttpPost]
    public async Task<ActionResult<PetDto>> CreatePet(CreatePetDto dto)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<PetDto>>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({"pet:" + userId}))");

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
            Level = 0,
            Xp = 0,
            Mood = "Egg",
            Stage = PetStage.Egg,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Pets.Add(pet);
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
        return ToDto(pet);
        });
    }

    // DÜZELTİLDİ (🔴 race condition — YENİ): "kullanıcının yeterli XP'si var
    // mı kontrol et -> XP düş -> pet'e XP ekle" adımları arasında hiçbir
    // eşzamanlılık koruması YOKTU. Aynı kullanıcıdan gelen iki eşzamanlı
    // "besle" isteği, ikisi de aynı anda "TotalXp >= feedCost" görüp
    // kullanıcının XP'sini negatife düşürebiliyordu (klasik check-then-act).
    // CreatePet/DevicesController.Register ile aynı desende kullanıcıya özel
    // bir advisory lock eklendi; XP bakiyesiyle ilgili olduğu için CreatePet'in
    // kullandığı "pet:" anahtarından KASITLI OLARAK farklı bir anahtar
    // ("petfeed-xp:") kullanılmıyor — aslında ikisi de aynı kullanıcının
    // XP'sini ilgilendirdiği için aynı "pet:" + userId anahtarı kullanılarak
    // CreatePet ile de serileştiriliyor (yumurta maliyeti ile besleme
    // maliyeti aynı kullanıcı için birbirini de bekler, bu güvenlik açısından
    // sorun değildir, sadece ekstra bir serileşme).
    [HttpPost("{id}/feed")]
    public async Task<ActionResult<PetDto>> FeedPet(int id)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ActionResult<PetDto>>(async () =>
        {
        _context.ChangeTracker.Clear();
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({"pet:" + userId}))");

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

        await transaction.CommitAsync();
        return ToDto(pet);
        });
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