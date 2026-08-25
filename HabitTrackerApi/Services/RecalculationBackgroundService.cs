using System.Diagnostics;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Models;
using Services.Observability;

namespace Services;


public class RecalculationBackgroundService : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecalculationBackgroundService> _logger;
    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public RecalculationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecalculationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task<int> ProcessPendingBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRecalculationOutboxProcessor>();
        var batch = await processor.ClaimBatchAsync(100, _workerId, cancellationToken);

        foreach (var item in batch)
        {
            try
            {
                RecalculationJob? job = item.JobType switch
                {
                    RecalculationJobType.Habit when item.HabitId.HasValue =>
                        new HabitRecalculationJob(item.HabitId.Value, item.UserId, item.TimeZoneId),
                    RecalculationJobType.Book when item.BookId.HasValue =>
                        new BookRecalculationJob(item.BookId.Value, item.UserId, item.TimeZoneId),
                    _ => null
                };

                if (job == null)
                {
                    await processor.MarkFailedAsync(item.Id, "Geçersiz recalculation outbox kaydı.", null, cancellationToken);
                    continue;
                }

                await ProcessWithRetryAsync(job, cancellationToken);
                await processor.MarkCompletedAsync(item.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                await processor.MarkFailedAsync(item.Id, ex.Message, DateTime.UtcNow.AddSeconds(30), cancellationToken);
                _logger.LogError(ex, "Recalculation outbox kaydı işlenemedi. Id={Id}", item.Id);
            }
        }

        return batch.Count;
    }

    private async Task ProcessWithRetryAsync(RecalculationJob job, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var activityName = job is HabitRecalculationJob ? "RecalculateHabit" : "RecalculateBook";
            using var activity = AppDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Internal);
            activity?.SetTag("habittracker.user_id", job.UserId);
            activity?.SetTag("habittracker.attempt", attempt);
            if (job is HabitRecalculationJob habitJob)
            {
                activity?.SetTag("habittracker.habit_id", habitJob.HabitId);
            }
            else if (job is BookRecalculationJob bookJob)
            {
                activity?.SetTag("habittracker.book_id", bookJob.BookId);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await ProcessJobAsync(job, stoppingToken);
                stopwatch.Stop();
                AppDiagnostics.RecalculationDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
                AppDiagnostics.RecalculationSucceeded.Add(1);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex,
                    "Arka plan yeniden hesaplama başarısız (deneme {Attempt}/{Max}). JobType={JobType} UserId={UserId}",
                    attempt, MaxAttempts, job.GetType().Name, job.UserId);

                try
                {
                    await Task.Delay(RetryDelays[attempt - 1], stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                AppDiagnostics.RecalculationFailed.Add(1);
                _logger.LogError(ex,
                    "Arka plan yeniden hesaplama tüm denemelerden sonra başarısız oldu. JobType={JobType} UserId={UserId}",
                    job.GetType().Name, job.UserId);
            }
        }
    }

    private async Task ProcessJobAsync(RecalculationJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        switch (job)
        {
            case HabitRecalculationJob habitJob:
                await ProcessHabitJobAsync(scope, context, userManager, habitJob, cancellationToken);
                break;
            case BookRecalculationJob bookJob:
                await ProcessBookJobAsync(scope, context, userManager, bookJob, cancellationToken);
                break;
        }
    }

    private static async Task ProcessHabitJobAsync(
        IServiceScope scope,
        AppDbContext context,
        UserManager<User> userManager,
        HabitRecalculationJob job,
        CancellationToken cancellationToken)
    {
        var habit = await context.Habits.FirstOrDefaultAsync(h => h.Id == job.HabitId, cancellationToken);
        if (habit == null)
        {
            
            return;
        }

        var progressService = scope.ServiceProvider.GetRequiredService<HabitProgressService>();
        var petGrowthService = scope.ServiceProvider.GetRequiredService<PetGrowthService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RecalculationBackgroundService>>();

        var recalc = await progressService.RecalculateHabitAsync(habit, job.TimeZoneId, cancellationToken);

        if (recalc.XpDelta != 0)
        {
            var user = await userManager.FindByIdAsync(job.UserId);
            if (user != null)
            {
                user.TotalXp = Math.Max(0, user.TotalXp + recalc.XpDelta);
                var updateResult = await userManager.UpdateAsync(user);
                updateResult.EnsureSucceeded(logger, "habit-recalc-background-xp", job.UserId);
            }
        }

        if (recalc.PetStreakBonusDelta > 0)
        {
            await petGrowthService.AddStreakBonusXpAsync(job.UserId, recalc.PetStreakBonusDelta, cancellationToken);
        }
        else if (recalc.PetStreakBonusDelta < 0)
        {
            await petGrowthService.RemoveStreakBonusXpAsync(job.UserId, -recalc.PetStreakBonusDelta, cancellationToken);
        }
    }

    private static async Task ProcessBookJobAsync(
        IServiceScope scope,
        AppDbContext context,
        UserManager<User> userManager,
        BookRecalculationJob job,
        CancellationToken cancellationToken)
    {
        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == job.BookId, cancellationToken);
        if (book == null)
        {
            return;
        }

        var bookService = scope.ServiceProvider.GetRequiredService<BookService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RecalculationBackgroundService>>();

        var xpDelta = await bookService.RecalculateBookAsync(book, job.TimeZoneId, cancellationToken);
        if (xpDelta != 0)
        {
            var user = await userManager.FindByIdAsync(job.UserId);
            if (user != null)
            {
                user.TotalXp = Math.Max(0, user.TotalXp + xpDelta);
                var updateResult = await userManager.UpdateAsync(user);
                updateResult.EnsureSucceeded(logger, "book-recalc-background-xp", job.UserId);
            }
        }
    }
}