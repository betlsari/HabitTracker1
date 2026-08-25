using Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;

namespace Controllers;

[ApiController]
[Route("api/admin/email-dead-letters")]
[Authorize]
public sealed class EmailDeadLettersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailQueue _emailQueue;

    public EmailDeadLettersController(AppDbContext context, IConfiguration configuration, IEmailQueue emailQueue)
    {
        _context = context;
        _configuration = configuration;
        _emailQueue = emailQueue;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeadLetterResponse>>> List(CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return Forbid();

        return await _context.EmailDeadLetters.AsNoTracking()
            .Where(x => x.ResolvedAt == null)
            .OrderByDescending(x => x.FailedAt)
            .Take(100)
            .Select(x => new DeadLetterResponse
            {
                Id = x.Id,
                ToEmail = x.ToEmail,
                Subject = x.Subject,
                AttemptCount = x.AttemptCount,
                LastError = x.LastError,
                FailedAt = x.FailedAt
            })
            .ToListAsync(cancellationToken);
    }

    [HttpPost("{id:long}/retry")]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return Forbid();

        var item = await _context.EmailDeadLetters.FirstOrDefaultAsync(x => x.Id == id && x.ResolvedAt == null, cancellationToken);
        if (item == null) return NotFound();

        await _emailQueue.EnqueueAsync(new EmailMessage(item.ToEmail, item.Subject, item.Body), cancellationToken);
        item.ResolvedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return Accepted(new { item.Id });
    }

    private bool IsAdmin()
    {
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var admins = _configuration.GetSection("Admin:Emails").Get<string[]>() ?? Array.Empty<string>();
        return email != null && admins.Contains(email, StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DeadLetterResponse
    {
        public long Id { get; set; }
        public required string ToEmail { get; set; }
        public required string Subject { get; set; }
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
        public DateTime FailedAt { get; set; }
    }
}
