namespace Dtos;

public class AuthAuditEventDto
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public required string Email { get; set; }
    public required string EventType { get; set; }
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}