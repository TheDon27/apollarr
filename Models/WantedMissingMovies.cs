using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class WantedMissingMoviesResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<RadarrMovieDetails> Records { get; set; } = new();
}
