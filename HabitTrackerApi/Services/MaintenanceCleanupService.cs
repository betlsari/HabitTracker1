using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class MaintenanceCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionAfterRevocation = TimeSpan.FromDays(30);
    private static readonly TimeSpan OutboxRetention = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MaintenanceCleanupService> _logger;

    public MaintenanceCleanupService(IServiceScopeFactory scopeFactory, ILogger<MaintenanceCleanupService> logger)
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

                await CleanupRefreshTokensAsync(context, stoppingToken);
                await CleanupOutboxAsync(context, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bakım temizliği sırasında hata oluştu.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private async Task CleanupRefreshTokensAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var revokedCutoff = now - RetentionAfterRevocation;

        var deletedCount = await context.RefreshTokens
            .Where(rt => rt.ExpiresAt < now || (rt.RevokedAt != null && rt.RevokedAt < revokedCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("RefreshToken temizliği tamamlandı. {Count} kayıt silindi.", deletedCount);
        }
    }

    private async Task CleanupOutboxAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - OutboxRetention;

        var emails = await context.EmailOutboxItems
            .Where(e => (e.Status == EmailOutboxStatus.Sent && e.SentAt < cutoff) ||
                        (e.Status == EmailOutboxStatus.Failed && e.CreatedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var recalculations = await context.RecalculationOutboxItems
            .Where(r => (r.Status == RecalculationOutboxStatus.Completed && r.CompletedAt < cutoff) ||
                        (r.Status == RecalculationOutboxStatus.Failed && r.CreatedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        if (emails > 0 || recalculations > 0)
        {
            _logger.LogInformation(
                "Outbox retention temizliği tamamlandı. Emails={Emails}, Recalculations={Recalculations}",
                emails, recalculations);
        }
    }
}