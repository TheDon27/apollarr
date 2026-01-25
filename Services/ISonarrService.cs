using Apollarr.Models;
namespace Apollarr.Services;

/// <summary>
/// Contract for Sonarr operations so controllers and orchestrators depend on abstractions.
/// </summary>
public interface ISonarrService
{
    Task<SonarrSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default);
    Task<SonarrSeriesDetails?> TryGetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default);
    Task<List<SonarrSeriesDetails>> GetAllSeriesAsync(CancellationToken cancellationToken = default);
    Task<List<Episode>> GetEpisodesForSeriesAsync(int seriesId, CancellationToken cancellationToken = default);
    Task<List<Episode>> GetWantedMissingEpisodesAsync(CancellationToken cancellationToken = default);
    Task UpdateSeriesAsync(SonarrSeriesDetails series, CancellationToken cancellationToken = default);
    Task<bool> DeleteSeriesAsync(int seriesId, bool deleteFiles, bool addImportListExclusion = false, CancellationToken cancellationToken = default);
    Task<SonarrSeriesDetails?> AddSeriesAsync(SonarrSeriesDetails series, CancellationToken cancellationToken = default);
    Task<bool> UpdateEpisodeAsync(Episode episode, CancellationToken cancellationToken = default);
    Task DeleteEpisodeFileAsync(int episodeFileId, CancellationToken cancellationToken = default);
    Task RefreshSeriesAsync(int seriesId, CancellationToken cancellationToken = default);
    Task RescanSeriesAsync(int seriesId, CancellationToken cancellationToken = default);
}
