namespace Models;

public class Pet : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public required string Type { get; set; }
    public int Level { get; set; }
    public int Xp { get; set; }

    
    public string Mood { get; set; } = "Happy";

    public DateTime CreatedAt { get; set; }

    public string? Nickname { get; set; }

    
    public PetStage Stage { get; set; } = PetStage.Egg;

    public DateTime? HatchedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
