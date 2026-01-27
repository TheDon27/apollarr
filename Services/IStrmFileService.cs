using Apollarr.Models;

namespace Apollarr.Services;

/// <summary>
/// Contract for STRM file generation and monitoring workflows.
/// </summary>
public interface IStrmFileService
{
    Task<SeriesValidationResult> ProcessSeriesEpisodesAsync(SonarrSeriesDetails series, List<Episode> episodes, CancellationToken cancellationToken = default);
    Task<MonitorSeriesResponse> ProcessSeriesMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default);
    Task<MonitorWantedResponse> ProcessWantedMissingAsync(CancellationToken cancellationToken = default);
    Task<MonitorWantedResponse> ProcessWantedMissingMoviesAsync(CancellationToken cancellationToken = default);
    Task<MonitorSeasonsResponse> ProcessSeasonsMonitoringAsync(bool onlyMonitored, CancellationToken cancellationToken = default);
    Task CreateStrmFilesForSeriesAsync(SonarrSeriesDetails series, List<Episode> episodes, CancellationToken cancellationToken = default);
    Task<bool> ValidateStreamUrlAsync(string streamUrl, CancellationToken cancellationToken = default);
    Task<MovieValidationResult> ProcessMovieAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default);
    Task<bool> ValidateMovieLinkAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default);
}
