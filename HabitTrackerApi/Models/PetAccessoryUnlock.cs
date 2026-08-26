namespace Models;

public class PetAccessoryUnlock
{
    public int Id { get; set; }
    public int PetId { get; set; }
    public Pet? Pet { get; set; }
    public required string Accessory { get; set; }
    public DateTime UnlockedAt { get; set; }

   
}