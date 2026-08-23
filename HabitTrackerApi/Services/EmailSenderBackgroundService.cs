namespace Services;

/// <summary>
/// Kuyruktaki e-postaları arka planda gönderir. Başarısız denemelerde
/// üstel geri çekilme (exponential backoff) ile sınırlı sayıda tekrar dener;
/// tüm denemeler tükenirse kaybolmasın diye loglar (production'da burada
/// bir "dead letter" tabloya/kalıcı depoya yazmak önerilir).
/// </summary>
public class EmailSenderBackgroundService : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30)
    };

    private readonly IEmailQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailSenderBackgroundService> _logger;

    public EmailSenderBackgroundService(
        IEmailQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailSenderBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.DequeueAllAsync(stoppingToken))
        {
            await SendWithRetryAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetryAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                await emailService.SendEmailAsync(message.ToEmail, message.Subject, message.Body);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex,
                    "Email gönderimi başarısız (deneme {Attempt}/{Max}). To={To}",
                    attempt, MaxAttempts, message.ToEmail);

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
                _logger.LogError(ex,
                    "Email gönderimi tüm denemelerden sonra başarısız oldu. To={To} Subject={Subject}",
                    message.ToEmail, message.Subject);
            }
        }
    }
}