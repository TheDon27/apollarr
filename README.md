# Apollarr

A .NET Core Web API application that integrates with Sonarr to automatically create .strm files for Apollo.to streaming.

## Features

- **Sonarr Webhook Integration**: Receives webhooks from Sonarr
- **Automatic .strm File Creation**: When a new series is added to Sonarr (via `seriesAdd` event), Apollarr automatically:
  - Fetches series details and all episodes from Sonarr API
  - Creates organized season folders
  - Generates .strm files for each episode pointing to Apollo.to streams
- **Missing Episodes Background Service**: Automatically checks for missing episodes every hour and retries creating .strm files
- **Episode Management**: Only creates .strm files for monitored episodes
- **Stream Validation**: Validates stream URLs before creating files to ensure content availability

## Requirements

- .NET 10.0 SDK or later
- Sonarr instance with API access
- Apollo.to account with streaming access

## Configuration

### Environment Variables

Create a `.env` file in the project root with the following variables:

```env
# Apollo.to credentials
APOLLO_USERNAME=your_apollo_username
APOLLO_PASSWORD=your_apollo_password

# Sonarr configuration
SONARR_URL=http://localhost:8989
SONARR_API_KEY=your_sonarr_api_key
```

See `.env.example` for a template.

### Sonarr Webhook Setup

1. In Sonarr, go to **Settings → Connect**
2. Click the **+** button and select **Webhook**
3. Configure the webhook:
   - **Name**: Apollarr
   - **On Series Add**: ✓ (checked)
   - **URL**: `http://your-apollarr-host:8080/sonarr`
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

## Project Structure

- `Controllers/` - API controllers
  - `SonarrController.cs` - Handles Sonarr webhooks
  - `RadarrController.cs` - Placeholder for Radarr integration
  - `MissingEpisodesController.cs` - Manual trigger for missing episode checks
- `Services/` - Business logic services
  - `SonarrService.cs` - Sonarr API integration
  - `StrmFileService.cs` - .strm file creation
  - `MissingEpisodeService.cs` - Missing episode processing logic
  - `MissingEpisodeBackgroundService.cs` - Background service for scheduled checks
  - `IFileSystemService.cs` - File system abstraction
- `Models/` - Data models
  - `SonarrWebhook.cs` - Webhook payload models
  - `SonarrSeriesDetails.cs` - Sonarr API response models
  - `WantedMissing.cs` - Wanted/missing API response models
  - `ApiResponses.cs` - Standard API response DTOs
- `Common/` - Shared utilities
  - `AppSettings.cs` - Strongly-typed configuration
  - `RetryPolicy.cs` - Centralized retry logic
- `Program.cs` - Application entry point and configuration
- `appsettings.json` - Application configuration
- `Apollarr.csproj` - Project file

## How It Works

1. **Sonarr sends webhook**: When you add a new series in Sonarr, it sends a webhook to Apollarr with `eventType: "seriesAdd"`
2. **Apollarr fetches details**: The app queries Sonarr's API to get complete series information including all seasons and episodes
3. **Directory creation**: Season folders are created following Sonarr's naming convention (e.g., "Season 01")
4. **strm file generation**: For each monitored episode, a .strm file is created with the format:
   ```
   SeriesTitle - S01E01 - EpisodeTitle.strm
   ```
   The file contains the Apollo.to streaming URL:
   ```
   https://username:password@apollo.to/stream/tv/tvdbId/seasonNumber/episodeNumber
   ```

## API Documentation

When running in development mode, Swagger UI is available at:
- http://localhost:8080/swagger

The OpenAPI specification is available at:
- http://localhost:8080/openapi/v1.json

## Endpoints

### POST /sonarr
Receives Sonarr webhooks and processes them based on event type.

**Supported Events:**
- `seriesAdd` - Automatically creates .strm files for all episodes in the series

**Response:**
```json
{
  "message": "SeriesAdd event processed successfully",
  "seriesId": 123,
  "seriesTitle": "Example Series",
  "seasonCount": 5,
  "episodeCount": 50
}
```

### POST /MissingEpisodes/check
Manually triggers a check for missing episodes and attempts to create .strm files for them.

**Response:**
```json
{
  "message": "Missing episode check completed successfully"
}
```

## Background Services

### Missing Episodes Service
Apollarr includes a background service that automatically checks for missing episodes on a configurable interval (default: every hour). This ensures that episodes that were initially unavailable or failed to create get retried automatically.

**Configuration:**
```json
{
  "AppSettings": {
    "MissingEpisode": {
      "Enabled": true,
      "CheckIntervalHours": 1
    }
  }
}
```

For detailed information about the missing episodes feature, see [MISSING_EPISODES_FEATURE.md](MISSING_EPISODES_FEATURE.md).
