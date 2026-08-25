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

    public DashboardCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public static string BuildKey(string userId) => $"{Prefix}{userId}";

    public async Task<DashboardDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var payload = await _cache.GetAsync(BuildKey(userId), cancellationToken);
        if (payload is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<DashboardDto>(payload);
    }

    public async Task SetAsync(string userId, DashboardDto dashboard, CancellationToken cancellationToken = default)
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

    public async Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(BuildKey(userId), cancellationToken);
    }
}
