using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Dtos;
using Models;
using Services;
using System.Security.Claims;
using Asp.Versioning;

namespace Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Authorize]
public class BackgroundsController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly PetCosmeticsService _petCosmeticsService;

    public BackgroundsController(UserManager<User> userManager, PetCosmeticsService petCosmeticsService)
    {
        _userManager = userManager;
        _petCosmeticsService = petCosmeticsService;
    }

    [HttpGet]
    public async Task<ActionResult<List<BackgroundDto>>> GetBackgrounds()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        return await _petCosmeticsService.GetCatalogForUserAsync(userId, user.EquippedBackground);
    }

    [HttpPut("equip")]
    public async Task<IActionResult> Equip(EquipBackgroundDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var ok = await _petCosmeticsService.TryEquipBackgroundAsync(user, dto.Background);
        if (!ok)
        {
            return BadRequest("Bu arka plan henüz açılmamış veya geçersiz.");
        }

        return Ok(new { user.EquippedBackground });
    }
}