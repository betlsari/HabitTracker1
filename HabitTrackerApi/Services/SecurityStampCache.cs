using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace Services;


public class SecurityStampCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    
    private const string NullMarker = "\u0000__NULL_SECURITY_STAMP__\u0000";

    private readonly IDistributedCache _cache;

    public SecurityStampCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    private static string BuildKey(string userId) => $"sstamp:{userId}";

    
    public async Task<(bool Found, string? SecurityStamp)> TryGetAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var bytes = await _cache.GetAsync(BuildKey(userId), cancellationToken);
        if (bytes == null)
        {
            return (false, null);
        }

        var value = Encoding.UTF8.GetString(bytes);
        return (true, value == NullMarker ? null : value);
    }

    public async Task SetAsync(
        string userId, string? securityStamp, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetBytes(securityStamp ?? NullMarker);
        await _cache.SetAsync(
            BuildKey(userId),
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration },
            cancellationToken);
    }

    
    public async Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(BuildKey(userId), cancellationToken);
    }
}