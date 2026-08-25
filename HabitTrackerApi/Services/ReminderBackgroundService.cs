using System.Diagnostics;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Models;
using Services.Observability;

namespace Services;


public class ReminderBackgroundService : BackgroundService   
{
    private const int MaxAttempts = 3;
    private const int BatchSize = 10;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(2)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundService> _logger;

    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderBackgroundService> logger)
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
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recalculation outbox işlenirken beklenmedik hata oluştu.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var claimScope = _scopeFactory.CreateScope();
        var processor = claimScope.ServiceProvider.GetRequiredService<IRecalculationOutboxProcessor>();

        var batch = await processor.ClaimBatchAsync(BatchSize, _workerId, stoppingToken);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var job in batch)
        {
            await ProcessJobWithOutcomeAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobWithOutcomeAsync(RecalculationOutboxItem job, CancellationToken stoppingToken)
    {
        var activityName = job.JobType == RecalculationJobType.Habit ? "RecalculateHabit" : "RecalculateBook";
        using var activity = AppDiagnostics.ActivitySource.StartActivity(activityName, ActivityKind.Internal);
        activity?.SetTag("habittracker.user_id", job.UserId);
        activity?.SetTag("habittracker.attempt", job.AttemptCount + 1);
        if (job.HabitId.HasValue) activity?.SetTag("habittracker.habit_id", job.HabitId.Value);
        if (job.BookId.HasValue) activity?.SetTag("habittracker.book_id", job.BookId.Value);

        var stopwatch = Stopwatch.StartNew();

        // İşi bilinçli olarak claim scope'undan AYRI, yeni bir scope
        // içinde çalıştırıyoruz: bu sayede uzun süren bir iş (örn. çok
        // completion'lı bir habit) claim/ack DbContext'ini uzun süre
        // meşgul etmez.
        using var workScope = _scopeFactory.CreateScope();

        try
        {
            await ProcessJobAsync(workScope, job, stoppingToken);
            stopwatch.Stop();
            AppDiagnostics.RecalculationDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
            AppDiagnostics.RecalculationSucceeded.Add(1);

            var processor = workScope.ServiceProvider.GetRequiredService<IRecalculationOutboxProcessor>();
            await processor.MarkCompletedAsync(job.Id, stoppingToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var processor = workScope.ServiceProvider.GetRequiredService<IRecalculationOutboxProcessor>();
            var nextAttemptNumber = job.AttemptCount + 1;

            if (nextAttemptNumber < MaxAttempts)
            {
                var delay = RetryDelays[Math.Min(job.AttemptCount, RetryDelays.Length - 1)];
                _logger.LogWarning(ex,
                    "Arka plan yeniden hesaplama başarısız (deneme {Attempt}/{Max}). JobType={JobType} UserId={UserId} Id={Id}",
                    nextAttemptNumber, MaxAttempts, job.JobType, job.UserId, job.Id);
                await processor.MarkFailedAsync(job.Id, ex.Message, DateTime.UtcNow.Add(delay), stoppingToken);
            }
            else
            {
                AppDiagnostics.RecalculationFailed.Add(1);
                _logger.LogError(ex,
                    "Arka plan yeniden hesaplama tüm denemelerden sonra kalıcı olarak başarısız oldu. JobType={JobType} UserId={UserId} Id={Id}",
                    job.JobType, job.UserId, job.Id);
                await processor.MarkFailedAsync(job.Id, ex.Message, null, stoppingToken);
            }
        }
    }

    private static async Task ProcessJobAsync(
        IServiceScope scope, RecalculationOutboxItem job, CancellationToken cancellationToken)
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        if (job.JobType == RecalculationJobType.Habit)
        {
            await ProcessHabitJobAsync(scope, context, userManager, job, cancellationToken);
        }
        else
        {
            await ProcessBookJobAsync(scope, context, userManager, job, cancellationToken);
        }
    }

    private static async Task ProcessHabitJobAsync(
        IServiceScope scope,
        AppDbContext context,
        UserManager<User> userManager,
        RecalculationOutboxItem job,
        CancellationToken cancellationToken)
    {
        if (job.HabitId is not int habitId)
        {
            return;
        }

        var habit = await context.Habits.FirstOrDefaultAsync(h => h.Id == habitId, cancellationToken);
        if (habit == null)
        {
            // Habit bu arada silinmiş olabilir; bu durumda iş sessizce
            // "tamamlandı" sayılır (yapılacak bir şey kalmadı).
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
        RecalculationOutboxItem job,
        CancellationToken cancellationToken)
    {
        if (job.BookId is not int bookId)
        {
            return;
        }

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
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