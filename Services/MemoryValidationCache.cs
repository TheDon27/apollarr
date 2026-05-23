using Apollarr.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Apollarr.Services;

/// <summary>In-process validation cache. Lost on restart; suitable for single-instance deployments.</summary>
public sealed class MemoryValidationCache : IValidationCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public MemoryValidationCache(IMemoryCache cache, IOptions<AppSettings> appSettings)
    {
        _cache = cache;
        _ttl = TimeSpan.FromHours(Math.Max(1, appSettings.Value.ValidationCache.ValidTtlHours));
    }

    public Task<bool> IsValidatedAsync(string streamUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cache.TryGetValue(ValidationCacheKey.For(streamUrl), out _));

    public Task SetValidatedAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        _cache.Set(ValidationCacheKey.For(streamUrl), true, _ttl);
        return Task.CompletedTask;
    }
}
