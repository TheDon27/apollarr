using Microsoft.AspNetCore.Mvc;
using Apollarr.Models;
using Apollarr.Services;
using System.Text.Json;

namespace Apollarr.Controllers;

[ApiController]
[Route("[controller]")]
public class SonarrController : ControllerBase
{
    private readonly ILogger<SonarrController> _logger;
    private readonly SonarrService _sonarrService;
    private readonly StrmFileService _strmFileService;

    public SonarrController(
        ILogger<SonarrController> logger,
        SonarrService sonarrService,
        StrmFileService strmFileService)
    {
        _logger = logger;
        _sonarrService = sonarrService;
        _strmFileService = strmFileService;
    }

    [HttpPost]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post([FromBody] JsonElement payload)
    {
        _logger.LogInformation("Sonarr webhook received");
        _logger.LogDebug("Payload: {Payload}", payload.GetRawText());

        try
        {
            var webhook = JsonSerializer.Deserialize<SonarrWebhook>(payload.GetRawText());
            
            if (webhook == null)
            {
                _logger.LogWarning("Failed to deserialize webhook payload");
                return BadRequest(new ErrorResponse("Invalid webhook payload"));
            }

            _logger.LogInformation("Event type: {EventType}", webhook.EventType);

            // Check if this is a seriesAdd event
            if (webhook.EventType?.Equals("seriesAdd", StringComparison.OrdinalIgnoreCase) == true)
            {
                return await HandleSeriesAddEventAsync(webhook);
            }

            // For other event types, just acknowledge receipt
            return Ok(new WebhookResponse("Webhook received", webhook.EventType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sonarr webhook");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Error processing webhook", ex.Message));
        }
    }

    private async Task<IActionResult> HandleSeriesAddEventAsync(SonarrWebhook webhook)
    {
        if (webhook.Series == null)
        {
            _logger.LogWarning("SeriesAdd event received but series data is missing");
            return BadRequest(new ErrorResponse("Series data missing in webhook"));
        }

        _logger.LogInformation("Processing seriesAdd event for series ID: {SeriesId}, Title: {SeriesTitle}", 
            webhook.Series.Id, webhook.Series.Title);

        // Fetch full series details from Sonarr API
        var seriesDetails = await _sonarrService.GetSeriesDetailsAsync(webhook.Series.Id);
        
        if (seriesDetails == null)
        {
            _logger.LogError("Failed to fetch series details for series ID: {SeriesId}", webhook.Series.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Failed to fetch series details from Sonarr"));
        }

        // Fetch all episodes for the series
        var episodes = await _sonarrService.GetEpisodesForSeriesAsync(webhook.Series.Id);
        
        _logger.LogInformation("Series has {SeasonCount} seasons and {EpisodeCount} episodes",
            seriesDetails.Seasons.Count, episodes.Count);

        // Create .strm files for all episodes
        await _strmFileService.CreateStrmFilesForSeriesAsync(seriesDetails, episodes);

        return Ok(new WebhookResponse(
            "SeriesAdd event processed successfully",
            webhook.EventType,
            webhook.Series.Id,
            seriesDetails.Title,
            seriesDetails.Seasons.Count,
            episodes.Count));
    }
}
