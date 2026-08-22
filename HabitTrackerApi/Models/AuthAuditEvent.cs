namespace Models;

public class AuthAuditEvent : IHasConcurrencyToken
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
