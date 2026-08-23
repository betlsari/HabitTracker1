using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;


public class RecalculationQueueHealthCheck : IHealthCheck
{
    private const int DegradedThreshold = 50;
    private const int UnhealthyThreshold = 500;

    private readonly IRecalculationQueue _queue;

    public RecalculationQueueHealthCheck(IRecalculationQueue queue)
    {
        _queue = queue;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = _queue.PendingCount;
        var data = new Dictionary<string, object> { ["pendingCount"] = pending };

        if (pending >= UnhealthyThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Yeniden hesaplama kuyruğunda {pending} bekleyen iş var. RecalculationBackgroundService çalışmıyor olabilir.",
                data: data));
        }

        if (pending >= DegradedThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Yeniden hesaplama kuyruğunda {pending} bekleyen iş var.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Yeniden hesaplama kuyruğu normal.", data));
    }
}