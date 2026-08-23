namespace Models;

public class PetAccessoryUnlock : IHasConcurrencyToken
{
    public int Id { get; set; }
    public int PetId { get; set; }
    public Pet? Pet { get; set; }
    public required string Accessory { get; set; }
    public DateTime UnlockedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}