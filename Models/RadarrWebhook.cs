using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class RadarrWebhook
{
    [Required]
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("movie")]
    public RadarrMovie? Movie { get; set; }
}

public class RadarrMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }
}
