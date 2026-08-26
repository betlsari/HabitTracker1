namespace Models;

public class NotificationPreference 
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public string DisabledTypes { get; set; } = string.Empty;

  
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }

    public bool DigestEnabled { get; set; }
    public int DigestHourUtc { get; set; } = 19;

    public DateTime UpdatedAt { get; set; }

   
}