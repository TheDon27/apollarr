using Apollarr.Common;
using Microsoft.Extensions.Options;

namespace Apollarr.Services;

public class MissingEpisodeBackgroundService : BackgroundService
{
    private readonly ILogger<MissingEpisodeBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval;
    private readonly bool _enabled;

    public MissingEpisodeBackgroundService(
        ILogger<MissingEpisodeBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _enabled = appSettings.Value.MissingEpisode.Enabled;
        _checkInterval = TimeSpan.FromHours(appSettings.Value.MissingEpisode.CheckIntervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Missing Episode Background Service is disabled");
            return;
        }

        _logger.LogInformation("Missing Episode Background Service started. Will check every {Interval}",
            _checkInterval);

        // Wait until the next hour mark before starting
        await WaitUntilNextHourAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Running scheduled missing episode check");

                // Create a scope to resolve scoped services
                using (var scope = _serviceProvider.CreateScope())
                {
                    var missingEpisodeService = scope.ServiceProvider
                        .GetRequiredService<MissingEpisodeService>();

                    await missingEpisodeService.ProcessMissingEpisodesAsync(stoppingToken);
                }

                _logger.LogInformation("Scheduled missing episode check completed. Next check in {Interval}",
                    _checkInterval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled missing episode check");
            }

            // Wait for the next interval
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
        }

        _logger.LogInformation("Missing Episode Background Service stopped");
    }

    private async Task WaitUntilNextHourAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var nextHour = now.Date.AddHours(now.Hour + 1);
        var delay = nextHour - now;

        if (delay.TotalMilliseconds > 0)
        {
            _logger.LogInformation("Waiting {Delay} until next hour mark ({NextHour} UTC) before first check",
                delay, nextHour);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // Service is stopping before first check
            }
        }
    }
}
