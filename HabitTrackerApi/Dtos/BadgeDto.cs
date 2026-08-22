namespace Dtos;

public class BadgeDto
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool Earned { get; set; }
    public DateTime? EarnedAt { get; set; }
}
