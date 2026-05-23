using System.ComponentModel.DataAnnotations;

namespace Apollarr.Common;

public class AppSettings
{
    public const string SectionName = "AppSettings";

    public SonarrSettings Sonarr { get; set; } = new();
    public RadarrSettings Radarr { get; set; } = new();
    public ApolloSettings Apollo { get; set; } = new();
    public StrmSettings Strm { get; set; } = new();
    public SchedulingSettings Scheduling { get; set; } = new();
    public ValidationCacheSettings ValidationCache { get; set; } = new();
}

public class SonarrSettings
{
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(0, 20)]
    public int MaxRetries { get; set; } = 5;
    public int[] RetryDelays { get; set; } = new[] { 2000, 3000, 5000, 8000, 10000 };
}

public class RadarrSettings
{
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(0, 20)]
    public int MaxRetries { get; set; } = 5;
    public int[] RetryDelays { get; set; } = new[] { 2000, 3000, 5000, 8000, 10000 };
}

public class ApolloSettings
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class StrmSettings
{
    [Required]
    public string StreamUrlTemplate { get; set; } = "https://starlite.best/api/stream/{username}/{password}/tvshow/{imdbId}/{season}/{episode}";
    public bool ValidateUrls { get; set; } = true;

    [Range(1, 120)]
    public int ValidationTimeoutSeconds { get; set; } = 10;

    // Upper bound on concurrent stream-link HEAD validations. Monitoring passes used to
    // validate one link at a time; bounding the parallelism keeps a full-library pass from
    // running longer than the schedule interval without overwhelming the provider.
    [Range(1, 64)]
    public int MaxConcurrentValidations { get; set; } = 8;
}

public class SchedulingSettings
{
    // Hourly series monitoring (seriesAdd-like workflow across your library)
    public bool EnableHourlySeriesMonitoring { get; set; } = true;

    [Range(0, 59)]
    public int HourlySeriesMonitoringMinute { get; set; } = 0; // run at HH:00 by default
    public bool HourlySeriesMonitoringOnlyMonitored { get; set; } = true; // only process monitored series by default

    // Minimum hours between full-library series-monitoring sweeps. The sweep re-validates
    // every episode of every series, so it should be infrequent; default is once per day.
    // Set to 1 to restore the previous hourly behavior.
    [Range(1, 168)]
    public int SeriesMonitoringIntervalHours { get; set; } = 24;

    // Wanted/missing monitoring
    public bool EnableWantedMissingMonitoring { get; set; } = true;

    [Range(1, 1440)]
    public int WantedMissingIntervalMinutes { get; set; } = 15;
}

public class ValidationCacheSettings
{
    // Caches positive stream-link validations so unchanged links are not re-HEADed every
    // pass. Negative results are never cached, so a previously-missing link is still picked
    // up as soon as it becomes available.
    public bool Enabled { get; set; } = true;

    // "Memory" (in-process IMemoryCache) or "Redis" (shared IDistributedCache).
    public string Provider { get; set; } = "Memory";

    // Required when Provider is "Redis"; e.g. "localhost:6379".
    public string? RedisConnectionString { get; set; }

    [Range(1, 8760)]
    public int ValidTtlHours { get; set; } = 12;
}
