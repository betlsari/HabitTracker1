namespace Models;

public class UserNotification : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int? HabitId { get; set; }
    public bool IsRead { get; set; }
    public required string DedupKey { get; set; }
    public DateTime CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
