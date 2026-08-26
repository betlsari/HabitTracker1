namespace Models;

using System.ComponentModel.DataAnnotations;

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

    public string? Nickname { get; set; }

    public PetStage Stage { get; set; } = PetStage.Egg;

    public DateTime? HatchedAt { get; set; }

    
    public string? EquippedAccessory { get; set; }

    public List<PetAccessoryUnlock> AccessoryUnlocks { get; set; } = new();

    
}