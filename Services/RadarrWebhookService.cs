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

        // Step 1: Set movie to monitored and quality profile to SDTV
        movieDetails.Monitored = true;
        
        // Find SDTV quality profile
        var qualityProfiles = await _radarrService.GetQualityProfilesAsync(cancellationToken);
        var sdtvProfile = qualityProfiles.FirstOrDefault(p => 
            p.Name.Equals("SDTV", StringComparison.OrdinalIgnoreCase));
        
        if (sdtvProfile != null)
        {
            movieDetails.QualityProfileId = sdtvProfile.Id;
            _logger.LogInformation("Setting quality profile to SDTV (ID: {ProfileId}) for movie {MovieTitle}", 
                sdtvProfile.Id, movieDetails.Title);
        }
        else
        {
            _logger.LogWarning("SDTV quality profile not found. Available profiles: {Profiles}", 
                string.Join(", ", qualityProfiles.Select(p => p.Name)));
        }

        await _radarrService.UpdateMovieAsync(movieDetails, cancellationToken);
        _logger.LogInformation("Movie {MovieTitle} set to monitored with quality profile {ProfileId}", 
            movieDetails.Title, movieDetails.QualityProfileId);

        // Step 2: Validate link
        _logger.LogInformation("Validating stream link for {MovieTitle}", movieDetails.Title);
        var hasValidLink = await _strmFileService.ValidateMovieLinkAsync(movieDetails, cancellationToken);

        // Initialize validation result - will be updated if valid link exists
        MovieValidationResult validationResult = new MovieValidationResult(hasValidLink, false);

        // Step 3: If valid link exists, delete all existing files, create .strm, and set unmonitored
        if (hasValidLink)
        {
            _logger.LogInformation("Valid link found for {MovieTitle}, proceeding with file cleanup and .strm creation", 
                movieDetails.Title);

            // Delete all existing movie files first
            var movieFiles = await _radarrService.GetMovieFilesAsync(movieDetails.Id, cancellationToken);
            foreach (var movieFile in movieFiles)
            {
                _logger.LogInformation("Deleting existing movie file ID {FileId} for movie {MovieTitle}", 
                    movieFile.Id, movieDetails.Title);
                await _radarrService.DeleteMovieFileAsync(movieFile.Id, cancellationToken);
            }

            // Refresh movie details to get updated state after file deletion
            movieDetails = await _radarrService.GetMovieDetailsAsync(movieDetails.Id, cancellationToken);
            if (movieDetails == null)
            {
                throw new InvalidOperationException($"Failed to refresh movie details for movie ID: {webhook.Movie.Id}");
            }

            // Create .strm file
            validationResult = await _strmFileService.ProcessMovieAsync(movieDetails, cancellationToken);
            
            if (!validationResult.StrmFileCreated)
            {
                _logger.LogError("Failed to create .strm file for {MovieTitle}", movieDetails.Title);
            }
            else
            {
                _logger.LogInformation(".strm file created for {MovieTitle}", movieDetails.Title);
            }

            // Set movie to unmonitored
            movieDetails.Monitored = false;
            await _radarrService.UpdateMovieAsync(movieDetails, cancellationToken);
            _logger.LogInformation("Movie {MovieTitle} set to unmonitored after creating .strm file", movieDetails.Title);

            // Trigger rescan so Radarr imports newly created .strm file
            await _radarrService.RescanMovieAsync(movieDetails.Id, cancellationToken);
        }
        else
        {
            _logger.LogInformation("No valid link found for {MovieTitle}, movie remains monitored", movieDetails.Title);
        }

        return new WebhookResponse(
            $"MovieAdd event processed - validated {movieDetails.Title}, " +
            $"valid link: {validationResult.HasValidLink}, strm file created: {validationResult.StrmFileCreated}, " +
            $"monitored: {movieDetails.Monitored}",
            webhook.EventType,
            movieDetails.Id,
            movieDetails.Title);
    }

    public Task<MonitorWantedResponse> MonitorWantedAsync(CancellationToken cancellationToken = default) =>
        _strmFileService.ProcessWantedMissingMoviesAsync(cancellationToken);
}
