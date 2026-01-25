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

public record MonitorEpisodesResponse(
    string Message,
    int SeriesProcessed,
    int EpisodesProcessed,
    int EpisodesWithValidLinks);

public record MonitorSeriesResponse(
    string Message,
    int SeriesProcessed,
    int EpisodesProcessed,
    int EpisodesWithValidLinks,
    int EpisodesMissing,
    int RescansTriggered,
    int EpisodeMonitoringUpdatesApplied);

public record MonitorWantedResponse(
    string Message,
    int SeriesProcessed,
    int EpisodesProcessed,
    int EpisodesWithValidLinks,
    int StrmFilesCreated,
    int RescansTriggered);

public record MonitorSeasonsResponse(
    string Message,
    int SeriesProcessed,
    int SeasonsProcessed);

public record SeriesValidationResult(
    int EpisodesProcessed,
    int EpisodesWithValidLinks,
    int EpisodesMissing);
