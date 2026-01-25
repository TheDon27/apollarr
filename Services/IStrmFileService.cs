using Apollarr.Models;

namespace Apollarr.Services;

/// <summary>
/// Contract for STRM file generation and monitoring workflows.
/// </summary>
public interface IStrmFileService
{
    Task<SeriesValidationResult> ProcessSeriesEpisodesAsync(SonarrSeriesDetails series, List<Episode> episodes, CancellationToken cancellationToken = default);
    Task<MonitorEpisodesResponse> ProcessEpisodesMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default);
    Task<MonitorSeriesResponse> ProcessSeriesMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default);
    Task<MonitorWantedResponse> ProcessWantedMissingAsync(CancellationToken cancellationToken = default);
    Task<MonitorSeasonsResponse> ProcessSeasonsMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default);
    Task CreateStrmFilesForSeriesAsync(SonarrSeriesDetails series, List<Episode> episodes, CancellationToken cancellationToken = default);
    Task<bool> ValidateStreamUrlAsync(string streamUrl, CancellationToken cancellationToken = default);
}
