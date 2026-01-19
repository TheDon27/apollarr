# Quick Start: Missing Episodes Feature

## What Does It Do?

Automatically checks for **monitored** missing episodes every hour and retries creating .strm files for them. This is useful when:
- Content wasn't available when the series was first added
- Stream validation failed initially but content is now available
- Episodes were added to Sonarr but .strm file creation failed

**Important:** Only processes episodes marked as "monitored" in Sonarr. Unmonitored episodes are automatically skipped.

## Enable/Disable

### Default Behavior
✅ **Enabled by default** - The service starts automatically when the application runs.

### To Disable
Add to `appsettings.json`:
```json
{
  "AppSettings": {
    "MissingEpisode": {
      "Enabled": false
    }
  }
}
```

## Configuration

### Change Check Interval
Default is every 1 hour. To change:

```json
{
  "AppSettings": {
    "MissingEpisode": {
      "Enabled": true,
      "CheckIntervalHours": 2
    }
  }
}
```

Common intervals:
- `0.5` = Every 30 minutes
- `1` = Every hour (default)
- `2` = Every 2 hours
- `6` = Every 6 hours
- `24` = Once per day

## Manual Trigger

Test or trigger immediately without waiting:

```bash
curl -X POST http://localhost:8080/MissingEpisodes/check
```

## Monitoring

### Check Logs
Look for these log messages:

**Service Started:**
```
Missing Episode Background Service started. Will check every 01:00:00
```

**During Check:**
```
Running scheduled missing episode check
Found 42 missing episodes
Missing episodes span 5 series
Processing 8 missing episodes for series ID 123
```

**Completion:**
```
Scheduled missing episode check completed. Next check in 01:00:00
Completed missing episode check. Processed: 5, Errors: 0
```

### No Missing Episodes
If you see:
```
No missing episodes found
```
This is normal! It means all episodes have files.

## How It Works

```
┌─────────────────────────────────────────┐
│  Application Starts                     │
└───────────────┬─────────────────────────┘
                ↓
┌─────────────────────────────────────────┐
│  Wait Until Next Hour Mark              │
│  (e.g., if started at 3:45, waits       │
│   until 4:00)                           │
└───────────────┬─────────────────────────┘
                ↓
┌─────────────────────────────────────────┐
│  Query Sonarr:                          │
│  /api/v3/wanted/missing?monitored=true  │
└───────────────┬─────────────────────────┘
                ↓
┌─────────────────────────────────────────┐
│  Filter: Only monitored=true episodes   │
└───────────────┬─────────────────────────┘
                ↓
┌─────────────────────────────────────────┐
│  For Each Missing Episode:              │
│  1. Fetch series details                │
│  2. Validate stream URL                 │
│  3. Create .strm file                   │
└───────────────┬─────────────────────────┘
                ↓
┌─────────────────────────────────────────┐
│  Wait 1 Hour (or configured interval)   │
└───────────────┬─────────────────────────┘
                ↓
              Repeat
```

## Common Questions

### Q: Will it process episodes multiple times?
**A:** No. Once a .strm file is created successfully, Sonarr marks the episode as having a file, so it won't appear in the missing list anymore.

### Q: What if I don't want validation?
**A:** Disable it in configuration:
```json
{
  "AppSettings": {
    "Strm": {
      "ValidateUrls": false
    }
  }
}
```

### Q: Can I run it more frequently?
**A:** Yes, set `CheckIntervalHours` to a smaller value like `0.5` for 30-minute checks.

### Q: Does it affect webhook processing?
**A:** No, it runs independently. Webhooks still work in real-time.

### Q: What happens if the service is down during a scheduled check?
**A:** The next check will run at the next scheduled interval after the service restarts.

## Troubleshooting

### Service Not Running
Check logs for:
```
Missing Episode Background Service is disabled
```
**Solution:** Set `MissingEpisode.Enabled` to `true`

### High CPU/Memory Usage
**Solution:** Increase `CheckIntervalHours` to reduce frequency

### Episodes Not Being Created
Check logs for:
- "does not have an IMDb ID" - Series needs IMDb ID in Sonarr
- "Stream URL is not valid" - Content not available yet
- File permission errors - Check media directory permissions

## Best Practices

1. **Start with defaults** - 1 hour interval works well for most cases
2. **Monitor logs initially** - Watch the first few runs to ensure it's working
3. **Use manual trigger for testing** - Test before relying on scheduled runs
4. **Adjust interval based on needs** - More frequent if you add series often
5. **Keep validation enabled** - Prevents creating files for unavailable content

## Integration with Docker

No special configuration needed! The service runs automatically in the container.

```bash
docker run -p 8080:8080 \
  -v /path/to/media:/media \
  --env-file .env \
  apollarr
```

The service will start and begin checking on the hour.

## Next Steps

- Read [MISSING_EPISODES_FEATURE.md](MISSING_EPISODES_FEATURE.md) for detailed documentation
- Check [OPTIMIZATIONS.md](OPTIMIZATIONS.md) for code architecture details
- Review [MIGRATION_GUIDE.md](MIGRATION_GUIDE.md) if upgrading from an older version
