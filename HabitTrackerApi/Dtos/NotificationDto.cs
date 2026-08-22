namespace Dtos;

public class NotificationDto
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int? HabitId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
