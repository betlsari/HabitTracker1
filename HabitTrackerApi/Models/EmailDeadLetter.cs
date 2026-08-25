namespace Models;

public class EmailDeadLetter
{
    public long Id { get; set; }
    public long? OriginalOutboxId { get; set; }
    public required string ToEmail { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime FailedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}