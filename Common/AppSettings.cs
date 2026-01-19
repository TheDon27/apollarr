namespace Apollarr.Common;

public class AppSettings
{
    public const string SectionName = "AppSettings";

    public SonarrSettings Sonarr { get; set; } = new();
    public ApolloSettings Apollo { get; set; } = new();
    public StrmSettings Strm { get; set; } = new();
    public MissingEpisodeSettings MissingEpisode { get; set; } = new();
}

public class SonarrSettings
{
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 5;
    public int[] RetryDelays { get; set; } = new[] { 2000, 3000, 5000, 8000, 10000 };
}

public class ApolloSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class StrmSettings
{
    public string StreamUrlTemplate { get; set; } = "https://starlite.best/api/stream/{username}/{password}/tvshow/{imdbId}/{season}/{episode}";
    public bool ValidateUrls { get; set; } = true;
    public int ValidationTimeoutSeconds { get; set; } = 10;
}

public class MissingEpisodeSettings
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalHours { get; set; } = 1;
}
