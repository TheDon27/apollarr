using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class RadarrMovieDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("alternateTitles")]
    public List<RadarrAlternateTitle> AlternateTitles { get; set; } = new();

    [JsonPropertyName("sortTitle")]
    public string? SortTitle { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("inCinemas")]
    public DateTime? InCinemas { get; set; }

    [JsonPropertyName("physicalRelease")]
    public DateTime? PhysicalRelease { get; set; }

    [JsonPropertyName("digitalRelease")]
    public DateTime? DigitalRelease { get; set; }

    [JsonPropertyName("images")]
    public List<RadarrImage> Images { get; set; } = new();

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("qualityProfileId")]
    public int QualityProfileId { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("minimumAvailability")]
    public string MinimumAvailability { get; set; } = string.Empty;

    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("folderName")]
    public string? FolderName { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("cleanTitle")]
    public string? CleanTitle { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("titleSlug")]
    public string? TitleSlug { get; set; }

    [JsonPropertyName("certification")]
    public string? Certification { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("added")]
    public DateTime? Added { get; set; }

    [JsonPropertyName("addOptions")]
    public RadarrAddOptions? AddOptions { get; set; }

    [JsonPropertyName("ratings")]
    public RadarrRatings? Ratings { get; set; }

    [JsonPropertyName("movieFile")]
    public RadarrMovieFile? MovieFile { get; set; }

    [JsonPropertyName("collection")]
    public RadarrCollection? Collection { get; set; }

    [JsonPropertyName("popularity")]
    public double? Popularity { get; set; }

    [JsonPropertyName("statistics")]
    public RadarrMovieStatistics? Statistics { get; set; }
}

public class RadarrAlternateTitle
{
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("movieId")]
    public int MovieId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("sourceId")]
    public int SourceId { get; set; }

    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("language")]
    public RadarrLanguage? Language { get; set; }
}

public class RadarrImage
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }
}

public class RadarrLanguage
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class RadarrAddOptions
{
    [JsonPropertyName("monitor")]
    public string Monitor { get; set; } = string.Empty;

    [JsonPropertyName("searchForMovie")]
    public bool SearchForMovie { get; set; }

    [JsonPropertyName("addMethod")]
    public string? AddMethod { get; set; }
}

public class RadarrRatings
{
    [JsonPropertyName("imdb")]
    public RadarrRating? Imdb { get; set; }

    [JsonPropertyName("tmdb")]
    public RadarrRating? Tmdb { get; set; }

    [JsonPropertyName("rottenTomatoes")]
    public RadarrRating? RottenTomatoes { get; set; }

    [JsonPropertyName("metacritic")]
    public RadarrRating? Metacritic { get; set; }
}

public class RadarrRating
{
    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class RadarrMovieFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("movieId")]
    public int MovieId { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("quality")]
    public RadarrQuality? Quality { get; set; }

    [JsonPropertyName("mediaInfo")]
    public RadarrMediaInfo? MediaInfo { get; set; }
}

public class RadarrQuality
{
    [JsonPropertyName("quality")]
    public RadarrQualityItem? Quality { get; set; }

    [JsonPropertyName("revision")]
    public RadarrRevision? Revision { get; set; }
}

public class RadarrQualityItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class RadarrRevision
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("real")]
    public int Real { get; set; }
}

public class RadarrMediaInfo
{
    [JsonPropertyName("audioChannels")]
    public double AudioChannels { get; set; }

    [JsonPropertyName("audioCodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("audioLanguages")]
    public string? AudioLanguages { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("subtitles")]
    public string? Subtitles { get; set; }

    [JsonPropertyName("videoCodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("videoDynamicRange")]
    public string? VideoDynamicRange { get; set; }

    [JsonPropertyName("videoDynamicRangeType")]
    public string? VideoDynamicRangeType { get; set; }
}

public class RadarrCollection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("images")]
    public List<RadarrImage> Images { get; set; } = new();
}

public class RadarrMovieStatistics
{
    [JsonPropertyName("movieFileCount")]
    public int MovieFileCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("releaseGroups")]
    public List<string> ReleaseGroups { get; set; } = new();
}
