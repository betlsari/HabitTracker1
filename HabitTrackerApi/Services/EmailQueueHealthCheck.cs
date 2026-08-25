using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;

/// <summary>
/// DÜZELTİLDİ: Artık kalıcı (DB-backed) EmailOutboxItems tablosundaki
/// GERÇEK bekleyen satır sayısını okuyor (önceden in-memory channel'ın
/// anlık Reader.Count'unu okuyordu — restart sonrası bu sayı her zaman
/// sıfırdan başlıyordu, dolayısıyla "birikmiş iş" hiç tespit edilemiyordu).
/// </summary>
public class EmailQueueHealthCheck : IHealthCheck
{
    private const int DegradedThreshold = 100;
    private const int UnhealthyThreshold = 1000;

    private readonly IEmailOutboxProcessor _processor;

    public EmailQueueHealthCheck(IEmailOutboxProcessor processor)
    {
        _processor = processor;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = await _processor.PendingCountAsync(cancellationToken);
        var data = new Dictionary<string, object> { ["pendingCount"] = pending };

        if (pending >= UnhealthyThreshold)
        {
            return HealthCheckResult.Unhealthy(
                $"Email outbox'ta {pending} bekleyen mesaj var. EmailSenderBackgroundService çalışmıyor olabilir.",
                data: data);
        }

        if (pending >= DegradedThreshold)
        {
            return HealthCheckResult.Degraded(
                $"Email outbox'ta {pending} bekleyen mesaj var.", data: data);
        }

        return HealthCheckResult.Healthy("Email outbox normal.", data);
    }
}