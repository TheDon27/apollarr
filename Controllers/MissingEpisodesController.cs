using Microsoft.AspNetCore.Mvc;
using Apollarr.Models;
using Apollarr.Services;

namespace Apollarr.Controllers;

[ApiController]
[Route("[controller]")]
public class MissingEpisodesController : ControllerBase
{
    private readonly ILogger<MissingEpisodesController> _logger;
    private readonly MissingEpisodeService _missingEpisodeService;

    public MissingEpisodesController(
        ILogger<MissingEpisodesController> logger,
        MissingEpisodeService missingEpisodeService)
    {
        _logger = logger;
        _missingEpisodeService = missingEpisodeService;
    }

    [HttpPost("check")]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CheckMissingEpisodes()
    {
        _logger.LogInformation("Manual missing episode check triggered");

        try
        {
            await _missingEpisodeService.ProcessMissingEpisodesAsync();
            return Ok(new WebhookResponse("Missing episode check completed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual missing episode check");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Error processing missing episodes", ex.Message));
        }
    }
}
