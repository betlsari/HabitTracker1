using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;


public class PushSenderBackgroundService : BackgroundService
{
    private const int MaxAttempts = 5;
    private const int BatchSize = 50;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushSenderBackgroundService> _logger;

    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public PushSenderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PushSenderBackgroundService> logger)
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
                _logger.LogError(ex, "Push outbox işlenirken beklenmedik hata oluştu.");
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
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPushOutboxProcessor>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pushSender = scope.ServiceProvider.GetRequiredService<IPushNotificationSender>();

        var batch = await processor.ClaimBatchAsync(BatchSize, _workerId, stoppingToken);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var item in batch)
        {
            try
            {
                var deviceTokens = await context.DeviceTokens
                    .AsNoTracking()
                    .Where(t => t.UserId == item.UserId)
                    .Select(t => t.Token)
                    .ToListAsync(stoppingToken);

                
                if (deviceTokens.Count == 0)
                {
                    await processor.MarkSentAsync(item.Id, stoppingToken);
                    continue;
                }

                await pushSender.SendAsync(deviceTokens, item.Title, item.Body, stoppingToken);
                await processor.MarkSentAsync(item.Id, stoppingToken);
            }
            catch (Exception ex)
            {
                var nextAttemptNumber = item.AttemptCount + 1;
                if (nextAttemptNumber < MaxAttempts)
                {
                    var delay = RetryDelays[Math.Min(item.AttemptCount, RetryDelays.Length - 1)];
                    _logger.LogWarning(ex,
                        "Push bildirimi gönderimi başarısız (deneme {Attempt}/{Max}). UserId={UserId} Id={Id}",
                        nextAttemptNumber, MaxAttempts, item.UserId, item.Id);
                    await processor.MarkFailedAsync(item.Id, ex.Message, DateTime.UtcNow.Add(delay), stoppingToken);
                }
                else
                {
                    _logger.LogError(ex,
                        "Push bildirimi gönderimi tüm denemelerden sonra kalıcı olarak başarısız oldu. " +
                        "UserId={UserId} Title={Title} Id={Id}",
                        item.UserId, item.Title, item.Id);
                    await processor.MarkFailedAsync(item.Id, ex.Message, null, stoppingToken);
                }
            }
        }
    }
}