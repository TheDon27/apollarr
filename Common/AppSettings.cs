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
}

public class SchedulingSettings
{
    // Hourly series monitoring (seriesAdd-like workflow across your library)
    public bool EnableHourlySeriesMonitoring { get; set; } = true;

    [Range(0, 59)]
    public int HourlySeriesMonitoringMinute { get; set; } = 0; // run at HH:00 by default
    public bool HourlySeriesMonitoringOnlyMonitored { get; set; } = true; // only process monitored series by default

    // Wanted/missing monitoring
    public bool EnableWantedMissingMonitoring { get; set; } = true;

    [Range(1, 1440)]
    public int WantedMissingIntervalMinutes { get; set; } = 15;
}
