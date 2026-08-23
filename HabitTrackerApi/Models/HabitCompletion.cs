namespace Models;

public class HabitCompletion : IHasConcurrencyToken
{
    public int Id { get; set; }
    public int HabitId { get; set; }
    public DateTime CompletionDate { get; set; }

    public int Amount { get; set; }

    public Habit? Habit { get; set; }

    public int XpEarned { get; set; }

    public int PetStreakBonusXp { get; set; }

    public bool IsOnTime { get; set; }

    
    public string? ClientRequestId { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}