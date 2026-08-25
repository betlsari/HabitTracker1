namespace Services;

public interface IPushQueue
{
    Task EnqueueAsync(string userId, string title, string body, CancellationToken cancellationToken = default);
}