using Microsoft.AspNetCore.Mvc;
using Data;
using Models;
using Microsoft.AspNetCore.Authorization;
using Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;



namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;

    public PetsController(AppDbContext context, UserManager<User> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PetDto>>> GetPets()
    {
       var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
       return await _context.Pets.Where(p => p.UserId == userId)
            .Select(p => new PetDto
            {
                Id = p.Id,
                Type = p.Type,
                Level = p.Level,
                Xp = p.Xp,
                Mood= p.Mood,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();
    }
[HttpPost]
    public async Task<ActionResult<PetDto>> CreatePet(CreatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var hasExistingPet = await _context.Pets.AnyAsync(p => p.UserId == userId);
        if(hasExistingPet)
        {
            const int eggCost= 50;
            var user = await _userManager.FindByIdAsync(userId);
            if(user == null || user.TotalXp < eggCost)
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
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Pets.Add(pet);
        await _context.SaveChangesAsync();

        var petDto = new PetDto
        {
            Id = pet.Id,
            Type = pet.Type,
            Level = pet.Level,
            Xp = pet.Xp,
            Mood= pet.Mood,
            CreatedAt = pet.CreatedAt
        };

        return petDto;
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
        pet.Level = pet.Xp / 100;
        await _context.SaveChangesAsync();

        var petDto = new PetDto
        {
            Id = pet.Id,
            Type = pet.Type,
            Level = pet.Level,
            Xp = pet.Xp,
            Mood= pet.Mood,
            CreatedAt = pet.CreatedAt
        };

        return petDto;
        
        
    }
    }

