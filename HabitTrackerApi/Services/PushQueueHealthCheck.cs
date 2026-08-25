using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;


public class PushQueueHealthCheck : IHealthCheck
{
    private const int DegradedThreshold = 200;
    private const int UnhealthyThreshold = 2000;

    private readonly IPushOutboxProcessor _processor;

    public PushQueueHealthCheck(IPushOutboxProcessor processor)
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
                $"Push outbox'ta {pending} bekleyen bildirim var. PushSenderBackgroundService çalışmıyor olabilir.",
                data: data);
        }

        if (pending >= DegradedThreshold)
        {
            return HealthCheckResult.Degraded(
                $"Push outbox'ta {pending} bekleyen bildirim var.", data: data);
        }

        return HealthCheckResult.Healthy("Push outbox normal.", data);
    }
}