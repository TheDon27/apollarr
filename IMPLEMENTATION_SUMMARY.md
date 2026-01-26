# Apollarr Implementation Summary

## Overview
This document summarizes the implementation of the Sonarr STRM Monitoring & Validation Automation system according to the requirements.

## ✅ Implemented Features

### 1. Event-Driven Behavior

#### Series Add Event (`/Sonarr` webhook)
- **Trigger**: Sonarr webhook with `eventType = seriesAdd`
- **Actions**:
  - ✅ Monitors the newly added series
  - ✅ Monitors all seasons in the series
  - ✅ Monitors all episodes in the series
  - ✅ **NEW**: Validates all episodes and creates .strm files for valid links
  - ✅ **NEW**: Tags episodes as `missing` if no valid link found
- **Result**: Newly added series stays monitored and immediately has .strm files for available episodes; missing episodes are tagged

### 2. Core Episode Validation Logic

The validation logic is reusable and handles:

#### When NO Valid Link is Found:
1. ✅ Deletes `.strm` file if it exists
2. ✅ Tags episode as `missing` in Sonarr
3. ✅ Exits episode workflow

#### When Valid Link is Found:
1. ✅ Checks if `.strm` file already exists (idempotent)
2. ✅ If non-`.strm` media file exists, deletes it
3. ✅ Creates `.strm` file with valid link
4. ✅ Removes `missing` tag if present
5. ✅ Unmonitors the episode

### 3. Endpoints

#### `/Sonarr` (POST webhook)
- Handles Sonarr events (notably `seriesAdd`) and immediately validates/creates `.strm` files for the new series.

#### `/Sonarr/Monitor/wanted` (POST)
- Manually triggers the wanted/missing workflow to validate links and create `.strm` files for Sonarr’s wanted list.

### 4. Scheduled Tasks

- Hourly series monitoring (seriesAdd-like sweep across monitored series).
- Wanted/missing monitoring on a configurable interval (default 15 minutes).

### 5. Configuration

Configuration is in `appsettings.json`:

```json
{
  "AppSettings": {
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

## 🏗️ Architecture

### New Services

#### `ValidationSchedulerService` (Background Service)
- Runs scheduled tasks automatically
- Manages hourly series monitoring and frequent wanted/missing checks
- Configurable via `appsettings.json`

### Enhanced Services

#### `SonarrService`
Added tag management methods:
- `GetAllTagsAsync()`: Retrieves all tags from Sonarr
- `GetOrCreateTagAsync(string tagLabel)`: Gets or creates a tag
- `AddTagToEpisodeAsync(Episode, int tagId)`: Adds tag to episode
- `RemoveTagFromEpisodeAsync(Episode, int tagId)`: Removes tag from episode
- `GetEpisodesByTagAsync(int tagId)`: Gets all episodes with specific tag

#### `StrmFileService`
Enhanced methods:
- `ProcessSeasonsMonitoringAsync(bool onlyMonitored)`: New method for season monitoring
- `ProcessEpisodeForMonitoringAsync()`: Updated to handle missing tags and idempotency

### New Models

#### `SonarrTag`
```csharp
public class SonarrTag
{
    public int Id { get; set; }
    public string Label { get; set; }
}
```

#### `MonitorSeasonsResponse`
```csharp
public record MonitorSeasonsResponse(
    string Message,
    int SeriesProcessed,
    int SeasonsProcessed);
