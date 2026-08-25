using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace Services;


public class SecurityStampCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private const string NullMarker = "\u0000__NULL_SECURITY_STAMP__\u0000";

    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurityStampCache> _logger;

    public SecurityStampCache(IDistributedCache cache, ILogger<SecurityStampCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    private static string BuildKey(string userId) => $"sstamp:{userId}";

    
    public async Task<(bool Found, string? SecurityStamp)> TryGetAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _cache.GetAsync(BuildKey(userId), cancellationToken);
            if (bytes == null)
            {
                return (false, null);
            }

            var value = Encoding.UTF8.GetString(bytes);
            return (true, value == NullMarker ? null : value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "SecurityStampCache okunamadı (Redis erişilemez olabilir). UserId={UserId}. DB'ye düşülüyor.",
                userId);
            return (false, null);
        }
    }

    public async Task SetAsync(
        string userId, string? securityStamp, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(securityStamp ?? NullMarker);
            await _cache.SetAsync(
                BuildKey(userId),
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
           
            _logger.LogWarning(ex,
                "SecurityStampCache yazılamadı (Redis erişilemez olabilir). UserId={UserId}.",
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
            _logger.LogError(ex,
                "SecurityStampCache invalidation başarısız oldu (Redis erişilemez olabilir). " +
                "UserId={UserId}. Eski access token'lar cache TTL'i (~{TtlSeconds}s) boyunca geçerli kalabilir.",
                userId, (int)CacheDuration.TotalSeconds);
        }
    }
}