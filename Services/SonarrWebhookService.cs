using Apollarr.Models;
namespace Apollarr.Services;

/// <summary>
/// Orchestrates Sonarr webhook workflows so controllers stay thin.
/// </summary>
public class SonarrWebhookService : ISonarrWebhookService
{
    private readonly ILogger<SonarrWebhookService> _logger;
    private readonly ISonarrService _sonarrService;
    private readonly IStrmFileService _strmFileService;

    public SonarrWebhookService(
        ILogger<SonarrWebhookService> logger,
        ISonarrService sonarrService,
        IStrmFileService strmFileService)
    {
        _logger = logger;
        _sonarrService = sonarrService;
        _strmFileService = strmFileService;
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
