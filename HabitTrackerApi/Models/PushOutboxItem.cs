namespace Models;

public enum PushOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3
}


public class PushOutboxItem : IHasConcurrencyToken
{
    public long Id { get; set; }

    public required string UserId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }

    public PushOutboxStatus Status { get; set; } = PushOutboxStatus.Pending;

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }

    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}