namespace Models;

public class RefreshToken : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public Guid FamilyId { get; set; }

   
    public string? ReplacedByTokenHash { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
