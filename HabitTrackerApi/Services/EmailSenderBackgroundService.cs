namespace Services;

public class EmailSenderBackgroundService : BackgroundService
{
    private const int MaxAttempts = 5;
    private const int BatchSize = 20;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailSenderBackgroundService> _logger;

    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public EmailSenderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailSenderBackgroundService> logger)
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
                _logger.LogError(ex, "Email outbox işlenirken beklenmedik hata oluştu.");
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
        var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

        var batch = await processor.ClaimBatchAsync(BatchSize, _workerId, stoppingToken);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var item in batch)
        {
            try
            {
                await emailService.SendEmailAsync(item.ToEmail, item.Subject, item.Body);
                await processor.MarkSentAsync(item.Id, stoppingToken);
            }
            catch (Exception ex)
            {
                var nextAttemptNumber = item.AttemptCount + 1;
                if (nextAttemptNumber < MaxAttempts)
                {
                    var delay = RetryDelays[Math.Min(item.AttemptCount, RetryDelays.Length - 1)];
                    _logger.LogWarning(ex,
                        "Email gönderimi başarısız (deneme {Attempt}/{Max}). To={To} Id={Id}",
                        nextAttemptNumber, MaxAttempts, item.ToEmail, item.Id);
                    await processor.MarkFailedAsync(item.Id, ex.Message, DateTime.UtcNow.Add(delay), stoppingToken);
                }
                else
                {
                    
                    _logger.LogError(ex,
                        "Email gönderimi tüm denemelerden sonra kalıcı olarak başarısız oldu. To={To} Subject={Subject} Id={Id}",
                        item.ToEmail, item.Subject, item.Id);
                    await processor.MarkFailedAsync(item.Id, ex.Message, null, stoppingToken);
                }
            }
        }
    }
}