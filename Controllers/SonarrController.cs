using Microsoft.AspNetCore.Mvc;
using Apollarr.Models;
using Apollarr.Services;

namespace Apollarr.Controllers;

[ApiController]
[Route("[controller]")]
public class SonarrController : ControllerBase
{
    private readonly ILogger<SonarrController> _logger;
    private readonly ISonarrWebhookService _webhookService;

    public SonarrController(
        ILogger<SonarrController> logger,
        ISonarrWebhookService webhookService)
    {
        _logger = logger;
        _webhookService = webhookService;
    }

    [HttpPost]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post([FromBody] SonarrWebhook webhook)
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
            if (webhook.EventType.Equals("seriesAdd", StringComparison.OrdinalIgnoreCase) &&
                webhook.Series == null)
            {
                _logger.LogWarning("SeriesAdd event received but series data is missing");
                return BadRequest(new ErrorResponse("Series data missing in webhook"));
            }

            var response = await _webhookService.HandleWebhookAsync(webhook, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sonarr webhook");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Error processing webhook", ex.Message));
        }
    }

    [HttpPost("monitor/wanted")]
    [ProducesResponseType<MonitorWantedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MonitorWanted()
    {
        _logger.LogInformation("Monitor wanted/missing endpoint called");

        try
        {
            var result = await _webhookService.MonitorWantedAsync(HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing monitor wanted request");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new ErrorResponse("Error processing monitor wanted request", ex.Message));
        }
    }
}
