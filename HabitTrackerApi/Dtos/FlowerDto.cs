namespace Dtos;

public class FlowerDto
{
    public int Id { get; set; }
    public int WaterAmount { get; set; }
    public int Level { get; set; }
    public string Stage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
