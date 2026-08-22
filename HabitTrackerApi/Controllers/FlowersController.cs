using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;
using System.Security.Claims;
using Dtos;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FlowersController : ControllerBase
{
    private readonly FlowerService _flowerService;

    public FlowersController(FlowerService flowerService)
    {
        _flowerService = flowerService;
    }

    [HttpGet]
    public async Task<ActionResult<FlowerDto>> GetFlower()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var flower = await _flowerService.GetOrCreateAsync(userId);
        return FlowerService.ToDto(flower);
    }
}
