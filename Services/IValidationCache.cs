using System.Security.Cryptography;
using System.Text;

namespace Apollarr.Services;

/// <summary>
/// Caches positive stream-link validations so unchanged links are not re-validated (HEAD
/// request) on every monitoring pass. Only successful validations are stored, and they expire
/// after a configurable TTL, so a previously-missing link is still detected once it becomes
/// available and a link that has gone dead is eventually re-checked and cleaned up.
/// </summary>
public interface IValidationCache
{
    /// <summary>Returns true if the stream URL was recently validated as available.</summary>
    Task<bool> IsValidatedAsync(string streamUrl, CancellationToken cancellationToken = default);

    /// <summary>Records that the stream URL validated successfully.</summary>
    Task SetValidatedAsync(string streamUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hashes stream URLs into cache keys. Stream URLs embed Apollo credentials, so we never use
/// the raw URL as a key; a SHA-256 hash keeps credentials out of the cache and bounds key length.
/// </summary>
internal static class ValidationCacheKey
{
    public static string For(string streamUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(streamUrl));
        return "apollarr:strm-valid:" + Convert.ToHexString(bytes);
    }
}
