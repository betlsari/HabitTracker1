namespace Models;

public class Badge
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
}

public class UserBadge : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public int BadgeId { get; set; }
    public Badge? Badge { get; set; }
    public DateTime EarnedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
