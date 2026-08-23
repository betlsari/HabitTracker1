using Microsoft.Extensions.Caching.Memory;

namespace Services;


public class SecurityStampCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    public SecurityStampCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    private static string BuildKey(string userId) => $"sstamp:{userId}";

    public bool TryGet(string userId, out string? securityStamp) =>
        _cache.TryGetValue(BuildKey(userId), out securityStamp);

    public void Set(string userId, string? securityStamp)
    {
        _cache.Set(BuildKey(userId), securityStamp, CacheDuration);
    }

    public void Invalidate(string userId)
    {
        _cache.Remove(BuildKey(userId));
    }
}