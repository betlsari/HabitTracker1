using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;


public class RedisHealthCheck : IHealthCheck
{
    private const string ProbeKeyPrefix = "healthcheck:probe:";
    private static readonly TimeSpan ProbeTtl = TimeSpan.FromSeconds(10);

    private readonly IDistributedCache _cache;

    public RedisHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var probeKey = $"{ProbeKeyPrefix}{Guid.NewGuid():N}";
        var probeValue = DateTime.UtcNow.Ticks.ToString();

        try
        {
            await _cache.SetStringAsync(
                probeKey,
                probeValue,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ProbeTtl },
                cancellationToken);

            var readBack = await _cache.GetStringAsync(probeKey, cancellationToken);

            
            try
            {
                await _cache.RemoveAsync(probeKey, cancellationToken);
            }
            catch
            {
                // yut - sadece probe temizliği, kritik değil
            }

            if (readBack != probeValue)
            {
                return HealthCheckResult.Unhealthy(
                    "Redis'e yazılan probe değeri okunamadı veya eşleşmedi.");
            }

            return HealthCheckResult.Healthy("Redis erişilebilir.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis'e erişilemiyor.", ex);
        }
    }
}