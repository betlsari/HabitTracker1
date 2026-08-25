namespace Models;


public enum EmailOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3
}

public class EmailOutboxItem : IHasConcurrencyToken
{
    public long Id { get; set; }

    public required string ToEmail { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    // Yeniden deneme için ne zaman tekrar işleme alınabileceği (backoff).
    public DateTime? NextAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    // Bir worker bu satırı işlemeye başladığında doldurulur; teşhis/izleme
    // amaçlı, kilit mekanizması SKIP LOCKED üzerinden DB seviyesinde sağlanır.
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}