```

#### Updated Models
- `Episode`: Added `Tags` property (List<int>)
- `SonarrSeriesDetails`: Already had `Tags` property

## 🔒 Safety & Idempotency

### Idempotent Operations
✅ Re-running any task will not duplicate files or tags
✅ If `.strm` file exists and link is valid, no action taken
✅ Tag operations check existence before adding/removing

### Safety Rules
✅ File deletion only occurs when replacing with `.strm` file
✅ Monitoring state changes are explicit and intentional
✅ All operations safe to retry
✅ Error handling prevents partial state changes

## 📊 Expected Outcomes

✅ Sonarr can continue monitoring new series while Apollarr manages .strm creation
✅ Only `.strm` files exist for valid episodes
✅ Episodes without valid streams are clearly marked with `missing` tag

## 🚀 Usage Examples

### Manual Endpoint Calls

```bash
# Trigger wanted/missing validation
curl -X POST http://localhost:8080/Sonarr/Monitor/wanted
```

### Webhook Setup

Configure Sonarr webhook:
- URL: `http://apollarr:8080/Sonarr`
- Triggers: Select "On Series Add"
- Method: POST

## 🔧 Configuration

### Environment Variables (.env)
```env
SONARR_URL=https://sonarr.example.com
SONARR_API_KEY=your_api_key
APOLLO_USERNAME=your_username
APOLLO_PASSWORD=your_password
```

### Application Settings (appsettings.json)
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

## 📝 Logging

The system provides comprehensive logging:
- Info: Task start/completion, episode processing
- Debug: Detailed validation steps, idempotent skips
- Warning: Missing IMDb IDs, invalid links
- Error: Processing failures with full context

## 🧪 Testing Recommendations

1. **Test SeriesAdd Webhook**: Add a new series in Sonarr, verify all monitoring is disabled
2. **Test Missing Tag**: Manually call endpoint with invalid series to verify missing tag creation
3. **Test Valid Link**: Call endpoint with valid series to verify `.strm` creation and unmonitoring
4. **Test Idempotency**: Run same endpoint twice, verify no duplicate operations
5. **Test Scheduled Tasks**: Temporarily set schedule times to near-future to verify execution
6. **Test Tag Filtering**: Add missing tag to episodes, verify `?tag=missing` filters correctly

## 🎯 Requirements Compliance

| Requirement | Status | Implementation |
|-------------|--------|----------------|
| SeriesAdd unmonitors everything | ✅ | `HandleSeriesAddEventAsync` in SonarrController |
| Episode validation logic | ✅ | `ProcessEpisodeForMonitoringAsync` in StrmFileService |
| Missing tag management | ✅ | Tag methods in SonarrService |
| Delete .strm on invalid link | ✅ | Implemented in episode validation |
| Idempotent operations | ✅ | Check-before-action pattern throughout |
| Weekly full validation | ✅ | `ValidationSchedulerService` |
| Daily missing validation | ✅ | `ValidationSchedulerService` with tag filter |
| `/Sonarr/Monitor/episodes` | ✅ | SonarrController with tag filtering |
| `/Sonarr/Monitor/seasons` | ✅ | SonarrController |
| `/Sonarr/Monitor/series` | ✅ | SonarrController |
| Safe file operations | ✅ | Validation before deletion |
| Self-healing | ✅ | Daily missing validation restores valid streams |

## 🔄 Workflow Summary

### New Series Added
1. Sonarr sends webhook → Apollarr
2. Apollarr unmonitors series, all seasons, all episodes
3. **Apollarr immediately validates all episodes and creates .strm files**
4. Episodes with valid links get .strm files created
5. Episodes without valid links are tagged as `missing`
6. No need to wait for scheduled validation!

### Daily (2 AM)
1. Scheduler triggers missing validation
2. Processes only episodes tagged `missing`
3. If stream now available: creates `.strm`, removes tag, unmonitors
4. If still missing: keeps tag, no changes

### Weekly (Sunday 3 AM)
1. Scheduler triggers full validation
2. Processes ALL episodes
3. Validates all existing `.strm` files
4. Replaces any newly downloaded files with `.strm`
5. Tags new missing episodes
6. Unmonitors episodes with valid streams

## 📚 Additional Notes

- All operations are logged for audit trail
- Background service starts automatically with application
- Schedules can be disabled via configuration
- Tag-based filtering enables targeted processing
- System designed for large libraries (processes series sequentially to avoid overwhelming Sonarr API)
