using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class SonarrSeriesDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("ended")]
    public bool Ended { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("qualityProfileId")]
    public int QualityProfileId { get; set; }

    [JsonPropertyName("languageProfileId")]
    public int LanguageProfileId { get; set; }

    [JsonPropertyName("seriesType")]
    public string SeriesType { get; set; } = string.Empty;

    [JsonPropertyName("seasonFolder")]
    public bool SeasonFolder { get; set; }

    [JsonPropertyName("rootFolderPath")]
    public string? RootFolderPath { get; set; }

    [JsonPropertyName("titleSlug")]
    public string? TitleSlug { get; set; }

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<SeasonDetails> Seasons { get; set; } = new();

    [JsonPropertyName("statistics")]
    public SeriesStatistics? Statistics { get; set; }

    [JsonPropertyName("addOptions")]
    public SonarrAddOptions? AddOptions { get; set; }
}

public class SeasonDetails
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("statistics")]
    public SeasonStatistics? Statistics { get; set; }
}

public class SeasonStatistics
{
    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("percentOfEpisodes")]
    public decimal PercentOfEpisodes { get; set; }
}

public class SeriesStatistics
{
    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("percentOfEpisodes")]
    public decimal PercentOfEpisodes { get; set; }
}

public class Episode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("episodeFileId")]
    public int EpisodeFileId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("airDate")]
    public string? AirDate { get; set; }

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();
}

public class SonarrTag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

public class SonarrAddOptions
{
    [JsonPropertyName("monitor")]
    public string Monitor { get; set; } = string.Empty;

    [JsonPropertyName("ignoreEpisodesWithFiles")]
    public bool? IgnoreEpisodesWithFiles { get; set; }

    [JsonPropertyName("ignoreEpisodesWithoutFiles")]
    public bool? IgnoreEpisodesWithoutFiles { get; set; }

    [JsonPropertyName("searchForMissingEpisodes")]
    public bool? SearchForMissingEpisodes { get; set; }

    [JsonPropertyName("searchForCutoffUnmetEpisodes")]
    public bool? SearchForCutoffUnmetEpisodes { get; set; }

    [JsonPropertyName("monitorSpecials")]
    public bool? MonitorSpecials { get; set; }

    [JsonPropertyName("unmonitorSpecials")]
    public bool? UnmonitorSpecials { get; set; }
}
