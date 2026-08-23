using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// AuthAuditEvents tablosu her login/2FA/parola işleminde büyür ve hiç
/// temizlenmiyordu. Bu servis belirli bir saklama süresinden eski kayıtları
/// periyodik olarak siler (RefreshTokenCleanupService ile aynı desen).
/// </summary>
public class AuthAuditCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthAuditCleanupService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(180);

    public AuthAuditCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthAuditCleanupService> logger)
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
                var deletedCount = await context.AuthAuditEvents
                    .Where(e => e.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deletedCount > 0)
                {
                    _logger.LogInformation(
                        "AuthAuditEvent temizliği tamamlandı. {Count} kayıt silindi.",
                        deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuthAuditEvent temizliği sırasında hata oluştu.");
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
}