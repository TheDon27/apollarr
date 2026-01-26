using Apollarr.Models;

namespace Apollarr.Services;

/// <summary>
/// Orchestrates Radarr webhook workflows so controllers stay thin.
/// </summary>
public class RadarrWebhookService : IRadarrWebhookService
{
    private readonly ILogger<RadarrWebhookService> _logger;
    private readonly IRadarrService _radarrService;
    private readonly IStrmFileService _strmFileService;

    public RadarrWebhookService(
        ILogger<RadarrWebhookService> logger,
        IRadarrService radarrService,
        IStrmFileService strmFileService)
    {
        _logger = logger;
        _radarrService = radarrService;
        _strmFileService = strmFileService;
    }

    public async Task<WebhookResponse> HandleWebhookAsync(RadarrWebhook webhook, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Radarr webhook received");
        _logger.LogInformation("Event type: {EventType}", webhook.EventType);

        if (webhook.EventType?.Equals("movieAdd", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await HandleMovieAddEventAsync(webhook, cancellationToken);
        }

        // For other event types, just acknowledge receipt
        return new WebhookResponse("Webhook received", webhook.EventType);
    }

    private async Task<WebhookResponse> HandleMovieAddEventAsync(RadarrWebhook webhook, CancellationToken cancellationToken)
    {
        if (webhook.Movie == null)
        {
            throw new ArgumentException("Movie data missing in webhook");
        }

        _logger.LogInformation("Processing movieAdd event for movie ID: {MovieId}, Title: {MovieTitle}",
            webhook.Movie.Id, webhook.Movie.Title);

        var movieDetails = await _radarrService.GetMovieDetailsAsync(webhook.Movie.Id, cancellationToken);

        if (movieDetails == null)
        {
            throw new InvalidOperationException($"Failed to fetch movie details for movie ID: {webhook.Movie.Id}");
        }

        _logger.LogInformation("Movie fetched: {MovieTitle} (ID: {MovieId})", movieDetails.Title, movieDetails.Id);

        // Ensure movie is monitored
        movieDetails.Monitored = true;
        await _radarrService.UpdateMovieAsync(movieDetails, cancellationToken);

        _logger.LogInformation("Movie {MovieTitle} set to monitored", movieDetails.Title);

        // Validate link and create .strm file
        _logger.LogInformation("Starting movie validation and .strm file creation for {MovieTitle}", movieDetails.Title);

        var validationResult = await _strmFileService.ProcessMovieAsync(movieDetails, cancellationToken);

        _logger.LogInformation(
            "MovieAdd validation complete for {MovieTitle}. Valid link: {HasValidLink}, Strm file created: {StrmCreated}",
            movieDetails.Title, validationResult.HasValidLink, validationResult.StrmFileCreated);

        // Trigger rescan so Radarr imports newly created .strm file
        await _radarrService.RescanMovieAsync(movieDetails.Id, cancellationToken);

        return new WebhookResponse(
            $"MovieAdd event processed - validated {movieDetails.Title}, " +
            $"valid link: {validationResult.HasValidLink}, strm file created: {validationResult.StrmFileCreated}",
            webhook.EventType,
            movieDetails.Id,
            movieDetails.Title);
    }

    public Task<MonitorWantedResponse> MonitorWantedAsync(CancellationToken cancellationToken = default) =>
        _strmFileService.ProcessWantedMissingMoviesAsync(cancellationToken);
}
