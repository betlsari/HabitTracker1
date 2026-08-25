namespace Models;


public enum RecalculationJobType
{
    Habit = 0,
    Book = 1
}

public enum RecalculationOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public class RecalculationOutboxItem : IHasConcurrencyToken
{
    public long Id { get; set; }

    public RecalculationJobType JobType { get; set; }

    public int? HabitId { get; set; }
    public int? BookId { get; set; }

    public required string UserId { get; set; }
    public string? TimeZoneId { get; set; }

    public RecalculationOutboxStatus Status { get; set; } = RecalculationOutboxStatus.Pending;

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}