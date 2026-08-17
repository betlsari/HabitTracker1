using Microsoft.AspNetCore.Mvc;
using Data;
using Models;
using Microsoft.AspNetCore.Authorization;
using Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Controllers;
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PetsController : ControllerBase
{
    private readonly AppDbContext _context;

    public PetsController(AppDbContext context)
    {
        _context = context;
    }
[HttpPost]
    public async Task<ActionResult<PetDto>> CreatePet(CreatePetDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var hasExistingPet = await _context.Pets.AnyAsync(p => p.UserId == userId);
        if (hasExistingPet)
        {
            return BadRequest("User already has a pet.");
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
}