using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;
using System.Security.Claims;
using Dtos;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BadgesController : ControllerBase
{
    private readonly BadgeService _badgeService;

    public BadgesController(BadgeService badgeService)
    {
        _badgeService = badgeService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BadgeDto>>> GetBadges()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await _badgeService.GetCatalogForUserAsync(userId);
    }
}
