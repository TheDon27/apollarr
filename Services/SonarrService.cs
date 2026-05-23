using Apollarr.Models;
using System.Net;
using System.Text.Json;

namespace Apollarr.Services;

public class SonarrService : ISonarrService
{
    private readonly SonarrApiClient _apiClient;
    private readonly ILogger<SonarrService> _logger;

    public SonarrService(SonarrApiClient apiClient, ILogger<SonarrService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<SonarrSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        return await _apiClient.GetAsync<SonarrSeriesDetails>(
            $"/api/v3/series/{seriesId}",
            $"GetSeriesDetails for series {seriesId}",
            new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable },
            cancellationToken);
    }

    public async Task<SonarrSeriesDetails?> TryGetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendAsync(
            HttpMethod.Get,
            $"/api/v3/series/{seriesId}",
            payload: null,
            operationName: $"TryGetSeriesDetails for series {seriesId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Series ID {SeriesId} not found (skipping)", seriesId);
                return null;
            }

            _logger.LogWarning("Failed to fetch series ID {SeriesId}: {StatusCode}", seriesId, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<SonarrSeriesDetails>(json);
    }

    public async Task<List<SonarrSeriesDetails>> GetAllSeriesAsync(CancellationToken cancellationToken = default)
    {
        var series = await _apiClient.GetAsync<List<SonarrSeriesDetails>>(
            "/api/v3/series",
            "GetAllSeries",
            cancellationToken: cancellationToken);

        return series ?? new List<SonarrSeriesDetails>();
    }

    public async Task<List<Episode>> GetEpisodesForSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        var episodes = await _apiClient.GetAsync<List<Episode>>(
            $"/api/v3/episode?seriesId={seriesId}",
            $"GetEpisodes for series {seriesId}",
            cancellationToken: cancellationToken);

        return episodes ?? new List<Episode>();
    }

    public async Task<List<Episode>> GetWantedMissingEpisodesAsync(CancellationToken cancellationToken = default)
    {
        var allMissingEpisodes = new List<Episode>();
        const int pageSize = 100;
        var page = 1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var endpoint = $"/api/v3/wanted/missing?page={page}&pageSize={pageSize}&sortKey=airDateUtc&sortDirection=descending&monitored=true";
            var response = await _apiClient.GetAsync<WantedMissingResponse>(
                endpoint,
                $"GetWantedMissing page {page}",
                cancellationToken: cancellationToken);

            if (response == null || response.Records.Count == 0)
                break;

            _logger.LogInformation(
                "Fetched page {Page} of wanted/missing episodes: {Count} records, {Total} total",
                page, response.Records.Count, response.TotalRecords);

            // Additional client-side filter to ensure only monitored episodes
            var monitoredEpisodes = response.Records.Where(e => e.Monitored).ToList();

            if (monitoredEpisodes.Count != response.Records.Count)
            {
                _logger.LogDebug(
                    "Filtered {FilteredCount} unmonitored episodes from page {Page}",
                    response.Records.Count - monitoredEpisodes.Count, page);
            }

            allMissingEpisodes.AddRange(monitoredEpisodes);

            // Check if there are more pages
            if (page * pageSize >= response.TotalRecords)
                break;

            page++;
        }

        _logger.LogInformation("Fetched total of {Count} monitored wanted/missing episodes", allMissingEpisodes.Count);
        return allMissingEpisodes;
    }

    public async Task UpdateSeriesAsync(SonarrSeriesDetails series, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating series ID {SeriesId}", series.Id);

        await _apiClient.SendAsync(
            HttpMethod.Put,
            $"/api/v3/series/{series.Id}",
            series,
            $"update series {series.Id}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully updated series ID {SeriesId}", series.Id);
    }

    public async Task<bool> UpdateEpisodeAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating episode ID {EpisodeId} (S{Season:D2}E{Episode:D2}) monitored={Monitored}",
            episode.Id, episode.SeasonNumber, episode.EpisodeNumber, episode.Monitored);

        var response = await _apiClient.SendAsync(
            HttpMethod.Put,
            $"/api/v3/episode/{episode.Id}",
            episode,
            $"update episode {episode.Id}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Episode ID {EpisodeId} not found when updating; skipping", episode.Id);
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to update episode {episode.Id}: {response.StatusCode} {body}");
        }

        return true;
    }

    public async Task DeleteEpisodeFileAsync(int episodeFileId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting episode file ID {EpisodeFileId}", episodeFileId);

        await _apiClient.SendAsync(
            HttpMethod.Delete,
            $"/api/v3/episodefile/{episodeFileId}",
            payload: null,
            operationName: $"delete episode file {episodeFileId}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully deleted episode file ID {EpisodeFileId}", episodeFileId);
    }

    public async Task RescanSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        // Sonarr v3 command to rescan disk for a series (re-import files, including newly created .strm)
        var command = new { name = "RescanSeries", seriesId };

        _logger.LogInformation("Triggering series rescan for series ID {SeriesId}", seriesId);

        var response = await _apiClient.SendAsync(
            HttpMethod.Post,
            "/api/v3/command",
            command,
            $"rescan series {seriesId}",
            throwOnError: false,
            cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully triggered series rescan for series ID {SeriesId}", seriesId);
        }
        else
        {
            _logger.LogWarning("Failed to trigger series rescan for series ID {SeriesId}: {StatusCode}",
                seriesId, response.StatusCode);
        }
    }


}
