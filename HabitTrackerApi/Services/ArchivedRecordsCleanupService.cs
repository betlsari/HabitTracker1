using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class ArchivedRecordsCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArchivedRecordsCleanupService> _logger;

    public ArchivedRecordsCleanupService(IServiceScopeFactory scopeFactory, ILogger<ArchivedRecordsCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTime.UtcNow - RetentionPeriod;

                var habitIds = await context.Habits
                    .Where(h => h.IsArchived && h.ArchivedAt != null && h.ArchivedAt < cutoff)
                    .Select(h => h.Id)
                    .ToListAsync(stoppingToken);
                var bookIds = await context.Books
                    .Where(b => b.IsArchived && b.ArchivedAt != null && b.ArchivedAt < cutoff)
                    .Select(b => b.Id)
                    .ToListAsync(stoppingToken);

                if (habitIds.Count == 0 && bookIds.Count == 0)
                {
                    await Task.Delay(Interval, stoppingToken);
                    continue;
                }

                await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);
                var completionCount = habitIds.Count == 0
                    ? 0
                    : await context.HabitCompletions.Where(c => habitIds.Contains(c.HabitId)).ExecuteDeleteAsync(stoppingToken);
                var logCount = bookIds.Count == 0
                    ? 0
                    : await context.BookReadingLogs.Where(l => bookIds.Contains(l.BookId)).ExecuteDeleteAsync(stoppingToken);
                var habitCount = habitIds.Count == 0
                    ? 0
                    : await context.Habits.Where(h => habitIds.Contains(h.Id)).ExecuteDeleteAsync(stoppingToken);
                var bookCount = bookIds.Count == 0
                    ? 0
                    : await context.Books.Where(b => bookIds.Contains(b.Id)).ExecuteDeleteAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);

                _logger.LogInformation(
                    "Arşiv retention temizliği tamamlandı. Habits={HabitCount}, Books={BookCount}, Completions={CompletionCount}, ReadingLogs={LogCount}",
                    habitCount, bookCount, completionCount, logCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arşivlenmiş kayıt retention temizliği sırasında hata oluştu.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
