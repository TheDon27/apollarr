namespace Apollarr.Models;

public record WebhookResponse(
    string Message,
    string? EventType = null,
    int? SeriesId = null,
    string? SeriesTitle = null,
    int? SeasonCount = null,
    int? EpisodeCount = null);

public record ErrorResponse(
    string Message,
    string? Error = null);
