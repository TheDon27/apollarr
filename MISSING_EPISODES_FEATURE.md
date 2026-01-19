# Missing Episodes Background Service

## Overview
The Missing Episodes Background Service automatically checks for missing episodes on an hourly basis and retries creating .strm files for them. This ensures that episodes that were initially unavailable or failed to create get another chance.

## How It Works

1. **Scheduled Checks**: Runs on the hour (configurable interval)
2. **Fetch Missing**: Queries Sonarr's `/api/v3/wanted/missing` endpoint with `monitored=true` filter
3. **Filter Episodes**: Only processes episodes where `monitored=true` (double-checked client-side)
4. **Group by Series**: Organizes missing episodes by their series
5. **Create .strm Files**: Attempts to create .strm files for each missing episode
6. **Validation**: Validates stream URLs before creating files (if enabled)

## Configuration

### Environment Variables
```bash
# Existing configuration still required
SONARR_URL=http://sonarr:8989
SONARR_API_KEY=your-api-key
APOLLO_USERNAME=your-username
APOLLO_PASSWORD=your-password
```

### Optional Configuration (appsettings.json)
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

### Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable/disable the background service |
| `CheckIntervalHours` | `1` | How often to check for missing episodes (in hours) |

## Features

### Automatic Processing
- Runs continuously in the background
- Starts at the next hour mark after application startup
- Processes only **monitored** missing episodes from Sonarr
- Groups episodes by series for efficient processing
- Handles pagination for large numbers of missing episodes
- Double-checks monitored status both server-side (API filter) and client-side

### Error Handling
- Continues processing other series if one fails
- Logs detailed error information
- Retries with exponential backoff (inherited from SonarrService)
- Graceful shutdown on application stop

### Manual Trigger
You can manually trigger a missing episode check via the API:

```bash
POST /MissingEpisodes/check
```

**Response:**
```json
{
  "message": "Missing episode check completed successfully"
}
```

This is useful for:
- Testing the feature
- Triggering an immediate check without waiting for the scheduled run
- Running checks on-demand after adding new series

## Logging

The service provides detailed logging at various levels:

### Information Level
```
Missing Episode Background Service started. Will check every 01:00:00
Running scheduled missing episode check
Found 42 missing episodes
Missing episodes span 5 series
Processing 8 missing episodes for series ID 123
Creating .strm files for missing episodes of series: Breaking Bad
Scheduled missing episode check completed. Next check in 01:00:00
```

### Warning Level
```
Could not fetch series details for series ID 456, skipping
Stream URL is not valid for Breaking Bad S01E05, skipping .strm file creation
```

### Error Level
```
Error processing missing episodes for series ID 789
Error during scheduled missing episode check
```

## Architecture

### Components

1. **MissingEpisodeBackgroundService**
   - Hosted service that runs in the background
   - Manages scheduling and timing
   - Creates service scopes for dependency injection

2. **MissingEpisodeService**
   - Business logic for processing missing episodes
   - Coordinates between SonarrService and StrmFileService
   - Groups and processes episodes by series

3. **SonarrService.GetWantedMissingEpisodesAsync**
   - Fetches missing episodes from Sonarr API
   - Handles pagination automatically
   - Returns all missing episodes across all pages

### Service Lifecycle

```
Application Start
    ↓
Background Service Starts
    ↓
Wait Until Next Hour Mark
    ↓
┌─────────────────────────┐
│  Fetch Missing Episodes │
└───────────┬─────────────┘
            ↓
┌─────────────────────────┐
│  Group by Series        │
└───────────┬─────────────┘
            ↓
┌─────────────────────────┐
│  For Each Series:       │
│  - Fetch Details        │
│  - Create .strm Files   │
└───────────┬─────────────┘
            ↓
┌─────────────────────────┐
│  Wait for Next Interval │
└───────────┬─────────────┘
            ↓
         Repeat
```

## Benefits

1. **Automatic Recovery**: Episodes that fail initially get retried automatically
2. **Availability Handling**: Handles cases where content becomes available later
3. **No Manual Intervention**: Runs completely automatically
4. **Configurable**: Adjust check frequency based on your needs
5. **Resource Efficient**: Only processes missing episodes, not all episodes
6. **Graceful**: Can be disabled without affecting other functionality

## Performance Considerations

### Pagination
The service automatically handles pagination when fetching missing episodes, processing 100 episodes per page. This prevents memory issues with large libraries.

### Scoped Services
Each check creates a new service scope, ensuring proper disposal of resources and preventing memory leaks.

### Cancellation Support
The service respects cancellation tokens, allowing for graceful shutdown without waiting for completion.

### Validation Timeout
Stream URL validation has a configurable timeout (default 10 seconds) to prevent hanging on slow/unresponsive endpoints.

## Troubleshooting

### Service Not Running
Check logs for:
```
Missing Episode Background Service is disabled
```
Solution: Set `MissingEpisode.Enabled` to `true`

### No Missing Episodes Found
This is normal if all episodes have files. Check Sonarr's Wanted > Missing page to verify.

### Episodes Not Being Created
Check logs for:
- Series without IMDb IDs (will be skipped)
- Invalid stream URLs (will be skipped)
- File system permission errors

### High Resource Usage
Consider:
- Increasing `CheckIntervalHours` to reduce frequency
- Disabling stream URL validation if not needed
- Checking Sonarr API performance

## Example Scenarios

### Scenario 1: New Series Added
1. User adds a new series to Sonarr
2. Sonarr webhook triggers immediate .strm file creation
3. Some episodes fail validation (not available yet)
4. Background service retries every hour
5. When content becomes available, .strm files are created

### Scenario 2: Streaming Service Outage
1. Streaming service goes down temporarily
2. All validations fail, no .strm files created
3. Service comes back online
4. Next hourly check succeeds and creates all files

### Scenario 3: Manual Testing
1. Developer wants to test the feature
2. Calls `POST /MissingEpisodes/check`
3. Immediate check runs without waiting for schedule
4. Reviews logs for results

## Integration with Existing Features

The missing episode service integrates seamlessly with:
- **Webhook Processing**: Works alongside real-time webhook processing
- **Retry Logic**: Uses the same retry policies as other services
- **Configuration**: Shares configuration system with other components
- **Logging**: Uses consistent logging patterns
- **Validation**: Uses the same stream URL validation logic

## Future Enhancements

Potential improvements:
1. Configurable sort order (by air date, series, etc.)
2. Filter by series tags or quality profiles
3. Notification system for failed episodes
4. Metrics and statistics tracking
5. Rate limiting for API calls
6. Selective processing (only specific series)
