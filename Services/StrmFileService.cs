using Apollarr.Common;
using Apollarr.Models;
using Microsoft.Extensions.Options;

namespace Apollarr.Services;

public class StrmFileService
{
    private readonly ILogger<StrmFileService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IFileSystemService _fileSystem;
    private readonly ApolloSettings _apolloSettings;
    private readonly StrmSettings _strmSettings;

    public StrmFileService(
        ILogger<StrmFileService> logger,
        HttpClient httpClient,
        IFileSystemService fileSystem,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _apolloSettings = appSettings.Value.Apollo;
        _strmSettings = appSettings.Value.Strm;

        if (string.IsNullOrWhiteSpace(_apolloSettings.Username))
            throw new InvalidOperationException("APOLLO_USERNAME not configured");
        if (string.IsNullOrWhiteSpace(_apolloSettings.Password))
            throw new InvalidOperationException("APOLLO_PASSWORD not configured");
    }

    public async Task CreateStrmFilesForSeriesAsync(SonarrSeriesDetails series, List<Episode> episodes)
    {
        try
        {
            _logger.LogInformation("Creating .strm files for series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
            _logger.LogInformation("Series path: {SeriesPath}", series.Path);
            _logger.LogInformation("Total episodes to process: {EpisodeCount}", episodes.Count);

            // Group episodes by season
            var episodesBySeason = episodes
                .Where(e => e.Monitored) // Only create .strm files for monitored episodes
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
                    await CreateStrmFileForEpisodeAsync(series, episode, seasonPath);
                }
            }

            _logger.LogInformation("Completed creating .strm files for series: {SeriesTitle}", series.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating .strm files for series: {SeriesTitle} (ID: {SeriesId})", series.Title, series.Id);
            throw;
        }
    }

    private async Task CreateStrmFileForEpisodeAsync(SonarrSeriesDetails series, Episode episode, string seasonPath)
    {
        try
        {
            // Get episode title, use "Episode X" as fallback if title is empty or TBA
            var episodeTitle = GetEpisodeTitle(episode);
            
            // Create filename: SeriesTitle - S01E01 - EpisodeTitle.strm
            var fileName = SanitizeFileName($"{series.Title} - S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episodeTitle}.strm");
            var filePath = Path.Combine(seasonPath, fileName);

            // Validate that we have an IMDb ID
            if (string.IsNullOrWhiteSpace(series.ImdbId))
            {
                _logger.LogWarning("Series {SeriesTitle} (ID: {SeriesId}) does not have an IMDb ID, skipping episode S{Season:D2}E{Episode:D2}", 
                    series.Title, series.Id, episode.SeasonNumber, episode.EpisodeNumber);
                return;
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
                var isValid = await ValidateStreamUrlAsync(streamUrl);
                if (!isValid)
                {
                    _logger.LogWarning("Stream URL is not valid for {SeriesTitle} S{Season:D2}E{Episode:D2}, skipping .strm file creation", 
                        series.Title, episode.SeasonNumber, episode.EpisodeNumber);
                    return;
                }
            }

            // Write the stream URL to the .strm file
            await _fileSystem.WriteAllTextAsync(filePath, streamUrl);

            _logger.LogInformation("Created .strm file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating .strm file for episode S{SeasonNumber:D2}E{EpisodeNumber:D2}", episode.SeasonNumber, episode.EpisodeNumber);
            throw;
        }
    }

    private async Task<bool> ValidateStreamUrlAsync(string streamUrl)
    {
        try
        {
            _logger.LogDebug("Validating stream URL: {StreamUrl}", streamUrl);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_strmSettings.ValidationTimeoutSeconds));
            var request = new HttpRequestMessage(HttpMethod.Head, streamUrl);
            var response = await _httpClient.SendAsync(request, cts.Token);

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

    private static string GetEpisodeTitle(Episode episode)
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

    private static string SanitizeFileName(string fileName)
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
