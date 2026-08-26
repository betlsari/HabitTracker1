using Data;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Services;

public sealed class MaintenanceCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionAfterRevocation = TimeSpan.FromDays(30);
   

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

    
}