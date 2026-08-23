using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services;


public class FcmHealthCheck : IHealthCheck
{
    private readonly FcmAccessTokenProvider _tokenProvider;

    public FcmHealthCheck(FcmAccessTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_tokenProvider.IsConfigured)
        {
            return HealthCheckResult.Healthy("FCM yapılandırılmamış (push bildirimleri devre dışı).");
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            return HealthCheckResult.Degraded("FCM yapılandırılmış ama access token alınamadı. Push bildirimleri çalışmıyor olabilir.");
        }

        return HealthCheckResult.Healthy("FCM erişilebilir.");
    }
}