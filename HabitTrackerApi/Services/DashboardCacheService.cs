using System.Text.Json;
using Dtos;
using Microsoft.Extensions.Caching.Distributed;

namespace Services;

public sealed class DashboardCacheService
{
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(2);
    private const string Prefix = "dashboard:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<DashboardCacheService> _logger;

    public DashboardCacheService(IDistributedCache cache, ILogger<DashboardCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public static string BuildKey(string userId) => $"{Prefix}{userId}";

    
    public async Task<DashboardDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await _cache.GetAsync(BuildKey(userId), cancellationToken);
            if (payload is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<DashboardDto>(payload);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Dashboard cache okunamadı (Redis erişilemez olabilir). UserId={UserId}. DB'den hesaplanacak.",
                userId);
            return null;
        }
    }

    public async Task SetAsync(string userId, DashboardDto dashboard, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(dashboard);

            await _cache.SetAsync(
                BuildKey(userId),
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = AbsoluteExpiration,
                    SlidingExpiration = SlidingExpiration
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
           
            _logger.LogWarning(ex,
                "Dashboard cache yazılamadı (Redis erişilemez olabilir). UserId={UserId}.",
                userId);
        }
    }

    public async Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(BuildKey(userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            
            _logger.LogWarning(ex,
                "Dashboard cache invalidation başarısız oldu (Redis erişilemez olabilir). UserId={UserId}.",
                userId);
        }
    }
}