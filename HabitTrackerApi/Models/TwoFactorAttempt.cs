namespace Models;

public class TwoFactorAttempt : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    
    public User? User { get; set; }

    public int FailedCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}