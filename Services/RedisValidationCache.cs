using Apollarr.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Apollarr.Services;

/// <summary>
/// Distributed validation cache backed by <see cref="IDistributedCache"/> (Redis). Survives
/// restarts and is shared across instances, so a redeploy does not force a full re-validation pass.
/// </summary>
public sealed class RedisValidationCache : IValidationCache
{
    private static readonly byte[] Marker = { 1 };

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisValidationCache> _logger;
    private readonly DistributedCacheEntryOptions _entryOptions;

    public RedisValidationCache(IDistributedCache cache, ILogger<RedisValidationCache> logger, IOptions<AppSettings> appSettings)
    {
        _cache = cache;
        _logger = logger;
        _entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, appSettings.Value.ValidationCache.ValidTtlHours))
        };
    }

    public async Task<bool> IsValidatedAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _cache.GetAsync(ValidationCacheKey.For(streamUrl), cancellationToken) != null;
        }
        catch (Exception ex)
        {
            // Treat cache outages as a miss so validation still proceeds against the provider.
            _logger.LogWarning(ex, "Validation cache read failed; falling back to live validation");
            return false;
        }
    }

    public async Task SetValidatedAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.SetAsync(ValidationCacheKey.For(streamUrl), Marker, _entryOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validation cache write failed; result not cached");
        }
    }
}

/// <summary>No-op cache used when validation caching is disabled; every link is validated live.</summary>
public sealed class NullValidationCache : IValidationCache
{
    public Task<bool> IsValidatedAsync(string streamUrl, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task SetValidatedAsync(string streamUrl, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
