using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;
using System.Security.Claims;
using Dtos;

namespace Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _notificationService;

    public NotificationsController(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications([FromQuery] bool unreadOnly = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await _notificationService.ListAsync(userId, unreadOnly);
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ok = await _notificationService.MarkReadAsync(userId, id);
        if (!ok)
        {
            return NotFound();
        }

        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var count = await _notificationService.MarkAllReadAsync(userId);
        return Ok(new { markedRead = count });
    }

    // YENİ: Tek bir bildirimi siler.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ok = await _notificationService.DeleteAsync(userId, id);
        if (!ok)
        {
            return NotFound();
        }

        return NoContent();
    }

    
    [HttpDelete("read")]
    public async Task<IActionResult> DeleteAllRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var count = await _notificationService.DeleteAllReadAsync(userId);
        return Ok(new { deleted = count });
    }
}