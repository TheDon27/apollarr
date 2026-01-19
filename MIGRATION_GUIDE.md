# Migration Guide

## Overview
This guide helps you understand the changes made during the standardization and optimization effort.

## Breaking Changes

### None!
All changes are backward compatible. Existing environment variable configuration continues to work.

## New Features

### 1. Optional JSON Configuration
You can now optionally configure the application via `appsettings.json`:

```json
{
  "AppSettings": {
    "Sonarr": {
      "Url": "http://sonarr:8989",
      "ApiKey": "your-api-key",
      "MaxRetries": 5,
      "RetryDelays": [2000, 3000, 5000, 8000, 10000]
    },
    "Apollo": {
      "Username": "your-username",
      "Password": "your-password"
    },
    "Strm": {
      "StreamUrlTemplate": "https://starlite.best/api/stream/{username}/{password}/tvshow/{imdbId}/{season}/{episode}",
      "ValidateUrls": true,
      "ValidationTimeoutSeconds": 10
    }
  }
}
```

### 2. Configurable Retry Behavior
You can now customize retry delays and max retries for Sonarr API calls.

### 3. Configurable Stream Validation
- Toggle URL validation on/off via `Strm.ValidateUrls`
- Set validation timeout via `Strm.ValidationTimeoutSeconds`

### 4. Custom Stream URL Template
Change the streaming service by modifying `Strm.StreamUrlTemplate`.

## Code Changes for Developers

### If You Were Extending SonarrService

**Before:**
```csharp
public SonarrService(HttpClient httpClient, IConfiguration configuration, ILogger<SonarrService> logger)
{
    _httpClient = httpClient;
    _logger = logger;
    _sonarrUrl = configuration["SONARR_URL"] ?? throw new InvalidOperationException("SONARR_URL not configured");
    _sonarrApiKey = configuration["SONARR_API_KEY"] ?? throw new InvalidOperationException("SONARR_API_KEY not configured");
}
```

**After:**
```csharp
public SonarrService(
    HttpClient httpClient,
    IOptions<AppSettings> appSettings,
    ILogger<SonarrService> logger)
{
    _httpClient = httpClient;
    _logger = logger;
    _settings = appSettings.Value.Sonarr;
}
```

### If You Were Extending StrmFileService

**Before:**
```csharp
public StrmFileService(ILogger<StrmFileService> logger, IConfiguration configuration, HttpClient httpClient)
```

**After:**
```csharp
public StrmFileService(
    ILogger<StrmFileService> logger,
    HttpClient httpClient,
    IFileSystemService fileSystem,
    IOptions<AppSettings> appSettings)
```

### If You Were Testing File Operations

**Before:**
```csharp
// Direct file system access - hard to test
await File.WriteAllTextAsync(filePath, streamUrl);
```

**After:**
```csharp
// Mockable interface
await _fileSystem.WriteAllTextAsync(filePath, streamUrl);
```

**Test Example:**
```csharp
var mockFileSystem = new Mock<IFileSystemService>();
mockFileSystem.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>()))
    .Returns(Task.CompletedTask);

var service = new StrmFileService(logger, httpClient, mockFileSystem.Object, options);
```

## New Files Added

1. `Common/RetryPolicy.cs` - Centralized retry logic
2. `Common/AppSettings.cs` - Strongly-typed configuration
3. `Models/ApiResponses.cs` - Response DTOs
4. `Services/IFileSystemService.cs` - File system abstraction
5. `OPTIMIZATIONS.md` - Detailed optimization documentation
6. `MIGRATION_GUIDE.md` - This file

## Deployment Notes

### Docker
No changes required. Environment variables continue to work as before.

### Configuration Priority
1. Environment variables (highest priority)
2. appsettings.json
3. Default values in code

### Recommended Steps
1. Pull latest code
2. Run `dotnet build` to verify
3. Run existing tests (if any)
4. Deploy as usual
5. Monitor logs for any issues

## Rollback Plan

If issues arise:
1. Revert to previous commit
2. Or, ensure environment variables are set (they override all other config)

## Support

For issues or questions:
1. Check `OPTIMIZATIONS.md` for detailed changes
2. Review code comments
3. Check logs for configuration errors
