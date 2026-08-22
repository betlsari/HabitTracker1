using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// YENİ: RefreshTokens tablosu, her login/refresh işleminde yeni satır eklendiği
/// için süresiz büyüyordu; süresi dolmuş veya iptal edilmiş (RevokedAt dolu)
/// token'ları temizleyen hiçbir mekanizma yoktu. Bu servis periyodik olarak:
///   - Süresi dolmuş (ExpiresAt geçmişte) VE
///   - İptal edilmiş olup üzerinden makul bir süre geçmiş (RevokedAt dolu ve
///     RetentionAfterRevocation süresinden eski)
/// kayıtları veritabanından siler. Aktif/geçerli tokenlara dokunmaz.
/// </summary>
public class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    // İptal edilmiş bir token'ı, denetim/forensik amaçlı bir süre daha saklıyoruz;
    // sadece bu süreden eski olanlar siliniyor.
    private static readonly TimeSpan RetentionAfterRevocation = TimeSpan.FromDays(30);

    public RefreshTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefreshTokenCleanupService> logger)
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

                var now = DateTime.UtcNow;
                var revokedCutoff = now - RetentionAfterRevocation;

                var deletedCount = await context.RefreshTokens
                    .Where(rt =>
                        rt.ExpiresAt < now ||
                        (rt.RevokedAt != null && rt.RevokedAt < revokedCutoff))
                    .ExecuteDeleteAsync(stoppingToken);

                if (deletedCount > 0)
                {
                    _logger.LogInformation(
                        "RefreshToken temizliği tamamlandı. {Count} kayıt silindi. {Time}",
                        deletedCount,
                        now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshToken temizliği sırasında hata oluştu.");
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