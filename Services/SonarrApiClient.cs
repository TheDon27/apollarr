using Apollarr.Common;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apollarr.Services;

/// <summary>
/// Centralizes Sonarr HTTP access with shared headers, JSON options, and retry handling.
/// </summary>
public class SonarrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SonarrApiClient> _logger;
    private readonly SonarrSettings _settings;

    public SonarrApiClient(HttpClient httpClient, IOptions<AppSettings> appSettings, ILogger<SonarrApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = appSettings.Value.Sonarr;

        if (string.IsNullOrWhiteSpace(_settings.Url))
            throw new InvalidOperationException("SONARR_URL not configured");
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("SONARR_API_KEY not configured");

        // Configure base address and headers once for this typed client.
        _httpClient.BaseAddress ??= new Uri(_settings.Url.TrimEnd('/') + "/");
        if (!_httpClient.DefaultRequestHeaders.Contains("X-Api-Key"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey);
        }
    }

    public async Task<T?> GetAsync<T>(
        string endpoint,
        string operationName,
        HttpStatusCode[]? nonRetryableStatusCodes = null,
        CancellationToken cancellationToken = default)
    {
        var response = await RetryPolicy.ExecuteHttpRequestWithRetryAsync(
            () => SendRequestAsync(HttpMethod.Get, endpoint, null, operationName, cancellationToken),
            _logger,
            operationName,
            _settings.MaxRetries,
            _settings.RetryDelays,
            nonRetryableStatusCodes ?? Array.Empty<HttpStatusCode>(),
            cancellationToken);

        return await DeserializeAsync<T>(response, cancellationToken);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        object? payload,
        string operationName,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        var response = await RetryPolicy.ExecuteHttpRequestWithRetryAsync(
            () => SendRequestAsync(method, endpoint, payload, operationName, cancellationToken),
            _logger,
            operationName,
            _settings.MaxRetries,
            _settings.RetryDelays,
            Array.Empty<HttpStatusCode>(),
            cancellationToken);

        if (!response.IsSuccessStatusCode && throwOnError)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to {Operation} ({StatusCode}): {Body}", operationName, response.StatusCode, body);
            throw new HttpRequestException($"Failed to {operationName}: {response.StatusCode}");
        }

        return response;
    }

    public async Task<T?> SendAndReadAsync<T>(
        HttpMethod method,
        string endpoint,
        object? payload,
        string operationName,
        bool throwOnError = true,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(method, endpoint, payload, operationName, throwOnError, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string endpoint,
        object? payload,
        string operationName,
        CancellationToken cancellationToken)
    {
        var relativeEndpoint = endpoint.TrimStart('/');
        var request = new HttpRequestMessage(method, relativeEndpoint);

        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("Sending Sonarr request: {Method} {Endpoint}", method, relativeEndpoint);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(contentStream, JsonOptions, cancellationToken);
    }
}
