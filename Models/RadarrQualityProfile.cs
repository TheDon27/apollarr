using System.Text.Json.Serialization;

namespace Apollarr.Models;

public class RadarrQualityProfile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("upgradeAllowed")]
    public bool UpgradeAllowed { get; set; }

    [JsonPropertyName("cutoff")]
    public int Cutoff { get; set; }

    [JsonPropertyName("items")]
    public List<RadarrQualityProfileItem> Items { get; set; } = new();
}

public class RadarrQualityProfileItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("quality")]
    public RadarrQuality? Quality { get; set; }

    [JsonPropertyName("items")]
    public List<RadarrQualityProfileItem> Items { get; set; } = new();

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }
}
