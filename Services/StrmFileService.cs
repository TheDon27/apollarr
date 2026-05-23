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
    private readonly IRadarrService? _radarrService;
    private readonly IValidationCache _validationCache;
    private readonly int _maxConcurrentValidations;

    public StrmFileService(
        ILogger<StrmFileService> logger,
        HttpClient httpClient,
        IFileSystemService fileSystem,
        ISonarrService sonarrService,
        IOptions<AppSettings> appSettings,
        IRadarrService? radarrService = null,
        IValidationCache? validationCache = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _sonarrService = sonarrService;
        _radarrService = radarrService;
        _validationCache = validationCache ?? new NullValidationCache();
        _apolloSettings = appSettings.Value.Apollo;
        _strmSettings = appSettings.Value.Strm;
        _maxConcurrentValidations = Math.Max(1, _strmSettings.MaxConcurrentValidations);

        if (string.IsNullOrWhiteSpace(_apolloSettings.Username))
            throw new InvalidOperationException("APOLLO_USERNAME not configured");
        if (string.IsNullOrWhiteSpace(_apolloSettings.Password))
            throw new InvalidOperationException("APOLLO_PASSWORD not configured");
    }

    // Runs <paramref name="body"/> over every item with at most _maxConcurrentValidations
    // in flight. Used to parallelize the per-item link validation that previously ran serially.
    private async Task ForEachConcurrentAsync<T>(
        IEnumerable<T> items, Func<T, Task> body, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(_maxConcurrentValidations);
        var tasks = new List<Task>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await body(item);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    // Validates a stream URL, consulting the cache first. A cached positive result skips the
    // HEAD request entirely; live successes are cached. Negative results are never cached so a
    // link that later becomes available is still picked up on the next pass.
    private async Task<bool> ValidateLinkCachedAsync(string streamUrl, CancellationToken cancellationToken)
    {
        if (await _validationCache.IsValidatedAsync(streamUrl, cancellationToken))
        {
            _logger.LogDebug("Stream URL validation served from cache: {StreamUrl}", RedactCredentials(streamUrl));
            return true;
        }

        var isValid = await ValidateStreamUrlAsync(streamUrl, cancellationToken);
        if (isValid)
        {
            await _validationCache.SetValidatedAsync(streamUrl, cancellationToken);
        }

        return isValid;
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
        var strmFilesCreated = 0;

        await ForEachConcurrentAsync(episodes, async episode =>
        {
            try
            {
                Interlocked.Increment(ref episodesProcessed);

                var strmPath = GetEpisodeStrmPath(series, episode);
                var existedBefore = _fileSystem.FileExists(strmPath);

                var hasValidLink = await ProcessEpisodeForMonitoringAsync(series, episode, cancellationToken);

                if (hasValidLink)
                {
                    Interlocked.Increment(ref episodesWithValidLinks);

                    if (!existedBefore && _fileSystem.FileExists(strmPath))
                    {
                        Interlocked.Increment(ref strmFilesCreated);
                    }
                }
                else
                {
                    Interlocked.Increment(ref episodesMissing);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing episode S{Season:D2}E{Episode:D2} for series {SeriesTitle}",
                    episode.SeasonNumber, episode.EpisodeNumber, series.Title);
                Interlocked.Increment(ref episodesMissing);
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Completed processing series {SeriesTitle}. Processed: {Processed}, Valid: {Valid}, Missing: {Missing}, STRM created: {StrmCreated}",
            series.Title, episodesProcessed, episodesWithValidLinks, episodesMissing, strmFilesCreated);

        return new SeriesValidationResult(episodesProcessed, episodesWithValidLinks, episodesMissing, strmFilesCreated);
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

                // Only rescan when a new .strm was actually created. Rescans are expensive on the
                // Sonarr side, and the periodic sweep otherwise re-scanned every series every cycle
                // even when nothing changed. (seriesAdd still rescans unconditionally on first add.)
                if (validationResult.StrmFilesCreated > 0)
                {
                    await _sonarrService.RescanSeriesAsync(seriesDetails.Id, cancellationToken);
                    rescansTriggered++;
                }

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

            var createdForSeries = 0;

            await ForEachConcurrentAsync(seriesGroup, async episode =>
            {
                Interlocked.Increment(ref episodesProcessed);

                var strmPath = GetEpisodeStrmPath(seriesDetails, episode);
                var existedBefore = _fileSystem.FileExists(strmPath);

                var hasValidLink = await ProcessEpisodeForMonitoringAsync(seriesDetails, episode, cancellationToken);

                if (hasValidLink)
                {
                    Interlocked.Increment(ref episodesWithValidLinks);

                    if (!existedBefore && _fileSystem.FileExists(strmPath))
                    {
                        Interlocked.Increment(ref strmFilesCreated);
                        Interlocked.Increment(ref createdForSeries);
                    }
                }
            }, cancellationToken);

            if (createdForSeries > 0)
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

    public async Task<MonitorWantedMoviesResponse> ProcessWantedMissingMoviesAsync(CancellationToken cancellationToken = default)
    {
        if (_radarrService == null)
        {
            throw new InvalidOperationException("Radarr service is not configured");
        }

        _logger.LogInformation("Starting wanted/missing movies monitoring process");

        var wantedMovies = await _radarrService.GetWantedMissingMoviesAsync(cancellationToken);

        var moviesProcessed = 0;
        var moviesWithValidLinks = 0;
        var strmFilesCreated = 0;
        var rescansTriggered = 0;

        await ForEachConcurrentAsync(wantedMovies, async movie =>
        {
            Interlocked.Increment(ref moviesProcessed);

            var strmPath = GetMovieStrmPath(movie);
            var existedBefore = _fileSystem.FileExists(strmPath);

            var hasValidLink = await ProcessMovieForMonitoringAsync(movie, cancellationToken);

            if (hasValidLink)
            {
                Interlocked.Increment(ref moviesWithValidLinks);

                // Only rescan if a new .strm file was actually created
                // This matches Sonarr's behavior: only rescan when a new file needs to be imported
                // Note: Unlike Sonarr which groups episodes by series (one rescan per series),
                // Radarr rescans each movie individually since movies are independent entities.
                // This means if many movies get new .strm files, there will be many rescans.
                if (!existedBefore && _fileSystem.FileExists(strmPath))
                {
                    Interlocked.Increment(ref strmFilesCreated);
                    _logger.LogDebug("New .strm file created for movie {MovieTitle} (ID: {MovieId}), triggering rescan", movie.Title, movie.Id);
                    await _radarrService!.RescanMovieAsync(movie.Id, cancellationToken);
                    Interlocked.Increment(ref rescansTriggered);
                }
                else if (existedBefore)
                {
                    _logger.LogDebug("Skipping rescan for movie {MovieTitle} (ID: {MovieId}) - .strm file already exists", movie.Title, movie.Id);
                }
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Wanted/missing movies monitoring complete. Movies: {MoviesProcessed}, Valid: {ValidLinks}, STRM created: {StrmCreated}, Rescans: {Rescans}",
            moviesProcessed, moviesWithValidLinks, strmFilesCreated, rescansTriggered);

        return new MonitorWantedMoviesResponse(
            "Wanted/missing movies monitoring completed",
            moviesProcessed,
            moviesWithValidLinks,
            strmFilesCreated,
            rescansTriggered);
    }

    public async Task<bool> ValidateMovieLinkAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default)
    {
        // Skip if no IMDb ID
        if (string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            _logger.LogWarning("Movie {MovieTitle} has no IMDb ID, cannot validate link", movie.Title);
            return false;
        }

        // Build stream URL for movie (using "movie" instead of "tvshow", no season/episode)
        var movieStreamUrlTemplate = _strmSettings.StreamUrlTemplate
            .Replace("/tvshow/", "/movie/")
            .Replace("/{season}/", "")
            .Replace("/{episode}", "")
            .Replace("{season}", "")
            .Replace("{episode}", "");

        var streamUrl = movieStreamUrlTemplate
            .Replace("{username}", _apolloSettings.Username)
            .Replace("{password}", _apolloSettings.Password)
            .Replace("{imdbId}", movie.ImdbId);

        // Validate the stream URL
        return await ValidateStreamUrlAsync(streamUrl, cancellationToken);
    }

    private async Task<bool> ProcessMovieForMonitoringAsync(
        RadarrMovieDetails movie, CancellationToken cancellationToken)
    {
        try
        {
            // Skip if no IMDb ID
            if (string.IsNullOrWhiteSpace(movie.ImdbId))
            {
                _logger.LogWarning("Movie {MovieTitle} has no IMDb ID, skipping", movie.Title);
                return false;
            }

            // Build stream URL for movie (using "movie" instead of "tvshow", no season/episode)
            var movieStreamUrlTemplate = _strmSettings.StreamUrlTemplate
                .Replace("/tvshow/", "/movie/")
                .Replace("/{season}/", "")
                .Replace("/{episode}", "")
                .Replace("{season}", "")
                .Replace("{episode}", "");

            var streamUrl = movieStreamUrlTemplate
                .Replace("{username}", _apolloSettings.Username)
                .Replace("{password}", _apolloSettings.Password)
                .Replace("{imdbId}", movie.ImdbId);

            // Validate the stream URL (cache hit skips the HEAD request)
            var isValid = await ValidateLinkCachedAsync(streamUrl, cancellationToken);

            if (!isValid)
            {
                _logger.LogInformation(
                    "Movie {MovieTitle} has no valid link; checking for existing .strm to clean up",
                    movie.Title);

                // Delete .strm file if it exists
                var strmFilePath = GetMovieStrmPath(movie);

                if (_fileSystem.FileExists(strmFilePath))
                {
                    _fileSystem.DeleteFile(strmFilePath);
                    _logger.LogInformation("Deleted .strm file for missing movie: {FilePath}", strmFilePath);
                }
                else
                {
                    _logger.LogDebug("No .strm file found to delete for missing movie at {FilePath}", strmFilePath);
                }

                return false;
            }

            _logger.LogInformation("Movie {MovieTitle} has valid link, processing", movie.Title);

            // Check if .strm file already exists (idempotent)
            var strmFilePathValid = GetMovieStrmPath(movie);

            if (_fileSystem.FileExists(strmFilePathValid))
            {
                _logger.LogDebug(".strm file already exists for {MovieTitle}, skipping", movie.Title);
                return true;
            }

            // Delete existing non-.strm files if they exist
            if (movie.HasFile && movie.MovieFile != null && movie.MovieFile.Id > 0)
            {
                _logger.LogInformation("Movie {MovieTitle} has existing file (ID: {FileId}), deleting to replace with .strm",
                    movie.Title, movie.MovieFile.Id);

                cancellationToken.ThrowIfCancellationRequested();

                await _radarrService!.DeleteMovieFileAsync(movie.MovieFile.Id, cancellationToken);
            }

            // Ensure movie directory exists
            if (!_fileSystem.DirectoryExists(movie.Path))
            {
                _fileSystem.CreateDirectory(movie.Path);
            }

            // Create .strm file
            await _fileSystem.WriteAllTextAsync(strmFilePathValid, streamUrl);
            _logger.LogInformation("Created .strm file: {FilePath}", strmFilePathValid);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing movie {MovieTitle} for monitoring", movie.Title);
            return false;
        }
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

            // Validate the stream URL (cache hit skips the HEAD request)
            var isValid = await ValidateLinkCachedAsync(streamUrl, cancellationToken);

        if (!isValid)
        {
            _logger.LogInformation(
                "Episode S{Season:D2}E{Episode:D2} has no valid link; checking for existing .strm to clean up",
                episode.SeasonNumber, episode.EpisodeNumber);

            // Delete .strm file if it exists (even if the season directory is missing)
            var strmFilePath = GetEpisodeStrmPath(series, episode);

            if (_fileSystem.FileExists(strmFilePath))
            {
                _fileSystem.DeleteFile(strmFilePath);
                _logger.LogInformation("Deleted .strm file for missing episode: {FilePath}", strmFilePath);
            }
            else
            {
                _logger.LogDebug("No .strm file found to delete for missing episode at {FilePath}", strmFilePath);
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

    private string GetSeasonPath(SonarrSeriesDetails series, int seasonNumber) =>
        Path.Combine(series.Path, $"Season {seasonNumber:D2}");

    private string GetEpisodeStrmPath(SonarrSeriesDetails series, Episode episode)
    {
        var episodeTitle = GetEpisodeTitle(episode);
        var fileName = SanitizeFileName($"{series.Title} - S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episodeTitle}.strm");
        return Path.Combine(GetSeasonPath(series, episode.SeasonNumber), fileName);
    }

    public async Task<MovieValidationResult> ProcessMovieAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing movie: {MovieTitle} (ID: {MovieId})", movie.Title, movie.Id);

        try
        {
            // Validate that we have an IMDb ID
            if (string.IsNullOrWhiteSpace(movie.ImdbId))
            {
                _logger.LogWarning("Movie {MovieTitle} (ID: {MovieId}) does not have an IMDb ID, skipping", movie.Title, movie.Id);
                return new MovieValidationResult(false, false);
            }

            // Create streaming URL for movie (no season/episode)
            // For movies, the template should be something like: https://starlite.best/api/stream/{username}/{password}/movie/{imdbId}
            var movieStreamUrlTemplate = _strmSettings.StreamUrlTemplate
                .Replace("/tvshow/", "/movie/")
                .Replace("/{season}/", "")
                .Replace("/{episode}", "")
                .Replace("{season}", "")
                .Replace("{episode}", "");

            var streamUrl = movieStreamUrlTemplate
                .Replace("{username}", _apolloSettings.Username)
                .Replace("{password}", _apolloSettings.Password)
                .Replace("{imdbId}", movie.ImdbId);

            // Validate the stream URL before creating the file if enabled
            if (_strmSettings.ValidateUrls)
            {
                var isValid = await ValidateLinkCachedAsync(streamUrl, cancellationToken);
                if (!isValid)
                {
                    _logger.LogWarning("Stream URL is not valid for {MovieTitle}, skipping .strm file creation", movie.Title);
                    return new MovieValidationResult(false, false);
                }
            }

            // Get expected .strm filename
            var strmFilePath = GetMovieStrmPath(movie);

            // Check if .strm file already exists (idempotent)
            if (_fileSystem.FileExists(strmFilePath))
            {
                _logger.LogDebug(".strm file already exists for {MovieTitle}, skipping", movie.Title);
                return new MovieValidationResult(true, false);
            }

            // Delete existing movie file if it exists
            if (movie.HasFile && movie.MovieFile != null && movie.MovieFile.Id > 0)
            {
                _logger.LogInformation("Movie {MovieTitle} has existing file (ID: {FileId}), will be deleted by Radarr when .strm is created", 
                    movie.Title, movie.MovieFile.Id);
            }

            // Ensure movie directory exists
            if (!_fileSystem.DirectoryExists(movie.Path))
            {
                _fileSystem.CreateDirectory(movie.Path);
            }

            // Create .strm file
            await _fileSystem.WriteAllTextAsync(strmFilePath, streamUrl);
            _logger.LogInformation("Created .strm file: {FilePath}", strmFilePath);

            return new MovieValidationResult(true, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing movie {MovieTitle} (ID: {MovieId})", movie.Title, movie.Id);
            return new MovieValidationResult(false, false);
        }
    }

    private string GetMovieStrmPath(RadarrMovieDetails movie)
    {
        var fileName = SanitizeFileName($"{movie.Title} ({movie.Year}).strm");
        return Path.Combine(movie.Path, fileName);
    }

    public async Task<bool> ValidateStreamUrlAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating stream URL: {StreamUrl}", RedactCredentials(streamUrl));

            using var validationTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_strmSettings.ValidationTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, validationTimeoutCts.Token);
            var request = new HttpRequestMessage(HttpMethod.Head, streamUrl);
            var response = await _httpClient.SendAsync(request, linkedCts.Token);

            // Check if we got redirected to the error page
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? streamUrl;
            if (finalUrl.Contains("error.starlite.best"))
            {
                _logger.LogWarning("Stream URL redirected to error page: {ErrorUrl}", RedactCredentials(finalUrl));
                return false;
            }

            // If we get a successful response, the URL is valid
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Stream URL is valid: {StreamUrl}", RedactCredentials(streamUrl));
                return true;
            }

            _logger.LogWarning("Stream URL returned status code {StatusCode}: {StreamUrl}", response.StatusCode, RedactCredentials(streamUrl));
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Stream URL validation canceled: {StreamUrl}", RedactCredentials(streamUrl));
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Stream URL validation timed out after {Timeout}s: {StreamUrl}",
                _strmSettings.ValidationTimeoutSeconds, RedactCredentials(streamUrl));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating stream URL: {StreamUrl}", RedactCredentials(streamUrl));
            return false;
        }
    }

    // Replaces the configured Apollo username/password with a placeholder so credentials
    // embedded in stream URLs never reach logs.
    private string RedactCredentials(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var redacted = url;
        if (!string.IsNullOrEmpty(_apolloSettings.Username))
            redacted = redacted.Replace(_apolloSettings.Username, "***");
        if (!string.IsNullOrEmpty(_apolloSettings.Password))
            redacted = redacted.Replace(_apolloSettings.Password, "***");

        return redacted;
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
