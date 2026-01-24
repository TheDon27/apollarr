using Apollarr.Common;
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
    private volatile bool _isDailyValidationRunning;
    private volatile bool _isWeeklyValidationRunning;

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
        _logger.LogInformation("Daily missing validation: {Enabled} at {Time}", 
            _settings.EnableDailyMissingValidation, _settings.DailyMissingValidationTime);
        _logger.LogInformation("Weekly full validation: {Enabled} on {Day} at {Time}", 
            _settings.EnableWeeklyFullValidation, _settings.WeeklyFullValidationDay, _settings.WeeklyFullValidationTime);
        _logger.LogInformation(
            "Hourly series monitoring: {Enabled} at minute {Minute} (onlyMonitored={OnlyMonitored})",
            _settings.EnableHourlySeriesMonitoring, _settings.HourlySeriesMonitoringMinute, _settings.HourlySeriesMonitoringOnlyMonitored);
        _logger.LogInformation(
            "Wanted/missing monitoring: {Enabled} every {Interval} minutes",
            _settings.EnableWantedMissingMonitoring, _settings.WantedMissingIntervalMinutes);

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

                // Check if it's time for daily missing validation
                if (_settings.EnableDailyMissingValidation && !_isDailyValidationRunning)
                {
                    var dailyTime = ParseTime(_settings.DailyMissingValidationTime);
                    if (ShouldRunTask(now, dailyTime, null))
                    {
                        _logger.LogInformation("Starting daily missing-only validation task");
                        _isDailyValidationRunning = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RunDailyMissingValidationAsync(stoppingToken);
                            }
                            finally
                            {
                                _isDailyValidationRunning = false;
                            }
                        });
                    }
                }

                // Check if it's time for weekly full validation
                if (_settings.EnableWeeklyFullValidation && !_isWeeklyValidationRunning)
                {
                    var weeklyTime = ParseTime(_settings.WeeklyFullValidationTime);
                    if (ShouldRunTask(now, weeklyTime, _settings.WeeklyFullValidationDay))
                    {
                        _logger.LogInformation("Starting weekly full validation task");
                        _isWeeklyValidationRunning = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await RunWeeklyFullValidationAsync(stoppingToken);
                            }
                            finally
                            {
                                _isWeeklyValidationRunning = false;
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

    private async Task RunDailyMissingValidationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var strmFileService = scope.ServiceProvider.GetRequiredService<IStrmFileService>();

            _logger.LogInformation("Running daily missing-only validation (tag=missing)");
            var result = await strmFileService.ProcessEpisodesMonitoringAsync(
                onlyMonitored: false, 
                tagFilter: "missing",
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Daily missing validation complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, " +
                "Valid Links: {ValidLinks}",
                result.SeriesProcessed, result.EpisodesProcessed, result.EpisodesWithValidLinks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running daily missing validation");
        }
    }

    private async Task RunWeeklyFullValidationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var strmFileService = scope.ServiceProvider.GetRequiredService<IStrmFileService>();

            _logger.LogInformation("Running weekly full validation (all episodes)");
            var result = await strmFileService.ProcessEpisodesMonitoringAsync(
                onlyMonitored: false, 
                tagFilter: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Weekly full validation complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, " +
                "Valid Links: {ValidLinks}",
                result.SeriesProcessed, result.EpisodesProcessed, result.EpisodesWithValidLinks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running weekly full validation");
        }
    }

    private async Task RunWantedMissingMonitoringAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var strmFileService = scope.ServiceProvider.GetRequiredService<IStrmFileService>();

            var result = await strmFileService.ProcessWantedMissingAsync(cancellationToken);

            _logger.LogInformation(
                "Wanted/missing monitoring complete. Series: {SeriesProcessed}, Episodes: {EpisodesProcessed}, Valid: {ValidLinks}, STRM: {StrmCreated}, Rescans: {Rescans}",
                result.SeriesProcessed, result.EpisodesProcessed, result.EpisodesWithValidLinks, result.StrmFilesCreated, result.RescansTriggered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running wanted/missing monitoring");
        }
    }

    private TimeSpan ParseTime(string timeString)
    {
        if (TimeSpan.TryParse(timeString, out var time))
            return time;

        _logger.LogWarning("Invalid time format: {TimeString}, using 02:00", timeString);
        return new TimeSpan(2, 0, 0);
    }

    private bool ShouldRunTask(DateTime now, TimeSpan scheduledTime, DayOfWeek? requiredDay)
    {
        // Check if it's the right day (if specified)
        if (requiredDay.HasValue && now.DayOfWeek != requiredDay.Value)
            return false;

        // Check if we're within the scheduled minute
        var currentTime = now.TimeOfDay;
        var scheduledMinute = new TimeSpan(scheduledTime.Hours, scheduledTime.Minutes, 0);
        var nextMinute = scheduledMinute.Add(TimeSpan.FromMinutes(1));

        return currentTime >= scheduledMinute && currentTime < nextMinute;
    }
}
