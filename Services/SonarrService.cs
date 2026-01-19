using Apollarr.Common;
using Apollarr.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace Apollarr.Services;

public class SonarrService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SonarrService> _logger;
    private readonly SonarrSettings _settings;

    public SonarrService(
        HttpClient httpClient,
        IOptions<AppSettings> appSettings,
        ILogger<SonarrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = appSettings.Value.Sonarr;

        if (string.IsNullOrWhiteSpace(_settings.Url))
            throw new InvalidOperationException("SONARR_URL not configured");
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("SONARR_API_KEY not configured");
    }

    public async Task<SonarrSeriesDetails?> GetSeriesDetailsAsync(int seriesId)
    {
        return await RetryPolicy.ExecuteHttpRequestWithRetryAsync(
            async () => await CreateSonarrRequestAsync($"/api/v3/series/{seriesId}"),
            _logger,
            $"GetSeriesDetails for series {seriesId}",
            _settings.MaxRetries,
            _settings.RetryDelays,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable })
            .ContinueWith(async task =>
            {
                var response = await task;
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SonarrSeriesDetails>(content);
            })
            .Unwrap();
    }

    public async Task<List<Episode>> GetEpisodesForSeriesAsync(int seriesId)
    {
        return await RetryPolicy.ExecuteHttpRequestWithRetryAsync(
            async () => await CreateSonarrRequestAsync($"/api/v3/episode?seriesId={seriesId}"),
            _logger,
            $"GetEpisodes for series {seriesId}",
            maxRetries: 3)
            .ContinueWith(async task =>
            {
                var response = await task;
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<Episode>>(content) ?? new List<Episode>();
            })
            .Unwrap();
    }

    public async Task<List<Episode>> GetWantedMissingEpisodesAsync(CancellationToken cancellationToken = default)
    {
        var allMissingEpisodes = new List<Episode>();
        var pageSize = 100;
        var page = 1;
        var hasMorePages = true;

        while (hasMorePages && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Add monitored=true filter to only get monitored episodes
                var response = await RetryPolicy.ExecuteHttpRequestWithRetryAsync(
                    async () => await CreateSonarrRequestAsync($"/api/v3/wanted/missing?page={page}&pageSize={pageSize}&sortKey=airDateUtc&sortDirection=descending&monitored=true"),
                    _logger,
                    $"GetWantedMissing page {page}",
                    maxRetries: 3)
                    .ContinueWith(async task =>
                    {
                        var httpResponse = await task;
                        var content = await httpResponse.Content.ReadAsStringAsync();
                        return JsonSerializer.Deserialize<WantedMissingResponse>(content);
                    })
                    .Unwrap();

                if (response == null || response.Records.Count == 0)
                {
                    hasMorePages = false;
                    break;
                }

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
                hasMorePages = page * pageSize < response.TotalRecords;
                page++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching wanted/missing episodes at page {Page}", page);
                throw;
            }
        }

        _logger.LogInformation("Fetched total of {Count} monitored wanted/missing episodes", allMissingEpisodes.Count);
        return allMissingEpisodes;
    }

    private async Task<HttpResponseMessage> CreateSonarrRequestAsync(string endpoint)
    {
        var url = $"{_settings.Url.TrimEnd('/')}{endpoint}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", _settings.ApiKey);

        _logger.LogInformation("Sending request to Sonarr: {Url}", url);
        return await _httpClient.SendAsync(request);
    }
}
