using Microsoft.Extensions.Caching.Memory;

namespace Services;

public class SecurityStampCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private const string NullMarker = "\u0000__NULL_SECURITY_STAMP__\u0000";

    private readonly IMemoryCache _cache;

    public SecurityStampCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    private static string BuildKey(string userId) => $"sstamp:{userId}";

    public Task<(bool Found, string? SecurityStamp)> TryGetAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(BuildKey(userId), out string? value))
        {
            return Task.FromResult((true, value == NullMarker ? null : value));
        }
        return Task.FromResult((false, (string?)null));
    }

    public Task SetAsync(string userId, string? securityStamp, CancellationToken cancellationToken = default)
    {
        _cache.Set(BuildKey(userId), securityStamp ?? NullMarker, CacheDuration);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
    {
        _cache.Remove(BuildKey(userId));
        return Task.CompletedTask;
    }
}