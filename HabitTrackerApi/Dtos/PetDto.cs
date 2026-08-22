namespace Dtos;

public class PetDto
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }
    public required string Mood { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Nickname { get; set; }
}
