namespace Services;

public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
    IAsyncEnumerable<EmailMessage> DequeueAllAsync(CancellationToken cancellationToken);
}