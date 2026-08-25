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
    int PendingCount { get; }
}
