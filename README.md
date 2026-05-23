# Apollarr

A .NET 8 ASP.NET Core Web API that integrates with **Sonarr** (TV) and **Radarr** (movies) to automatically create `.strm` files pointing at an external streaming provider. The default stream URL template targets `starlite.best`; streaming credentials are supplied via the `APOLLO_*` environment variables.

## Features

- **Sonarr & Radarr Webhook Integration**: Receives webhooks from both Sonarr and Radarr.
- **Automatic .strm File Creation**:
  - On `seriesAdd`, Apollarr fetches the series and all episodes, monitors the series and all regular seasons (specials off), validates each episode's stream link, writes `.strm` files for valid links, and triggers a Sonarr rescan.
  - On `movieAdd`, Apollarr sets the movie to monitored with the **SDTV** quality profile, validates the link, removes any existing movie files, writes a `.strm` file, sets the movie unmonitored, and triggers a Radarr rescan.
- **Wanted/Missing Monitor**: `POST /sonarr/monitor/wanted` and `POST /radarr/monitor/wanted` fetch wanted/missing items from the *arr APIs, validate stream links, and create `.strm` files for valid links. These also run automatically on startup and on a configurable interval (default every 15 minutes).
- **Scheduled Series Sweep**: An hourly background sweep re-validates monitored series and refreshes their `.strm` files.
- **Stream Validation**: Validates stream URLs (via an HTTP `HEAD` request, treating a redirect to the provider's error page as invalid) before creating files.
- **Idempotent & Self-Healing**: Existing `.strm` files are reused rather than recreated; rescans only fire when a new file is actually written; links that go invalid have their `.strm` files cleaned up.

## Requirements

- .NET 8.0 SDK or later (LTS)
- Sonarr instance with API access
- Radarr instance with API access (for movie support)
- A streaming account compatible with the configured stream URL template

## Configuration

### Environment Variables

Create a `.env` file in the project root with the following variables (keep this file out of source control):

```env
# Streaming credentials
APOLLO_USERNAME=your_username
APOLLO_PASSWORD=your_password

# Sonarr configuration
SONARR_URL=http://localhost:8989
SONARR_API_KEY=your_sonarr_api_key

# Radarr configuration
RADARR_URL=http://localhost:7878
RADARR_API_KEY=your_radarr_api_key
```

See `.env.example` for a template.

The `APOLLO_USERNAME` and `APOLLO_PASSWORD` values are validated on startup and the API will not start without them. The Sonarr/Radarr URLs and API keys are read from these environment variables (or the `AppSettings` config section).

Application behavior (stream URL template, validation, scheduling) is configured under the `AppSettings` section of `appsettings.json`:

```json
{
  "AppSettings": {
    "Strm": {
      "StreamUrlTemplate": "https://starlite.best/api/stream/{username}/{password}/tvshow/{imdbId}/{season}/{episode}",
      "ValidateUrls": true,
      "ValidationTimeoutSeconds": 10
    },
    "Scheduling": {
      "EnableHourlySeriesMonitoring": true,
      "HourlySeriesMonitoringMinute": 0,
      "HourlySeriesMonitoringOnlyMonitored": true,
      "EnableWantedMissingMonitoring": true,
      "WantedMissingIntervalMinutes": 15
    }
  }
}
```

Movie stream URLs are derived from the same template by substituting `/tvshow/` with `/movie/` and dropping the season/episode segments.

In production, HTTPS redirection and HSTS are enabled by default—ensure TLS termination is configured on your ingress or reverse proxy.

### Sonarr Webhook Setup

1. In Sonarr, go to **Settings → Connect**
2. Click the **+** button and select **Webhook**
3. Configure the webhook:
   - **Name**: Apollarr
   - **On Series Add**: ✓ (checked)
   - **URL**: `http://your-apollarr-host:8080/sonarr`
   - **Method**: POST
4. Save the connection

### Radarr Webhook Setup

1. In Radarr, go to **Settings → Connect**
2. Click the **+** button and select **Webhook**
3. Configure the webhook:
   - **Name**: Apollarr
   - **On Movie Added**: ✓ (checked)
   - **URL**: `http://your-apollarr-host:8080/radarr`
   - **Method**: POST
4. Save the connection

## Getting Started

### Run the application locally

```bash
dotnet run
```

The API will be available at:
- HTTP: http://localhost:8080

### Run with Docker

Build the Docker image:

```bash
docker build -t apollarr .
```

Run the container:

```bash
docker run -p 8080:8080 -v /path/to/media:/media --env-file .env apollarr
```

**Important**: Mount your media directory so Apollarr can create .strm files in the same paths that Sonarr uses.

The API will be available at http://localhost:8080

### Build the application

```bash
dotnet build
```

### Run tests

```bash
dotnet test
```

Unit tests live in [tests/Apollarr.Tests/](tests/Apollarr.Tests/) and cover stream-URL validation, filename sanitization, configuration validation, and the Sonarr/Radarr webhook orchestration.

## Project Structure

- `Controllers/` - API controllers
  - `SonarrController.cs` - Sonarr webhook + wanted/missing endpoint
  - `RadarrController.cs` - Radarr webhook + wanted/missing endpoint
- `Services/` - Business logic services
  - `SonarrService.cs` / `RadarrService.cs` - *arr REST API integration
  - `SonarrApiClient.cs` / `RadarrApiClient.cs` - Typed `HttpClient` wrappers
  - `SonarrWebhookService.cs` / `RadarrWebhookService.cs` - Per-event orchestration
  - `StrmFileService.cs` - `.strm` file creation, validation, and monitoring logic (TV + movies)
  - `ValidationSchedulerService.cs` - Background service for scheduled monitoring
  - `IFileSystemService.cs` - File system abstraction
- `Models/` - Data models
  - `SonarrWebhook.cs` / `RadarrWebhook.cs` - Webhook payload models
  - `SonarrSeriesDetails.cs` / `RadarrMovieDetails.cs` - *arr API response models
  - `RadarrQualityProfile.cs` - Radarr quality profile model
  - `WantedMissing.cs` / `WantedMissingMovies.cs` - Wanted/missing API response models
  - `ApiResponses.cs` - Standard API response DTOs
- `Common/` - Shared utilities
  - `AppSettings.cs` - Strongly-typed configuration
  - `RetryPolicy.cs` - Centralized retry logic
  - `Middleware/` - Correlation ID and exception-handling middleware
- `tests/Apollarr.Tests/` - xUnit test project
- `Program.cs` - Application entry point and configuration
- `appsettings.json` - Application configuration
- `Apollarr.csproj` - Project file
- `Apollarr.sln` - Solution tying the app and test projects together

## How It Works

1. **An *arr app sends a webhook**: Adding a series in Sonarr (`eventType: "seriesAdd"`) or a movie in Radarr (`eventType: "movieAdd"`) triggers a webhook to Apollarr.
2. **Apollarr fetches details**: The app queries the Sonarr/Radarr API for complete series/movie metadata, including the IMDb ID used to build the stream URL.
3. **Stream validation**: For each episode (or movie), Apollarr builds a stream URL from the configured template and validates it with an HTTP `HEAD` request. A redirect to the provider's error page counts as invalid.
4. **Directory + .strm file generation**: For valid links, Apollarr ensures the target directory exists and writes a `.strm` file whose contents are the stream URL.
   - TV episodes: `Season NN/SeriesTitle - S01E01 - EpisodeTitle.strm`
   - Movies: `MovieTitle (Year).strm`
5. **Rescan**: When a new `.strm` file is written, Apollarr triggers a Sonarr/Radarr rescan so the file is imported. Existing files are left untouched (idempotent).

## API Documentation & Observability

When running in development mode, Swagger UI is available at:
- http://localhost:8080/swagger

The OpenAPI specification is available at:
- http://localhost:8080/openapi/v1.json

Each request returns or echoes an `X-Correlation-ID` header for tracing across logs and clients. Errors are returned as RFC 7807 `application/problem+json` responses and avoid logging bodies to reduce accidental exposure of sensitive data.

## Endpoints

### POST /sonarr
Receives Sonarr webhooks and processes them based on event type.

**Supported Events:**
- `seriesAdd` - Monitors the series and regular seasons, validates every episode, and creates `.strm` files for valid links.

Other event types are acknowledged but not acted on.

### POST /sonarr/monitor/wanted
Fetches wanted/missing episodes from Sonarr, validates stream links, creates `.strm` files for valid links, and triggers a rescan when new files are created. Also runs on startup and every 15 minutes (configurable) via the scheduler.

**Response:**
```json
{
  "message": "Wanted/missing monitoring completed",
  "seriesProcessed": 3,
  "episodesProcessed": 25,
  "episodesWithValidLinks": 18,
  "strmFilesCreated": 12,
  "rescansTriggered": 2
}
```

### POST /radarr
Receives Radarr webhooks and processes them based on event type.

**Supported Events:**
- `movieAdd` - Sets the movie to monitored with the SDTV quality profile, validates the link, removes existing files, creates a `.strm` file, sets the movie unmonitored, and triggers a rescan.

Other event types are acknowledged but not acted on.

### POST /radarr/monitor/wanted
Fetches wanted/missing movies from Radarr, validates stream links, creates `.strm` files for valid links, and triggers a rescan when new files are created. Also runs on startup and every 15 minutes (configurable) via the scheduler.

**Response:**
```json
{
  "message": "Wanted/missing movies monitoring completed",
  "seriesProcessed": 5,
  "episodesProcessed": 5,
  "episodesWithValidLinks": 4,
  "strmFilesCreated": 3,
  "rescansTriggered": 3
}
```
