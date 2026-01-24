using Apollarr.Models;
using System.IO;

namespace Apollarr.Services;

/// <summary>
/// Orchestrates Sonarr webhook and rebuild workflows so controllers stay thin.
/// </summary>
public class SonarrWebhookService : ISonarrWebhookService
{
    private readonly ILogger<SonarrWebhookService> _logger;
    private readonly ISonarrService _sonarrService;
    private readonly IStrmFileService _strmFileService;
    private readonly IFileSystemService _fileSystem;

    public SonarrWebhookService(
        ILogger<SonarrWebhookService> logger,
        ISonarrService sonarrService,
        IStrmFileService strmFileService,
        IFileSystemService fileSystem)
    {
        _logger = logger;
        _sonarrService = sonarrService;
        _strmFileService = strmFileService;
        _fileSystem = fileSystem;
    }

    public async Task<WebhookResponse> HandleWebhookAsync(SonarrWebhook webhook, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sonarr webhook received");
        _logger.LogInformation("Event type: {EventType}", webhook.EventType);

        if (webhook.EventType?.Equals("seriesAdd", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await HandleSeriesAddEventAsync(webhook, cancellationToken);
        }

        // For other event types, just acknowledge receipt
        return new WebhookResponse("Webhook received", webhook.EventType);
    }

    public Task<MonitorWantedResponse> MonitorWantedAsync(CancellationToken cancellationToken = default) =>
        _strmFileService.ProcessWantedMissingAsync(cancellationToken);

    public async Task<RebuildSeriesResponse> RebuildSeriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting series rebuild workflow");
        var seriesList = await _sonarrService.GetAllSeriesAsync(cancellationToken);

        var seriesProcessed = 0;
        var seriesDeleted = 0;
        var directoriesDeleted = 0;
        var seriesReadded = 0;
        var errors = new List<string>();

        foreach (var summary in seriesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var details = await _sonarrService.GetSeriesDetailsAsync(summary.Id, cancellationToken);
                if (details == null)
                {
                    errors.Add($"Series {summary.Title} ({summary.Id}): details not found");
                    continue;
                }

                seriesProcessed++;

                var originalPath = details.Path;
                var rootFolderPath = !string.IsNullOrWhiteSpace(details.RootFolderPath)
                    ? details.RootFolderPath
                    : Path.GetDirectoryName(originalPath) ?? string.Empty;

                var deletedFromSonarr = await _sonarrService.DeleteSeriesAsync(details.Id, deleteFiles: true, cancellationToken: cancellationToken);
                if (deletedFromSonarr)
                {
                    seriesDeleted++;
                }
                else
                {
                    errors.Add($"Failed to delete series {details.Title} ({details.Id}) from Sonarr");
                }

                if (!string.IsNullOrWhiteSpace(originalPath) && _fileSystem.DirectoryExists(originalPath))
                {
                    _fileSystem.DeleteDirectory(originalPath, recursive: true);
                    directoriesDeleted++;
                }

                details.RootFolderPath = rootFolderPath;
                details.Path = string.Empty; // do not reuse old path
                details.Monitored = true;
                details.AddOptions = new SonarrAddOptions
                {
                    Monitor = "all",
                    SearchForMissingEpisodes = false,
                    SearchForCutoffUnmetEpisodes = false,
                    IgnoreEpisodesWithFiles = true,
                    IgnoreEpisodesWithoutFiles = false
                };

                var added = await _sonarrService.AddSeriesAsync(details, cancellationToken);
                if (added != null)
                {
                    seriesReadded++;
                }
                else
                {
                    errors.Add($"Failed to re-add series {details.Title} ({details.Id}) to Sonarr");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Series {summary.Title} ({summary.Id}): {ex.Message}");
            }
        }

        return new RebuildSeriesResponse(
            "Series rebuild completed",
            seriesProcessed,
            seriesDeleted,
            directoriesDeleted,
            seriesReadded,
            errors);
    }

    private async Task<WebhookResponse> HandleSeriesAddEventAsync(SonarrWebhook webhook, CancellationToken cancellationToken)
    {
        if (webhook.Series == null)
        {
            throw new ArgumentException("Series data missing in webhook");
        }

        _logger.LogInformation("Processing seriesAdd event for series ID: {SeriesId}, Title: {SeriesTitle}",
            webhook.Series.Id, webhook.Series.Title);

        var seriesDetails = await _sonarrService.GetSeriesDetailsAsync(webhook.Series.Id, cancellationToken);

        if (seriesDetails == null)
        {
            throw new InvalidOperationException($"Failed to fetch series details for series ID: {webhook.Series.Id}");
        }

        var episodes = await _sonarrService.GetEpisodesForSeriesAsync(webhook.Series.Id, cancellationToken);

        _logger.LogInformation("Series has {SeasonCount} seasons and {EpisodeCount} episodes",
            seriesDetails.Seasons.Count, episodes.Count);

        // STEP 1: Ensure all regular seasons are monitored (specials off)
        seriesDetails.Monitored = true;
        foreach (var season in seriesDetails.Seasons)
        {
            season.Monitored = season.SeasonNumber > 0;
        }

        await _sonarrService.UpdateSeriesAsync(seriesDetails, cancellationToken);

        var episodesMonitoringUpdatesApplied = 0;
        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var desiredMonitored = episode.SeasonNumber > 0; // monitor all seasons, skip specials
            if (episode.Monitored != desiredMonitored)
            {
                episode.Monitored = desiredMonitored;
                var updated = await _sonarrService.UpdateEpisodeAsync(episode, cancellationToken);
                if (updated)
                {
                    episodesMonitoringUpdatesApplied++;
                }
            }
        }

        _logger.LogInformation(
            "Manual monitoring applied for {SeriesTitle}; all regular seasons monitored, specials off; episode updates: {UpdatesApplied}",
            seriesDetails.Title, episodesMonitoringUpdatesApplied);

        // STEP 2: Validate links and create .strm files for all episodes
        _logger.LogInformation("Starting episode validation and .strm file creation for series {SeriesTitle}", seriesDetails.Title);

        var validationResult = await _strmFileService.ProcessSeriesEpisodesAsync(seriesDetails, episodes, cancellationToken);

        _logger.LogInformation(
            "SeriesAdd validation complete for {SeriesTitle}. Episodes processed: {EpisodesProcessed}, " +
            "Valid links: {ValidLinks}, Missing: {Missing}",
            seriesDetails.Title, validationResult.EpisodesProcessed,
            validationResult.EpisodesWithValidLinks, validationResult.EpisodesMissing);

        // STEP 3: Trigger rescan so Sonarr imports newly created .strm files
        await _sonarrService.RescanSeriesAsync(seriesDetails.Id, cancellationToken);

        return new WebhookResponse(
            $"SeriesAdd event processed - monitored and validated {validationResult.EpisodesProcessed} episodes, " +
            $"{validationResult.EpisodesWithValidLinks} with valid links, {validationResult.EpisodesMissing} missing",
            webhook.EventType,
            webhook.Series.Id,
            seriesDetails.Title,
            seriesDetails.Seasons.Count,
            episodes.Count);
    }
}
