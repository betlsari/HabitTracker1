namespace Services;

public abstract record RecalculationJob(string UserId, string? TimeZoneId);

public sealed record HabitRecalculationJob(int HabitId, string UserId, string? TimeZoneId)
    : RecalculationJob(UserId, TimeZoneId);

public sealed record BookRecalculationJob(int BookId, string UserId, string? TimeZoneId)
    : RecalculationJob(UserId, TimeZoneId);

public interface IRecalculationQueue
{
    Task EnqueueHabitRecalculationAsync(int habitId, string userId, string? timeZoneId, CancellationToken cancellationToken = default);
    Task EnqueueBookRecalculationAsync(int bookId, string userId, string? timeZoneId, CancellationToken cancellationToken = default);
    int PendingCount { get; }
}
