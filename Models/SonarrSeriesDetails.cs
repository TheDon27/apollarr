using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class SonarrSeriesDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("alternateTitles")]
    public List<AlternateTitle> AlternateTitles { get; set; } = new();

    [JsonPropertyName("sortTitle")]
    public string? SortTitle { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("ended")]
    public bool Ended { get; set; }

    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("nextAiring")]
    public DateTime? NextAiring { get; set; }

    [JsonPropertyName("previousAiring")]
    public DateTime? PreviousAiring { get; set; }

    [JsonPropertyName("network")]
    public string? Network { get; set; }

    [JsonPropertyName("airTime")]
    public string? AirTime { get; set; }

    [JsonPropertyName("images")]
    public List<SonarrImage> Images { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public LanguageInfo? OriginalLanguage { get; set; }

    [JsonPropertyName("remotePoster")]
    public string? RemotePoster { get; set; }

    [JsonPropertyName("seasons")]
    public List<SeasonDetails> Seasons { get; set; } = new();

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("qualityProfileId")]
    public int QualityProfileId { get; set; }

    [JsonPropertyName("seasonFolder")]
    public bool SeasonFolder { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("monitorNewItems")]
    public string? MonitorNewItems { get; set; }

    [JsonPropertyName("useSceneNumbering")]
    public bool UseSceneNumbering { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("tvRageId")]
    public int TvRageId { get; set; }

    [JsonPropertyName("tvMazeId")]
    public int TvMazeId { get; set; }

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("firstAired")]
    public DateTime? FirstAired { get; set; }

    [JsonPropertyName("lastAired")]
    public DateTime? LastAired { get; set; }

    [JsonPropertyName("seriesType")]
    public string SeriesType { get; set; } = string.Empty;

    [JsonPropertyName("cleanTitle")]
    public string? CleanTitle { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("titleSlug")]
    public string? TitleSlug { get; set; }

    [JsonPropertyName("rootFolderPath")]
    public string? RootFolderPath { get; set; }

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("certification")]
    public string? Certification { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("added")]
    public DateTime? Added { get; set; }

    [JsonPropertyName("addOptions")]
    public SonarrAddOptions? AddOptions { get; set; }

    [JsonPropertyName("ratings")]
    public Ratings? Ratings { get; set; }

    [JsonPropertyName("statistics")]
    public SeriesStatistics? Statistics { get; set; }

    [JsonPropertyName("episodesChanged")]
    public bool? EpisodesChanged { get; set; }

    [JsonPropertyName("languageProfileId")]
    public int LanguageProfileId { get; set; }
}

public class AlternateTitle
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("sceneSeasonNumber")]
    public int? SceneSeasonNumber { get; set; }

    [JsonPropertyName("sceneOrigin")]
    public string? SceneOrigin { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class SonarrImage
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

public class LanguageInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class SeasonDetails
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("statistics")]
    public SeasonStatistics? Statistics { get; set; }

    [JsonPropertyName("images")]
    public List<SonarrImage> Images { get; set; } = new();
}

public class SeasonStatistics
{
    [JsonPropertyName("nextAiring")]
    public DateTime? NextAiring { get; set; }

    [JsonPropertyName("previousAiring")]
    public DateTime? PreviousAiring { get; set; }

    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("releaseGroups")]
    public List<string> ReleaseGroups { get; set; } = new();

    [JsonPropertyName("percentOfEpisodes")]
    public double PercentOfEpisodes { get; set; }
}

public class SeriesStatistics
{
    [JsonPropertyName("seasonCount")]
    public int SeasonCount { get; set; }

    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("releaseGroups")]
    public List<string> ReleaseGroups { get; set; } = new();

    [JsonPropertyName("percentOfEpisodes")]
    public double PercentOfEpisodes { get; set; }
}

public class Ratings
{
    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
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
