using System.Text;
using Xunit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Services;

namespace HabitTrackerApi.Tests;

public class SecurityHardeningTests
{
    [Fact]
    public void TextSanitizer_StripsHtmlAndNormalizesWhitespace()
    {
        var input = "  <b>Hello</b>   <script>alert(1)</script>   world  ";

        var result = TextSanitizer.SanitizePlainText(input);

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public async Task TwoFactorFallbackCodeService_ValidatesAndExpiresCodes()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var service = new TwoFactorFallbackCodeService(cache);

        var code = await service.GenerateCodeAsync("user-1");
        Assert.False(string.IsNullOrWhiteSpace(code));

        Assert.True(await service.ValidateCodeAsync("user-1", code));
        Assert.False(await service.ValidateCodeAsync("user-1", code));
    }

    private sealed class MemoryDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCacheOptions _options;
        private readonly Dictionary<string, byte[]> _entries = new();

        public MemoryDistributedCache(IOptions<MemoryDistributedCacheOptions> options)
        {
            _options = options.Value;
        }

        public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _entries.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
    }
}
