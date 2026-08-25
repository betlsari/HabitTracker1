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

    // DÜZELTİLDİ (🔴 rozet senkronizasyonu eksikliği): Önceden bu metod
    // sadece XP delta'sını ve pet streak bonus delta'sını uyguluyordu.
    // Retroaktif bir düzenleme (ör. DailyGoal/Period değişikliği) mevcut
    // dönemin streak'ini 3/7/30 eşiğine taşısa bile hiçbir rozet
    // (STREAK_3/7/30, READING_STREAK_7) verilmiyordu — bu değerlendirme
    // sadece senkron tekil completion akışlarında (HabitCompletionsController,
    // SyncController) yapılıyordu. Artık recalculation tamamlandıktan sonra
    // güncel dönem ilerlemesi (HabitProgressService.GetProgressAsync) okunup
    // BadgeService.EvaluateAfterCompletionAsync'e sentetik bir
    // CompletionSnapshot ile besleniyor; böylece geriye dönük düzenlemeler de
    // rozet kazanımını tetikleyebiliyor. AwardAsync idempotent olduğu için
    // bu ekstra çağrının yan etkisi yoktur (zaten kazanılmış rozetler
    // tekrar verilmez). Pet mood, ayrı bir periyodik job
    // (PetMoodBackgroundService, 6 saatte bir) tarafından zaten toptan
    // yeniden hesaplandığından burada tekrar tetiklenmiyor.
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
        var badgeService = scope.ServiceProvider.GetRequiredService<BadgeService>();
        var flowerService = scope.ServiceProvider.GetRequiredService<FlowerService>();
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

        // YENİ: Recalculation sonrası güncel dönem durumunu okuyup rozet
        // değerlendirmesini tetikle (bkz. yukarıdaki not).
        try
        {
            var progress = await progressService.GetProgressAsync(habit, job.TimeZoneId, cancellationToken);

            Flower? flower = null;
            if (HabitCategories.IsWater(habit.Category))
            {
                flower = await flowerService.GetOrCreateAsync(job.UserId, cancellationToken);
            }

            var snapshot = new CompletionSnapshot
            {
                TotalBeforeInPeriod = 0,
                TotalAfterInPeriod = progress.TotalInPeriod,
                GoalJustReached = progress.IsCompleted,
                PreviousPeriodGoalMet = progress.IsCompleted,
                StreakAfter = progress.CurrentStreak,
                PeriodStartLocal = progress.PeriodStart
            };

            await badgeService.EvaluateAfterCompletionAsync(job.UserId, habit, snapshot, flower, cancellationToken);
        }
        catch (Exception ex)
        {
            // Rozet değerlendirmesi asıl recalculation işinin başarısını
            // etkilememeli; sorun olursa sadece loglanır, iş yine de
            // tamamlanmış sayılır (rozetler idempotent olduğu için bir
            // sonraki completion/recalculation'da tekrar değerlendirilir).
            logger.LogWarning(ex,
                "Recalculation sonrası rozet değerlendirmesi başarısız oldu. HabitId={HabitId} UserId={UserId}",
                job.HabitId, job.UserId);
        }
    }

    // DÜZELTİLDİ (🔴 rozet senkronizasyonu eksikliği): Habit tarafındaki
    // aynı gerekçeyle, kitap recalculation'ı sonrası da güncel okuma serisi
    // (BookService.GetProgressAsync üzerinden) okunup
    // BadgeService.EvaluateAfterBookLogAsync tekrar çağrılıyor. Böylece
    // geçmişe dönük bir reading-log düzenlemesi READING_STREAK_7 eşiğine
    // ulaştırsa bile rozet artık kaçırılmıyor.
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
        var badgeService = scope.ServiceProvider.GetRequiredService<BadgeService>();
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

        // YENİ: Recalculation sonrası güncel okuma serisini okuyup
        // READING_STREAK_7 rozetini tekrar değerlendir.
        try
        {
            var progress = await bookService.GetProgressAsync(book, job.TimeZoneId, cancellationToken);
            await badgeService.EvaluateAfterBookLogAsync(job.UserId, progress.CurrentStreak, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Recalculation sonrası kitap rozet değerlendirmesi başarısız oldu. BookId={BookId} UserId={UserId}",
                job.BookId, job.UserId);
        }
    }
}