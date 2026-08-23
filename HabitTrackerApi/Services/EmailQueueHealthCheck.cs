using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;


public class EmailQueueHealthCheck : IHealthCheck
{
    private const int DegradedThreshold = 100;
    private const int UnhealthyThreshold = 1000;

    private readonly IEmailQueue _queue;

    public EmailQueueHealthCheck(IEmailQueue queue)
    {
        _queue = queue;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_queue is not EmailQueue concreteQueue)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Email kuyruğu durumu izlenemiyor (bilinmeyen implementasyon)."));
        }

        var pending = concreteQueue.PendingCount;
        var data = new Dictionary<string, object> { ["pendingCount"] = pending };

        if (pending >= UnhealthyThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Email kuyruğunda {pending} bekleyen mesaj var. EmailSenderBackgroundService çalışmıyor olabilir.", data: data));
        }

        if (pending >= DegradedThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Email kuyruğunda {pending} bekleyen mesaj var.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Email kuyruğu normal.", data));
    }
}