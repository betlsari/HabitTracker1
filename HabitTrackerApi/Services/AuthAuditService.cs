using Data;
using Microsoft.AspNetCore.Http;
using Models;

namespace Services;

public class AuthAuditService
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthAuditService> _logger;

    public AuthAuditService(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthAuditService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(
        string eventType,
        string email,
        bool succeeded,
        string? userId = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            _context.AuthAuditEvents.Add(new AuthAuditEvent
            {
                UserId = userId,
                Email = email,
                EventType = eventType,
                Succeeded = succeeded,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                Detail = detail,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit log yazımı asla ana akışı (login/register vb.) bloklamamalı.
            _logger.LogError(ex,
                "Audit log kaydı yazılamadı. EventType={EventType} Email={Email}",
                eventType, email);
        }
    }
}