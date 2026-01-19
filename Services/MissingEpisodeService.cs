using Apollarr.Models;

namespace Apollarr.Services;

public class MissingEpisodeService
{
    private readonly ILogger<MissingEpisodeService> _logger;
    private readonly SonarrService _sonarrService;
    private readonly StrmFileService _strmFileService;

    public MissingEpisodeService(
        ILogger<MissingEpisodeService> logger,
        SonarrService sonarrService,
        StrmFileService strmFileService)
    {
        _logger = logger;
        _sonarrService = sonarrService;
        _strmFileService = strmFileService;
    }

    public async Task ProcessMissingEpisodesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting missing episode check");

            // Fetch all missing episodes
            var missingEpisodes = await _sonarrService.GetWantedMissingEpisodesAsync(cancellationToken);
            
            if (missingEpisodes.Count == 0)
            {
                _logger.LogInformation("No missing episodes found");
                return;
            }

            _logger.LogInformation("Found {Count} missing episodes", missingEpisodes.Count);

            // Group episodes by series
            var episodesBySeries = missingEpisodes
                .GroupBy(e => e.SeriesId)
                .ToList();

            _logger.LogInformation("Missing episodes span {SeriesCount} series", episodesBySeries.Count);

            var processedCount = 0;
            var errorCount = 0;

            foreach (var seriesGroup in episodesBySeries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Missing episode processing cancelled");
                    break;
                }

                try
                {
                    await ProcessSeriesMissingEpisodesAsync(seriesGroup.Key, seriesGroup.ToList(), cancellationToken);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing missing episodes for series ID {SeriesId}", seriesGroup.Key);
                    errorCount++;
                }
            }

            _logger.LogInformation(
                "Completed missing episode check. Processed: {ProcessedCount}, Errors: {ErrorCount}",
                processedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during missing episode check");
            throw;
        }
    }

    private async Task ProcessSeriesMissingEpisodesAsync(
        int seriesId,
        List<Episode> missingEpisodes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing {Count} missing episodes for series ID {SeriesId}",
            missingEpisodes.Count, seriesId);

        // Fetch series details
        var seriesDetails = await _sonarrService.GetSeriesDetailsAsync(seriesId);
        
        if (seriesDetails == null)
        {
            _logger.LogWarning("Could not fetch series details for series ID {SeriesId}, skipping", seriesId);
            return;
        }

        _logger.LogInformation("Creating .strm files for missing episodes of series: {SeriesTitle}",
            seriesDetails.Title);

        // Create .strm files for the missing episodes
        await _strmFileService.CreateStrmFilesForSeriesAsync(seriesDetails, missingEpisodes);
    }
}
