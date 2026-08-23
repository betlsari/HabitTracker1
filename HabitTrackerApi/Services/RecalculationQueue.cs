using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Services;


public abstract record RecalculationJob(string UserId, string? TimeZoneId);

public sealed record HabitRecalculationJob(int HabitId, string UserId, string? TimeZoneId)
    : RecalculationJob(UserId, TimeZoneId);

public sealed record BookRecalculationJob(int BookId, string UserId, string? TimeZoneId)
    : RecalculationJob(UserId, TimeZoneId);

public interface IRecalculationQueue
{
    void EnqueueHabitRecalculation(int habitId, string userId, string? timeZoneId);
    void EnqueueBookRecalculation(int bookId, string userId, string? timeZoneId);
    IAsyncEnumerable<RecalculationJob> DequeueAllAsync(CancellationToken cancellationToken);

    
    int PendingCount { get; }
}

public sealed class RecalculationQueue : IRecalculationQueue
{
    private readonly Channel<RecalculationJob> _channel =
        Channel.CreateUnbounded<RecalculationJob>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : -1;

    public void EnqueueHabitRecalculation(int habitId, string userId, string? timeZoneId)
    {
        if (!_channel.Writer.TryWrite(new HabitRecalculationJob(habitId, userId, timeZoneId)))
        {
            throw new InvalidOperationException("Yeniden hesaplama kuyruğuna yazılamadı.");
        }
    }

    public void EnqueueBookRecalculation(int bookId, string userId, string? timeZoneId)
    {
        if (!_channel.Writer.TryWrite(new BookRecalculationJob(bookId, userId, timeZoneId)))
        {
            throw new InvalidOperationException("Yeniden hesaplama kuyruğuna yazılamadı.");
        }
    }

    public async IAsyncEnumerable<RecalculationJob> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return job;
        }
    }
}