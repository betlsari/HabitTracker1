using Microsoft.Extensions.Caching.Distributed;

namespace Services;

public sealed class TwoFactorFallbackCodeService
{
    private readonly IDistributedCache _cache;

    public TwoFactorFallbackCodeService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> GenerateCodeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var code = Random.Shared.Next(100000, 999999).ToString("D6");
        var key = BuildKey(userId);

        await _cache.SetStringAsync(
            key,
            code,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            },
            cancellationToken);

        return code;
    }

    public async Task<bool> ValidateCodeAsync(string userId, string? suppliedCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suppliedCode))
        {
            return false;
        }

        var key = BuildKey(userId);
        var storedCode = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(storedCode))
        {
            return false;
        }

        var matches = string.Equals(storedCode, suppliedCode.Trim(), StringComparison.Ordinal);
        if (matches)
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }

        return matches;
    }

    private static string BuildKey(string userId) => $"twofactor:email:{userId}";
}