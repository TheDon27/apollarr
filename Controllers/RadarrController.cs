using Microsoft.AspNetCore.Mvc;
using Apollarr.Models;
using Apollarr.Services;

namespace Apollarr.Controllers;

[ApiController]
[Route("[controller]")]
public class RadarrController : ControllerBase
{
    private readonly ILogger<RadarrController> _logger;
    private readonly IRadarrWebhookService _webhookService;

    public RadarrController(
        ILogger<RadarrController> logger,
        IRadarrWebhookService webhookService)
    {
        _logger = logger;
        _webhookService = webhookService;
    }

    [HttpPost]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post([FromBody] RadarrWebhook webhook)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (webhook == null || string.IsNullOrWhiteSpace(webhook.EventType))
        {
            _logger.LogWarning("Webhook payload missing or invalid");
            return BadRequest(new ErrorResponse("Invalid webhook payload"));
        }

        try
        {
            if (webhook.EventType.Equals("movieAdd", StringComparison.OrdinalIgnoreCase) &&
                webhook.Movie == null)
            {
                _logger.LogWarning("MovieAdd event received but movie data is missing");
                return BadRequest(new ErrorResponse("Movie data missing in webhook"));
            }

            var response = await _webhookService.HandleWebhookAsync(webhook, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Radarr webhook");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Error processing webhook", ex.Message));
        }
    }

    [HttpPost("monitor/wanted")]
    [ProducesResponseType<MonitorWantedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MonitorWanted()
    {
        _logger.LogInformation("Monitor wanted/missing movies endpoint called");

        try
        {
            var result = await _webhookService.MonitorWantedAsync(HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing monitor wanted movies request");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Error processing monitor wanted movies request", ex.Message));
        }
    }
}
