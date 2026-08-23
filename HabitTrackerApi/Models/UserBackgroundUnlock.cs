namespace Models;

public class UserBackgroundUnlock : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public required string Background { get; set; }
    public DateTime UnlockedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}