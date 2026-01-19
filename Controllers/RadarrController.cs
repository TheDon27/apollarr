using Microsoft.AspNetCore.Mvc;
using Apollarr.Models;

namespace Apollarr.Controllers;

[ApiController]
[Route("[controller]")]
public class RadarrController : ControllerBase
{
    private readonly ILogger<RadarrController> _logger;

    public RadarrController(ILogger<RadarrController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType<WebhookResponse>(StatusCodes.Status200OK)]
    public IActionResult Post([FromBody] object payload)
    {
        _logger.LogInformation("Radarr webhook received");
        
        // TODO: Process Radarr webhook payload
        
        return Ok(new WebhookResponse("Radarr webhook received successfully"));
    }
}
