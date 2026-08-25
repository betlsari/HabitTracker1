
namespace Models;


public class NotificationDigestDelivery : IHasConcurrencyToken
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public DateOnly DigestDate { get; set; }
    public DateTime CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}