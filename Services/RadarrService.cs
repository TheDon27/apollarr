using Apollarr.Models;
using System.Net;

namespace Apollarr.Services;

public class RadarrService : IRadarrService
{
    private readonly RadarrApiClient _apiClient;
    private readonly ILogger<RadarrService> _logger;

    public RadarrService(RadarrApiClient apiClient, ILogger<RadarrService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<RadarrMovieDetails?> GetMovieDetailsAsync(int movieId, CancellationToken cancellationToken = default)
    {
        return await _apiClient.GetAsync<RadarrMovieDetails>(
            $"/api/v3/movie/{movieId}",
            $"GetMovieDetails for movie {movieId}",
            new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable },
            cancellationToken);
    }

    public async Task<RadarrMovieDetails?> TryGetMovieDetailsAsync(int movieId, CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendAsync(
            HttpMethod.Get,
            $"/api/v3/movie/{movieId}",
            payload: null,
            operationName: $"TryGetMovieDetails for movie {movieId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Movie ID {MovieId} not found (skipping)", movieId);
                return null;
            }

            _logger.LogWarning("Failed to fetch movie ID {MovieId}: {StatusCode}", movieId, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<RadarrMovieDetails>(json);
    }

    public async Task<List<RadarrMovieDetails>> GetAllMoviesAsync(CancellationToken cancellationToken = default)
    {
        var movies = await _apiClient.GetAsync<List<RadarrMovieDetails>>(
            "/api/v3/movie",
            "GetAllMovies",
            cancellationToken: cancellationToken);

        return movies ?? new List<RadarrMovieDetails>();
    }

    public async Task<List<RadarrMovieDetails>> GetWantedMissingMoviesAsync(CancellationToken cancellationToken = default)
    {
        var allMissingMovies = new List<RadarrMovieDetails>();
        const int pageSize = 100;
        var page = 1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var endpoint = $"/api/v3/wanted/missing?page={page}&pageSize={pageSize}&sortKey=physicalRelease&sortDirection=descending&monitored=true";
            var response = await _apiClient.GetAsync<WantedMissingMoviesResponse>(
                endpoint,
                $"GetWantedMissingMovies page {page}",
                cancellationToken: cancellationToken);

            if (response == null || response.Records.Count == 0)
                break;

            _logger.LogInformation(
                "Fetched page {Page} of wanted/missing movies: {Count} records, {Total} total",
                page, response.Records.Count, response.TotalRecords);

            // Additional client-side filter to ensure only monitored movies
            var monitoredMovies = response.Records.Where(m => m.Monitored).ToList();

            if (monitoredMovies.Count != response.Records.Count)
            {
                _logger.LogDebug(
                    "Filtered {FilteredCount} unmonitored movies from page {Page}",
                    response.Records.Count - monitoredMovies.Count, page);
            }

            allMissingMovies.AddRange(monitoredMovies);

            // Check if there are more pages
            if (page * pageSize >= response.TotalRecords)
                break;

            page++;
        }

        _logger.LogInformation("Fetched total of {Count} monitored wanted/missing movies", allMissingMovies.Count);
        return allMissingMovies;
    }

    public async Task UpdateMovieAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating movie ID {MovieId}", movie.Id);

        await _apiClient.SendAsync(
            HttpMethod.Put,
            $"/api/v3/movie/{movie.Id}",
            movie,
            $"update movie {movie.Id}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully updated movie ID {MovieId}", movie.Id);
    }

    public async Task<bool> DeleteMovieAsync(
        int movieId,
        bool deleteFiles,
        bool addImportListExclusion = false,
        CancellationToken cancellationToken = default)
    {
        var endpoint =
            $"/api/v3/movie/{movieId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}&addImportListExclusion={addImportListExclusion.ToString().ToLowerInvariant()}";

        var response = await _apiClient.SendAsync(
            HttpMethod.Delete,
            endpoint,
            payload: null,
            operationName: $"delete movie {movieId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Deleted movie ID {MovieId} (deleteFiles={DeleteFiles})", movieId, deleteFiles);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Failed to delete movie ID {MovieId}: {StatusCode} {Body}",
            movieId, response.StatusCode, body);
        return false;
    }

    public async Task<bool> DeleteMovieFileAsync(int movieFileId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting movie file ID {MovieFileId}", movieFileId);

        var response = await _apiClient.SendAsync(
            HttpMethod.Delete,
            $"/api/v3/moviefile/{movieFileId}",
            payload: null,
            operationName: $"delete movie file {movieFileId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully deleted movie file ID {MovieFileId}", movieFileId);
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Failed to delete movie file ID {MovieFileId}: {StatusCode} {Body}",
            movieFileId, response.StatusCode, body);
        return false;
    }

    public async Task RefreshMovieAsync(int movieId, CancellationToken cancellationToken = default)
    {
        var command = new { name = "RefreshMovie", movieId };

        _logger.LogInformation("Triggering movie refresh for movie ID {MovieId}", movieId);

        var response = await _apiClient.SendAsync(
            HttpMethod.Post,
            "/api/v3/command",
            command,
            $"refresh movie {movieId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully triggered movie refresh for movie ID {MovieId}", movieId);
        }
        else
        {
            _logger.LogWarning("Failed to trigger movie refresh for movie ID {MovieId}: {StatusCode}",
                movieId, response.StatusCode);
        }
    }

    public async Task RescanMovieAsync(int movieId, CancellationToken cancellationToken = default)
    {
        // Radarr v3 command to rescan disk for a movie (re-import files, including newly created .strm)
        var command = new { name = "RescanMovie", movieId };

        _logger.LogInformation("Triggering movie rescan for movie ID {MovieId}", movieId);

        var response = await _apiClient.SendAsync(
            HttpMethod.Post,
            "/api/v3/command",
            command,
            $"rescan movie {movieId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully triggered movie rescan for movie ID {MovieId}", movieId);
        }
        else
        {
            _logger.LogWarning("Failed to trigger movie rescan for movie ID {MovieId}: {StatusCode}",
                movieId, response.StatusCode);
        }
    }
}
