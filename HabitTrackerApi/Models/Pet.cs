namespace Models;

public class Pet
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public required string Type { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }
    public string Mood { get; set; } = "Happy";
    public DateTime CreatedAt { get; set; }
}