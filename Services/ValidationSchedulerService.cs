using Apollarr.Common;
using Apollarr.Models;
using Microsoft.Extensions.Options;

namespace Apollarr.Services;

public class ValidationSchedulerService : BackgroundService
{
    private readonly ILogger<ValidationSchedulerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SchedulingSettings _settings;
    private DateTime? _lastHourlySeriesMonitoringRun;
    private DateTime? _lastWantedMissingRun;
    private volatile bool _isHourlySeriesMonitoringRunning;
    private volatile bool _isWantedMissingRunning;

    public ValidationSchedulerService(
        ILogger<ValidationSchedulerService> logger,
        IServiceProvider serviceProvider,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = appSettings.Value.Scheduling;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Validation Scheduler Service started");
        _logger.LogInformation(
            "Hourly series monitoring: {Enabled} at minute {Minute} (onlyMonitored={OnlyMonitored})",
            _settings.EnableHourlySeriesMonitoring, _settings.HourlySeriesMonitoringMinute, _settings.HourlySeriesMonitoringOnlyMonitored);
        _logger.LogInformation(
            "Wanted/missing monitoring: {Enabled} every {Interval} minutes",
            _settings.EnableWantedMissingMonitoring, _settings.WantedMissingIntervalMinutes);

        // Run wanted/missing checks on startup (both TV shows and movies)
        if (_settings.EnableWantedMissingMonitoring)
        {
            _logger.LogInformation("Running startup wanted/missing checks for TV shows and movies");
            _isWantedMissingRunning = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunWantedMissingMonitoringAsync(stoppingToken);
                    _lastWantedMissingRun = DateTime.Now;
                }
                finally
                {
                    _isWantedMissingRunning = false;
                }
            });
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;

                // Check if it's time for hourly series monitoring
                if (_settings.EnableHourlySeriesMonitoring && !_isHourlySeriesMonitoringRunning)
                {
                    var shouldRunThisHour = now.Minute == _settings.HourlySeriesMonitoringMinute
                                            && (_lastHourlySeriesMonitoringRun == null
                                                || _lastHourlySeriesMonitoringRun.Value.Date != now.Date
                                                || _lastHourlySeriesMonitoringRun.Value.Hour != now.Hour);

                    if (shouldRunThisHour)
                    {
                        _logger.LogInformation("Starting hourly series monitoring task");
                        _isHourlySeriesMonitoringRunning = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RunHourlySeriesMonitoringAsync(stoppingToken);
                                _lastHourlySeriesMonitoringRun = DateTime.Now;
                            }
                            finally
                            {
                                _isHourlySeriesMonitoringRunning = false;
                            }
                        });
                    }
                }

                // Check if it's time for wanted/missing monitoring
                if (_settings.EnableWantedMissingMonitoring && !_isWantedMissingRunning)
                {
                    var intervalMinutes = Math.Max(1, _settings.WantedMissingIntervalMinutes);
                    var shouldRunWanted = _lastWantedMissingRun == null ||
                                          now - _lastWantedMissingRun >= TimeSpan.FromMinutes(intervalMinutes);

                    if (shouldRunWanted)
                    {
                        _logger.LogInformation("Starting wanted/missing monitoring task");
                        _isWantedMissingRunning = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RunWantedMissingMonitoringAsync(stoppingToken);
                                _lastWantedMissingRun = DateTime.Now;
                            }
                            finally
                            {
                                _isWantedMissingRunning = false;
                            }
                        });
                    }
                }

                // Wait 1 minute before checking again
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in validation scheduler");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Validation Scheduler Service stopped");
    }

    private async Task RunHourlySeriesMonitoringAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var strmFileService = scope.ServiceProvider.GetRequiredService<IStrmFileService>();

            var result = await strmFileService.ProcessSeriesMonitoringAsync(
                onlyMonitored: _settings.HourlySeriesMonitoringOnlyMonitored,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Hourly series monitoring complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, Valid: {ValidLinks}, Missing: {Missing}, Rescans: {Rescans}",
                result.SeriesProcessed, result.EpisodesProcessed, result.EpisodesWithValidLinks, result.EpisodesMissing, result.RescansTriggered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running hourly series monitoring");
        }
    }

    private async Task RunWantedMissingMonitoringAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var strmFileService = scope.ServiceProvider.GetRequiredService<IStrmFileService>();

            // Run both TV shows and movies wanted/missing checks concurrently
            var tvShowsTask = strmFileService.ProcessWantedMissingAsync(cancellationToken);
            
            var moviesTask = Task.Run(async () =>
            {
                try
                {
                    return await strmFileService.ProcessWantedMissingMoviesAsync(cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Radarr service is not configured"))
                {
                    _logger.LogInformation("Radarr service not configured, skipping wanted/missing movies check");
                    return (MonitorWantedResponse?)null;
                }
            }, cancellationToken);

            // Wait for both tasks to complete (run concurrently)
            var tvShowsResult = await tvShowsTask;
            var moviesResult = await moviesTask;

            _logger.LogInformation(
                "Wanted/missing monitoring complete. TV Shows - Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, Valid: {ValidLinks}, STRM: {StrmCreated}, Rescans: {Rescans}",
                tvShowsResult.SeriesProcessed, tvShowsResult.EpisodesProcessed, tvShowsResult.EpisodesWithValidLinks, tvShowsResult.StrmFilesCreated, tvShowsResult.RescansTriggered);

            if (moviesResult != null)
            {
                _logger.LogInformation(
                    "Wanted/missing movies monitoring complete. Movies: {MoviesProcessed}, Valid: {ValidLinks}, STRM: {StrmCreated}, Rescans: {Rescans}",
                    moviesResult.SeriesProcessed, moviesResult.EpisodesWithValidLinks, moviesResult.StrmFilesCreated, moviesResult.RescansTriggered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running wanted/missing monitoring");
        }
    }

}
