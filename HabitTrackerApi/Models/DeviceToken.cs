namespace Models;

public class DeviceToken : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public required string Token { get; set; }
    public string Platform { get; set; } = "unknown";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
