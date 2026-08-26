namespace Models;

public class NotificationPreference
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public string DisabledTypes { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}