using Data;
using Models;

namespace Services;

public sealed class AuthAuditService
{
    private readonly AppDbContext _context;
    public AuthAuditService(AppDbContext context) => _context = context;

    public Task RecordAsync(HttpContext httpContext, string eventType, bool succeeded, User? user = null, string? email = null, string? detail = null)
    {
        _context.AuthAuditEvents.Add(new AuthAuditEvent
        {
            UserId = user?.Id, Email = email ?? user?.Email ?? string.Empty,
            EventType = eventType, Succeeded = succeeded,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(), Detail = detail,
            CreatedAt = DateTime.UtcNow
        });
        return _context.SaveChangesAsync();
    }
}
