namespace Models;

public class Flower : IHasConcurrencyToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }

    public int WaterAmount { get; set; }

    public int Level { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
