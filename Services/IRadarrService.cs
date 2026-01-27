using Apollarr.Models;

namespace Apollarr.Services;

/// <summary>
/// Contract for Radarr operations so controllers and orchestrators depend on abstractions.
/// </summary>
public interface IRadarrService
{
    Task<RadarrMovieDetails?> GetMovieDetailsAsync(int movieId, CancellationToken cancellationToken = default);
    Task<RadarrMovieDetails?> TryGetMovieDetailsAsync(int movieId, CancellationToken cancellationToken = default);
    Task<List<RadarrMovieDetails>> GetAllMoviesAsync(CancellationToken cancellationToken = default);
    Task<List<RadarrMovieDetails>> GetWantedMissingMoviesAsync(CancellationToken cancellationToken = default);
    Task UpdateMovieAsync(RadarrMovieDetails movie, CancellationToken cancellationToken = default);
    Task<bool> DeleteMovieAsync(int movieId, bool deleteFiles, bool addImportListExclusion = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteMovieFileAsync(int movieFileId, CancellationToken cancellationToken = default);
    Task RefreshMovieAsync(int movieId, CancellationToken cancellationToken = default);
    Task RescanMovieAsync(int movieId, CancellationToken cancellationToken = default);
    Task<List<RadarrQualityProfile>> GetQualityProfilesAsync(CancellationToken cancellationToken = default);
    Task<List<RadarrMovieFile>> GetMovieFilesAsync(int movieId, CancellationToken cancellationToken = default);
}
