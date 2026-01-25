using Apollarr.Common;
using Apollarr.Models;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Apollarr.Services;

public class StrmFileService : IStrmFileService
{
    private readonly ILogger<StrmFileService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IFileSystemService _fileSystem;
    private readonly ApolloSettings _apolloSettings;
    private readonly StrmSettings _strmSettings;
    private readonly ISonarrService _sonarrService;
    private const int EpisodeMonitorConcurrency = 4;

    public StrmFileService(
        ILogger<StrmFileService> logger,
        HttpClient httpClient,
        IFileSystemService fileSystem,
        ISonarrService sonarrService,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _sonarrService = sonarrService;
        _apolloSettings = appSettings.Value.Apollo;
        _strmSettings = appSettings.Value.Strm;

        if (string.IsNullOrWhiteSpace(_apolloSettings.Username))
            throw new InvalidOperationException("APOLLO_USERNAME not configured");
        if (string.IsNullOrWhiteSpace(_apolloSettings.Password))
            throw new InvalidOperationException("APOLLO_PASSWORD not configured");
    }

    public async Task<SeriesValidationResult> ProcessSeriesEpisodesAsync(
        SonarrSeriesDetails series, 
        List<Episode> episodes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing {EpisodeCount} episodes for series: {SeriesTitle} (ID: {SeriesId})", 
            episodes.Count, series.Title, series.Id);

        var episodesProcessed = 0;
        var episodesWithValidLinks = 0;
        var episodesMissing = 0;

        foreach (var episode in episodes)
        {
            try
            {
                episodesProcessed++;
                
                cancellationToken.ThrowIfCancellationRequested();

                var hasValidLink = await ProcessEpisodeForMonitoringAsync(series, episode, cancellationToken);
                
                if (hasValidLink)
                {
                    episodesWithValidLinks++;
                }
                else
                {
                    episodesMissing++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing episode S{Season:D2}E{Episode:D2} for series {SeriesTitle}", 
                    episode.SeasonNumber, episode.EpisodeNumber, series.Title);
                episodesMissing++;
            }
        }

        _logger.LogInformation(
            "Completed processing series {SeriesTitle}. Processed: {Processed}, Valid: {Valid}, Missing: {Missing}",
            series.Title, episodesProcessed, episodesWithValidLinks, episodesMissing);

