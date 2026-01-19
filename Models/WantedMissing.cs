using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class WantedMissingResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<Episode> Records { get; set; } = new();
}
