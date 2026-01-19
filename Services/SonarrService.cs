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

    private async Task<HttpResponseMessage> CreateSonarrRequestAsync(string endpoint)
    {
        var url = $"{_settings.Url.TrimEnd('/')}{endpoint}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", _settings.ApiKey);

        _logger.LogInformation("Sending request to Sonarr: {Url}", url);
        return await _httpClient.SendAsync(request);
    }
}