        return new SeriesValidationResult(episodesProcessed, episodesWithValidLinks, episodesMissing);
    }

    public async Task<MonitorEpisodesResponse> ProcessEpisodesMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting episode monitoring process, onlyMonitored: {OnlyMonitored}", onlyMonitored);

        var allSeries = await _sonarrService.GetAllSeriesAsync(cancellationToken);
        var seriesProcessed = 0;
        var episodesProcessed = 0;
        var episodesWithValidLinks = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = EpisodeMonitorConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(allSeries, options, async (series, ct) =>
        {
            try
            {
                _logger.LogInformation("Processing series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);

                var episodes = await _sonarrService.GetEpisodesForSeriesAsync(series.Id, ct);

                // Filter episodes based on onlyMonitored flag
                var episodesToProcess = FilterEpisodes(episodes, onlyMonitored);

                _logger.LogInformation("Found {EpisodeCount} episodes to process for series {SeriesTitle}",
                    episodesToProcess.Count, series.Title);

                var localEpisodesProcessed = 0;
                var localEpisodesWithValidLinks = 0;

                foreach (var episode in episodesToProcess)
                {
                    ct.ThrowIfCancellationRequested();
                    localEpisodesProcessed++;

                    var hasValidLink = await ProcessEpisodeForMonitoringAsync(series, episode, ct);

                    if (hasValidLink)
                    {
                        localEpisodesWithValidLinks++;
                    }
                }

                Interlocked.Add(ref episodesProcessed, localEpisodesProcessed);
                Interlocked.Add(ref episodesWithValidLinks, localEpisodesWithValidLinks);
                Interlocked.Increment(ref seriesProcessed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing series {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
            }
        });

        _logger.LogInformation(
            "Episode monitoring process complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, " +
            "Valid Links: {ValidLinks}",
            seriesProcessed, episodesProcessed, episodesWithValidLinks);

        return new MonitorEpisodesResponse(
            "Episode monitoring process completed",
            seriesProcessed,
            episodesProcessed,
            episodesWithValidLinks);
    }

    public async Task<MonitorSeriesResponse> ProcessSeriesMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting series monitoring process (seriesAdd-like), onlyMonitored: {OnlyMonitored}", onlyMonitored);

        var allSeries = await _sonarrService.GetAllSeriesAsync(cancellationToken);

        // Filter series if needed
        var seriesToProcess = onlyMonitored
            ? allSeries.Where(s => s.Monitored).ToList()
            : allSeries;

        var seriesProcessed = 0;
        var episodesProcessed = 0;
        var episodesWithValidLinks = 0;
        var episodesMissing = 0;
        var rescansTriggered = 0;
        var episodeMonitoringUpdatesApplied = 0;

        foreach (var series in seriesToProcess)
        {
            try
            {
                _logger.LogInformation("Processing series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);

                // Fetch full series details from Sonarr API (ensures seasons/metadata are up-to-date)
                cancellationToken.ThrowIfCancellationRequested();

                var seriesDetails = await _sonarrService.TryGetSeriesDetailsAsync(series.Id, cancellationToken);
                if (seriesDetails == null)
                {
                    _logger.LogWarning("Failed to fetch series details for series ID {SeriesId}, skipping", series.Id);
                    continue;
                }

                // Fetch all episodes for the series
                var episodes = await _sonarrService.GetEpisodesForSeriesAsync(series.Id, cancellationToken);

                // Ensure only the latest season is monitored (and the series itself is monitored)
                episodeMonitoringUpdatesApplied += await ApplyLatestSeasonMonitoringAsync(seriesDetails, episodes, cancellationToken);

                // Validate links and create .strm files for all episodes
                var validationResult = await ProcessSeriesEpisodesAsync(seriesDetails, episodes, cancellationToken);

                episodesProcessed += validationResult.EpisodesProcessed;
                episodesWithValidLinks += validationResult.EpisodesWithValidLinks;
                episodesMissing += validationResult.EpisodesMissing;

                // Trigger rescan so Sonarr imports newly created .strm files
                await _sonarrService.RescanSeriesAsync(seriesDetails.Id, cancellationToken);
                rescansTriggered++;

                seriesProcessed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing series {SeriesTitle} (ID: {SeriesId})",
                    series.Title, series.Id);
            }
        }

        _logger.LogInformation(
            "Series monitoring process complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, Valid: {ValidLinks}, Missing: {Missing}, Rescans: {Rescans}, EpisodeMonitoringUpdates: {EpisodeUpdates}",
            seriesProcessed, episodesProcessed, episodesWithValidLinks, episodesMissing, rescansTriggered, episodeMonitoringUpdatesApplied);

        return new MonitorSeriesResponse(
            "Series monitoring process completed",
            seriesProcessed,
            episodesProcessed,
            episodesWithValidLinks,
            episodesMissing,
            rescansTriggered,
            episodeMonitoringUpdatesApplied);
    }

    public async Task<MonitorWantedResponse> ProcessWantedMissingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting wanted/missing monitoring process");

        var wantedEpisodes = await _sonarrService.GetWantedMissingEpisodesAsync(cancellationToken);

        var seriesProcessed = 0;
        var episodesProcessed = 0;
        var episodesWithValidLinks = 0;
        var strmFilesCreated = 0;
        var rescansTriggered = 0;

        var episodesBySeries = wantedEpisodes.GroupBy(e => e.SeriesId);

        foreach (var seriesGroup in episodesBySeries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var seriesDetails = await _sonarrService.TryGetSeriesDetailsAsync(seriesGroup.Key, cancellationToken);
            if (seriesDetails == null)
            {
                _logger.LogWarning("Series ID {SeriesId} not found while processing wanted/missing episodes", seriesGroup.Key);
                continue;
            }

            var createdForSeries = false;

            foreach (var episode in seriesGroup)
            {
                episodesProcessed++;

                var strmPath = GetEpisodeStrmPath(seriesDetails, episode);
                var existedBefore = _fileSystem.FileExists(strmPath);

                var hasValidLink = await ProcessEpisodeForMonitoringAsync(seriesDetails, episode, cancellationToken);

                if (hasValidLink)
                {
                    episodesWithValidLinks++;

                    if (!existedBefore && _fileSystem.FileExists(strmPath))
                    {
                        strmFilesCreated++;
                        createdForSeries = true;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (createdForSeries)
            {
                await _sonarrService.RescanSeriesAsync(seriesDetails.Id, cancellationToken);
                rescansTriggered++;
            }

            seriesProcessed++;
        }

        _logger.LogInformation(
            "Wanted/missing monitoring complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, Valid: {ValidLinks}, STRM created: {StrmCreated}, Rescans: {Rescans}",
            seriesProcessed, episodesProcessed, episodesWithValidLinks, strmFilesCreated, rescansTriggered);

        return new MonitorWantedResponse(
            "Wanted/missing monitoring completed",
            seriesProcessed,
            episodesProcessed,
            episodesWithValidLinks,
            strmFilesCreated,
            rescansTriggered);
    }

    private static int GetLatestSeasonNumber(SonarrSeriesDetails seriesDetails)
    {
        return seriesDetails.Seasons
            .Where(s => s.SeasonNumber > 0)
            .Select(s => s.SeasonNumber)
            .DefaultIfEmpty(0)
            .Max();
    }

    private async Task<int> ApplyLatestSeasonMonitoringAsync(SonarrSeriesDetails seriesDetails, List<Episode> episodes, CancellationToken cancellationToken)
    {
        var latestSeasonNumber = GetLatestSeasonNumber(seriesDetails);

        // Always ensure the series itself is monitored
        seriesDetails.Monitored = true;

        // Monitor only the latest season (skip specials, season 0)
        if (latestSeasonNumber > 0)
        {
            foreach (var season in seriesDetails.Seasons)
            {
                if (season.SeasonNumber == 0)
                    continue;

                season.Monitored = season.SeasonNumber == latestSeasonNumber;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _sonarrService.UpdateSeriesAsync(seriesDetails, cancellationToken);

        // Monitor only episodes in the latest season; unmonitor all others (including specials)
        var updatesApplied = 0;
        if (latestSeasonNumber > 0)
        {
            foreach (var episode in episodes)
            {
                var desiredMonitored = episode.SeasonNumber == latestSeasonNumber;
                if (episode.SeasonNumber == 0)
                    desiredMonitored = false;

                if (episode.Monitored != desiredMonitored)
                {
                    episode.Monitored = desiredMonitored;
                    cancellationToken.ThrowIfCancellationRequested();

                    var updated = await _sonarrService.UpdateEpisodeAsync(episode, cancellationToken);
                    if (updated)
                    {
                        updatesApplied++;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Applied latest-season monitoring for {SeriesTitle} (Season {LatestSeason}); episode monitoring updates: {UpdatesApplied}",
            seriesDetails.Title, latestSeasonNumber, updatesApplied);

        return updatesApplied;
    }

    public async Task<MonitorSeasonsResponse> ProcessSeasonsMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting seasons monitoring check, onlyMonitored: {OnlyMonitored}", onlyMonitored);

        var allSeries = await _sonarrService.GetAllSeriesAsync(cancellationToken);
        var seriesProcessed = 0;
        var seasonsProcessed = 0;

        foreach (var series in allSeries)
        {
            try
            {
                _logger.LogInformation("Processing seasons for series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
                
                cancellationToken.ThrowIfCancellationRequested();

                var episodes = await _sonarrService.GetEpisodesForSeriesAsync(series.Id, cancellationToken);

                foreach (var season in series.Seasons)
                {
                    // Skip specials (season 0) if needed
                    if (season.SeasonNumber == 0)
                        continue;

                    var seasonEpisodes = episodes.Where(e => e.SeasonNumber == season.SeasonNumber).ToList();
                    if (seasonEpisodes.Count == 0)
                        continue;

                    var shouldBeMonitored = await DetermineSeasonMonitoringStatusAsync(series, season, seasonEpisodes);
                    
                    if (season.Monitored != shouldBeMonitored)
                    {
                        _logger.LogDebug(
                            "Skipping monitoring change for season {SeasonNumber} of {SeriesTitle} (current={Current}, desired={Desired})", 
                            season.SeasonNumber, series.Title, season.Monitored, shouldBeMonitored);
                    }
                    seasonsProcessed++;
                }

                seriesProcessed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing seasons for series {SeriesTitle} (ID: {SeriesId})", 
                    series.Title, series.Id);
            }
        }

        _logger.LogInformation(
            "Seasons monitoring check complete (no status changes applied). Series: {SeriesProcessed}, Seasons: {SeasonsProcessed}",
            seriesProcessed, seasonsProcessed);

        return new MonitorSeasonsResponse(
            "Seasons monitoring check completed",
            seriesProcessed,
            seasonsProcessed);
    }

    private async Task<bool> DetermineSeasonMonitoringStatusAsync(
        SonarrSeriesDetails series, 
        SeasonDetails season, 
        List<Episode> seasonEpisodes)
    {
        // Season should be monitored UNLESS:
        // 1. All episodes in the season have .strm files
        // 2. All episodes in the season are unmonitored

        if (seasonEpisodes.Count == 0)
            return false;

        // Check if all episodes are unmonitored
        var allUnmonitored = seasonEpisodes.All(e => !e.Monitored);
        if (!allUnmonitored)
        {
            _logger.LogDebug("Season {SeasonNumber} of {SeriesTitle} has monitored episodes, should be monitored", 
                season.SeasonNumber, series.Title);
            return true;
        }

        // Check if all episodes have .strm files
        var allHaveStrmFiles = true;
        foreach (var episode in seasonEpisodes)
        {
            var seasonPath = GetSeasonPath(series, episode.SeasonNumber);
            if (!_fileSystem.DirectoryExists(seasonPath))
            {
                allHaveStrmFiles = false;
                break;
            }

            var strmFilePath = GetEpisodeStrmPath(series, episode);

            if (!_fileSystem.FileExists(strmFilePath))
            {
                allHaveStrmFiles = false;
                break;
            }
        }

        if (!allHaveStrmFiles)
        {
            _logger.LogDebug("Season {SeasonNumber} of {SeriesTitle} is missing .strm files, should be monitored", 
                season.SeasonNumber, series.Title);
            return true;
        }

        // All episodes are unmonitored and have .strm files
        _logger.LogInformation("Season {SeasonNumber} of {SeriesTitle} has all episodes with .strm files and unmonitored, should be unmonitored", 
            season.SeasonNumber, series.Title);
        return false;
    }

    private async Task<bool> DetermineSeriesMonitoringStatusAsync(SonarrSeriesDetails series, List<Episode> episodes)
    {
        // Series should be monitored UNLESS:
        // 1. Series is ended
        // 2. All episodes (excluding specials) have .strm files
        // 3. All episodes (excluding specials) are unmonitored

        // Check if series is ended
        if (!series.Ended && !series.Status?.Equals("ended", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogDebug("Series {SeriesTitle} is continuing, should be monitored", series.Title);
            return true;
        }

        // Series is ended - check episodes
        var regularEpisodes = episodes.Where(e => e.SeasonNumber > 0).ToList();
        
        if (regularEpisodes.Count == 0)
        {
            _logger.LogDebug("Series {SeriesTitle} has no regular episodes, should be monitored", series.Title);
            return true;
        }

        // Check if all episodes are unmonitored
        var allUnmonitored = regularEpisodes.All(e => !e.Monitored);
        if (!allUnmonitored)
        {
            _logger.LogDebug("Series {SeriesTitle} has monitored episodes, should be monitored", series.Title);
            return true;
        }

        // Check if all episodes have .strm files
        var allHaveStrmFiles = true;
        foreach (var episode in regularEpisodes)
        {
            var seasonPath = GetSeasonPath(series, episode.SeasonNumber);
            if (!_fileSystem.DirectoryExists(seasonPath))
            {
                allHaveStrmFiles = false;
                break;
            }

            var strmFilePath = GetEpisodeStrmPath(series, episode);

            if (!_fileSystem.FileExists(strmFilePath))
            {
                allHaveStrmFiles = false;
                break;
            }
        }

        if (!allHaveStrmFiles)
        {
            _logger.LogDebug("Series {SeriesTitle} is missing .strm files, should be monitored", series.Title);
            return true;
        }

        // Series is ended, all episodes are unmonitored, and all have .strm files
        _logger.LogInformation("Series {SeriesTitle} is ended with all episodes unmonitored and having .strm files, should be unmonitored", 
            series.Title);
        return false;
    }

    private async Task<bool> ProcessEpisodeForMonitoringAsync(
        SonarrSeriesDetails series, Episode episode, CancellationToken cancellationToken)
    {
        try
        {
            // Skip if no IMDb ID
            if (string.IsNullOrWhiteSpace(series.ImdbId))
            {
                _logger.LogWarning("Series {SeriesTitle} has no IMDb ID, skipping episode S{Season:D2}E{Episode:D2}", 
                    series.Title, episode.SeasonNumber, episode.EpisodeNumber);
                return false;
            }

            // Build stream URL
            var streamUrl = _strmSettings.StreamUrlTemplate
                .Replace("{username}", _apolloSettings.Username)
                .Replace("{password}", _apolloSettings.Password)
                .Replace("{imdbId}", series.ImdbId)
                .Replace("{season}", episode.SeasonNumber.ToString())
                .Replace("{episode}", episode.EpisodeNumber.ToString());

            // Validate the stream URL
            var isValid = await ValidateStreamUrlAsync(streamUrl, cancellationToken);

            if (!isValid)
            {
                _logger.LogInformation("Episode S{Season:D2}E{Episode:D2} has no valid link; deleting any existing .strm file", 
                    episode.SeasonNumber, episode.EpisodeNumber);

                // Delete .strm file if it exists
                var seasonPath = GetSeasonPath(series, episode.SeasonNumber);
                if (_fileSystem.DirectoryExists(seasonPath))
                {
                    var strmFilePath = GetEpisodeStrmPath(series, episode);

                    if (_fileSystem.FileExists(strmFilePath))
                    {
                        _fileSystem.DeleteFile(strmFilePath);
                        _logger.LogInformation("Deleted .strm file for missing episode: {FilePath}", strmFilePath);
                    }
                }

                return false;
            }

            _logger.LogInformation("Episode S{Season:D2}E{Episode:D2} has valid link, processing", 
                episode.SeasonNumber, episode.EpisodeNumber);

            // Check if .strm file already exists (idempotent)
            var seasonPathValid = GetSeasonPath(series, episode.SeasonNumber);
            var strmFilePathValid = GetEpisodeStrmPath(series, episode);

            if (_fileSystem.FileExists(strmFilePathValid))
            {
                _logger.LogDebug(".strm file already exists for S{Season:D2}E{Episode:D2}, skipping", 
                    episode.SeasonNumber, episode.EpisodeNumber);
                return true;
            }

            // Delete existing non-.strm files if they exist
            await DeleteExistingEpisodeFilesAsync(series, episode, cancellationToken);

            // Create .strm file
            if (!_fileSystem.DirectoryExists(seasonPathValid))
            {
                _fileSystem.CreateDirectory(seasonPathValid);
            }

            await _fileSystem.WriteAllTextAsync(strmFilePathValid, streamUrl);
            _logger.LogInformation("Created .strm file: {FilePath}", strmFilePathValid);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing episode S{Season:D2}E{Episode:D2} for monitoring", 
                episode.SeasonNumber, episode.EpisodeNumber);
            return false;
        }
    }

    private async Task DeleteExistingEpisodeFilesAsync(SonarrSeriesDetails series, Episode episode, CancellationToken cancellationToken)
    {
        try
        {
            // Delete from Sonarr if episode has a file
            if (episode.HasFile && episode.EpisodeFileId > 0)
            {
                _logger.LogInformation("Deleting episode file from Sonarr (ID: {FileId}) for S{Season:D2}E{Episode:D2}", 
                    episode.EpisodeFileId, episode.SeasonNumber, episode.EpisodeNumber);
                
                await _sonarrService.DeleteEpisodeFileAsync(episode.EpisodeFileId, cancellationToken);
                episode.HasFile = false;
                episode.EpisodeFileId = 0;
            }

            // Delete any existing files in the season folder for this episode
            var seasonPath = GetSeasonPath(series, episode.SeasonNumber);
            if (_fileSystem.DirectoryExists(seasonPath))
            {
                var searchPattern = $"{series.Title} - S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}*";
                var files = _fileSystem.GetFiles(seasonPath, searchPattern);
                
                foreach (var file in files)
                {
                    // Keep existing .strm files; only purge other artifacts
                    if (Path.GetExtension(file).Equals(".strm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _logger.LogDebug("Deleting file: {FilePath}", file);
                    _fileSystem.DeleteFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting existing files for episode S{Season:D2}E{Episode:D2}", 
                episode.SeasonNumber, episode.EpisodeNumber);
            throw;
        }
    }

    public async Task CreateStrmFilesForSeriesAsync(SonarrSeriesDetails series, List<Episode> episodes, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing episodes for series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
            _logger.LogInformation("Series path: {SeriesPath}", series.Path);
            _logger.LogInformation("Total episodes to process: {EpisodeCount}", episodes.Count);

            // Group episodes by season
            var episodesBySeason = episodes
                .GroupBy(e => e.SeasonNumber)
                .OrderBy(g => g.Key);

            foreach (var seasonGroup in episodesBySeason)
            {
                var seasonNumber = seasonGroup.Key;
                var seasonPath = Path.Combine(series.Path, $"Season {seasonNumber:D2}");

                // Create season directory if it doesn't exist
                if (!_fileSystem.DirectoryExists(seasonPath))
                {
                    _fileSystem.CreateDirectory(seasonPath);
                    _logger.LogInformation("Created season directory: {SeasonPath}", seasonPath);
                }

                foreach (var episode in seasonGroup.OrderBy(e => e.EpisodeNumber))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessEpisodeAsync(series, episode, seasonPath, cancellationToken);
                }
            }

            _logger.LogInformation("Completed processing episodes for series: {SeriesTitle}", series.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing episodes for series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
            throw;
        }
    }

    private async Task ProcessEpisodeAsync(SonarrSeriesDetails series, Episode episode, string seasonPath, CancellationToken cancellationToken)
    {
        try
        {
            // Get expected .strm filename
            var strmFilePath = GetEpisodeStrmPath(series, episode);

            // Check if .strm file exists
            var strmFileExists = _fileSystem.FileExists(strmFilePath);

            if (strmFileExists)
            {
                _logger.LogDebug(".strm file exists for S{Season:D2}E{Episode:D2}, no monitoring needed", 
                    episode.SeasonNumber, episode.EpisodeNumber);
                
                return;
            }

            // No .strm file exists - check if episode has a file
            if (episode.HasFile && episode.EpisodeFileId > 0)
            {
                _logger.LogInformation("Episode S{Season:D2}E{Episode:D2} has existing file (ID: {FileId}), deleting to replace with .strm", 
                    episode.SeasonNumber, episode.EpisodeNumber, episode.EpisodeFileId);
                
                cancellationToken.ThrowIfCancellationRequested();

                await _sonarrService.DeleteEpisodeFileAsync(episode.EpisodeFileId, cancellationToken);
                episode.HasFile = false;
                episode.EpisodeFileId = 0;
            }

            // Create .strm file
            await CreateStrmFileForEpisodeAsync(series, episode, seasonPath, strmFilePath, cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing episode S{Season:D2}E{Episode:D2}", 
                episode.SeasonNumber, episode.EpisodeNumber);
            
            _logger.LogWarning("Leaving monitoring state unchanged for episode S{Season:D2}E{Episode:D2} due to error", 
                episode.SeasonNumber, episode.EpisodeNumber);
        }
    }

    private async Task CreateStrmFileForEpisodeAsync(SonarrSeriesDetails series, Episode episode, string seasonPath, string filePath, CancellationToken cancellationToken)
    {
        // Validate that we have an IMDb ID
        if (string.IsNullOrWhiteSpace(series.ImdbId))
        {
            _logger.LogWarning("Series {SeriesTitle} (ID: {SeriesId}) does not have an IMDb ID, skipping episode S{Season:D2}E{Episode:D2}", 
                series.Title, series.Id, episode.SeasonNumber, episode.EpisodeNumber);
            throw new InvalidOperationException($"Series {series.Title} does not have an IMDb ID");
        }

        // Create streaming URL using template
        var streamUrl = _strmSettings.StreamUrlTemplate
            .Replace("{username}", _apolloSettings.Username)
            .Replace("{password}", _apolloSettings.Password)
            .Replace("{imdbId}", series.ImdbId)
            .Replace("{season}", episode.SeasonNumber.ToString())
            .Replace("{episode}", episode.EpisodeNumber.ToString());

        // Validate the stream URL before creating the file if enabled
        if (_strmSettings.ValidateUrls)
        {
            var isValid = await ValidateStreamUrlAsync(streamUrl, cancellationToken);
            if (!isValid)
            {
                _logger.LogWarning("Stream URL is not valid for {SeriesTitle} S{Season:D2}E{Episode:D2}, skipping .strm file creation", 
                    series.Title, episode.SeasonNumber, episode.EpisodeNumber);
                throw new InvalidOperationException($"Stream URL is not valid for S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}");
            }
        }

        // Write the stream URL to the .strm file
        await _fileSystem.WriteAllTextAsync(filePath, streamUrl);

        _logger.LogInformation("Created .strm file: {FilePath}", filePath);
    }

    private string GetSeasonPath(SonarrSeriesDetails series, int seasonNumber) =>
        Path.Combine(series.Path, $"Season {seasonNumber:D2}");

    private List<Episode> FilterEpisodes(List<Episode> episodes, bool onlyMonitored)
    {
        var filtered = onlyMonitored
            ? episodes.Where(e => e.Monitored)
            : episodes.AsEnumerable();

        return filtered.ToList();
    }

    private string GetEpisodeStrmPath(SonarrSeriesDetails series, Episode episode)
    {
        var episodeTitle = GetEpisodeTitle(episode);
        var fileName = SanitizeFileName($"{series.Title} - S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episodeTitle}.strm");
        return Path.Combine(GetSeasonPath(series, episode.SeasonNumber), fileName);
    }

    public async Task<bool> ValidateStreamUrlAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating stream URL: {StreamUrl}", streamUrl);

            using var validationTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_strmSettings.ValidationTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, validationTimeoutCts.Token);
            var request = new HttpRequestMessage(HttpMethod.Head, streamUrl);
            var response = await _httpClient.SendAsync(request, linkedCts.Token);

            // Check if we got redirected to the error page
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? streamUrl;
            if (finalUrl.Contains("error.starlite.best"))
            {
                _logger.LogWarning("Stream URL redirected to error page: {ErrorUrl}", finalUrl);
                return false;
            }

            // If we get a successful response, the URL is valid
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Stream URL is valid: {StreamUrl}", streamUrl);
                return true;
            }

            _logger.LogWarning("Stream URL returned status code {StatusCode}: {StreamUrl}", response.StatusCode, streamUrl);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Stream URL validation canceled: {StreamUrl}", streamUrl);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Stream URL validation timed out after {Timeout}s: {StreamUrl}", 
                _strmSettings.ValidationTimeoutSeconds, streamUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating stream URL: {StreamUrl}", streamUrl);
            return false;
        }
    }

    public static string GetEpisodeTitle(Episode episode)
    {
        var placeholderTitles = new[] { "TBA", "TBD" };
        
        // If title is empty, null, or common placeholder values, use a generic name
        if (string.IsNullOrWhiteSpace(episode.Title) || 
            placeholderTitles.Any(p => episode.Title.Equals(p, StringComparison.OrdinalIgnoreCase)) ||
            episode.Title.StartsWith("Episode ", StringComparison.OrdinalIgnoreCase))
        {
            return $"Episode {episode.EpisodeNumber}";
        }

        return episode.Title;
    }

    public static string SanitizeFileName(string fileName)
    {
        // Remove invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        
        // Replace problematic characters
        var replacements = new Dictionary<string, string>
        {
            { ":", " -" },
            { "\"", "'" },
            { "?", "" },
            { "*", "" },
            { "<", "" },
            { ">", "" },
            { "|", "-" }
        };

        foreach (var (oldChar, newChar) in replacements)
        {
            sanitized = sanitized.Replace(oldChar, newChar);
        }
        
        // Remove multiple consecutive spaces and trim
        while (sanitized.Contains("  "))
        {
            sanitized = sanitized.Replace("  ", " ");
        }
        
        // Trim and remove trailing periods (Windows doesn't like them)
        return sanitized.Trim().TrimEnd('.');
    }
}
