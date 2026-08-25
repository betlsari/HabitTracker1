using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class OutboxCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxCleanupService> _logger;

    public OutboxCleanupService(IServiceScopeFactory scopeFactory, ILogger<OutboxCleanupService> logger)
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
                var cutoff = DateTime.UtcNow - Retention;
                var emails = await context.EmailOutboxItems
                    .Where(e => (e.Status == EmailOutboxStatus.Sent && e.SentAt < cutoff) ||
                                (e.Status == EmailOutboxStatus.Failed && e.CreatedAt < cutoff))
                    .ExecuteDeleteAsync(stoppingToken);
                var recalculations = await context.RecalculationOutboxItems
                    .Where(r => (r.Status == RecalculationOutboxStatus.Completed && r.CompletedAt < cutoff) ||
                                (r.Status == RecalculationOutboxStatus.Failed && r.CreatedAt < cutoff))
                    .ExecuteDeleteAsync(stoppingToken);
                if (emails > 0 || recalculations > 0)
                {
                    _logger.LogInformation("Outbox retention temizliği tamamlandı. Emails={Emails}, Recalculations={Recalculations}", emails, recalculations);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox retention temizliği sırasında hata oluştu.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}