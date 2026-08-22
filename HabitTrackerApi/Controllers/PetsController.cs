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

    public PetsController(AppDbContext context, UserManager<User> userManager, NotificationService notificationService)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PetDto>>> GetPets()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var pets = await _context.Pets.AsNoTracking().Where(p => p.UserId == userId).ToListAsync();
        return pets.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<PetDto>> CreatePet(CreatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // DÜZELTİLDİ: Type artık dokümanda belirtilen sabit seçeneklerle
        // sınırlandırılıyor (kedi, köpek, panda, tavşan). Önceden herhangi
        // bir string kabul ediliyordu.
        if (!PetTypes.IsValid(dto.Type))
        {
            return BadRequest($"Geçersiz evcil hayvan türü. İzin verilen türler: {string.Join(", ", PetTypes.Allowed)}");
        }

        var hasExistingPet = await _context.Pets.AnyAsync(p => p.UserId == userId);
        if (hasExistingPet)
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

        // YENİ: Pet, önce yumurta aşamasında oluşturulur (dokümandaki "kullanıcı
        // başlangıçta yumurta seçer" akışı). Level 0'da kalır ve Mood "Egg"
        // olarak sabitlenir; PetHatching.HatchXpThreshold XP'ye ulaşınca açılır.
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

        // YENİ: Hâlâ yumurtaysa Level artmaz; sadece hatch eşiğine yaklaşılır.
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

    // YENİ: pet güncelleme (şu an için takma isim). Type ve Mood bilinçli
    // olarak buradan değiştirilemiyor: Type sabit kalmalı, Mood ise sistem
    // tarafından (PetMoodService) otomatik hesaplanıyor.
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