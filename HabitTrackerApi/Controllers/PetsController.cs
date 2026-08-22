using Microsoft.AspNetCore.Mvc;
using Data;
using Models;
using Microsoft.AspNetCore.Authorization;
using Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Services;



namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly NotificationService _notificationService;

    // YENİ: Kullanıcı başına maksimum pet sayısı. Önceden hiçbir üst sınır
    // yoktu; eggCost sadece XP maliyeti getiriyordu ama teorik olarak kullanıcı
    // XP'si yettiği sürece sınırsız pet biriktirebiliyordu.
    private const int MaxPetsPerUser = 5;

    public PetsController(AppDbContext context, UserManager<User> userManager, NotificationService notificationService)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
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

        // YENİ: Üst sınır kontrolü.
        if (existingPetCount >= MaxPetsPerUser)
        {
            return BadRequest($"En fazla {MaxPetsPerUser} evcil hayvana sahip olabilirsiniz.");
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
        IsEgg = pet.Stage == PetStage.Egg
    };
}