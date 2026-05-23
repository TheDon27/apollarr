# CLAUDE.md

Guidance for working in this repository.

## What this is

Apollarr is a .NET 8 ASP.NET Core Web API that integrates with **Sonarr** (TV) and **Radarr** (movies) to automatically generate `.strm` files pointing at an external streaming provider. When media is added or is "wanted/missing", Apollarr validates that a stream link exists, writes a `.strm` file into the media library path, and triggers the *arr app to rescan and import it.

The default stream URL template targets `starlite.best`. Credentials are supplied via `APOLLO_*` env vars and substituted into the template at runtime.

## Build / run / test

```bash
dotnet build              # build the solution (Apollarr.sln)
dotnet run                # runs on http://localhost:8080 (Kestrel listens on all interfaces, port 8080)
dotnet test               # runs the xUnit suite in tests/Apollarr.Tests
```

- Tests live in [tests/Apollarr.Tests/](tests/Apollarr.Tests/) (xUnit + Moq). They cover `ValidateStreamUrlAsync`, `SanitizeFileName`/`GetEpisodeTitle`, `AppSettingsValidator`, and the Sonarr/Radarr webhook orchestration. The test project is excluded from the web app's compile globs via `DefaultItemExcludes` in [Apollarr.csproj](Apollarr.csproj).
- Swagger UI (`/swagger`) and the OpenAPI doc (`/openapi/v1.json`) are only mapped in the Development environment.
- `.env` is loaded at startup via `DotNetEnv` before configuration binding. Required: `APOLLO_USERNAME`, `APOLLO_PASSWORD`, `SONARR_URL`, `SONARR_API_KEY`, `RADARR_URL`, `RADARR_API_KEY`.

## Architecture

Request/orchestration flow is layered so controllers stay thin:

- **Controllers** ([Controllers/](Controllers/)) — `SonarrController`, `RadarrController`. Each exposes `POST /{controller}` (webhook) and `POST /{controller}/monitor/wanted`. They validate the payload and delegate to a webhook service.
- **Webhook services** — `SonarrWebhookService`, `RadarrWebhookService`. Orchestrate the per-event workflow (e.g. `seriesAdd`, `movieAdd`).
- **Domain services** — `SonarrService` / `RadarrService` wrap the *arr REST APIs. Each is backed by a typed `HttpClient` (`SonarrApiClient` / `RadarrApiClient`) configured in [Program.cs](Program.cs) with pooled `SocketsHttpHandler` connections.
- **[StrmFileService](Services/StrmFileService.cs)** — the core logic. Builds the stream URL, validates it, creates/deletes `.strm` files, and decides monitoring state. Handles **both** TV episodes and movies. `IRadarrService` is injected as optional (`= null`); movie methods throw `InvalidOperationException("Radarr service is not configured")` if it's absent. Per-item link validation runs **bounded-parallel** (`Strm.MaxConcurrentValidations`, default 8) via the `ForEachConcurrentAsync` helper; counters are aggregated with `Interlocked`.
- **[IValidationCache](Services/IValidationCache.cs)** — caches **positive** stream-link validations (keyed by a SHA-256 of the URL, so credentials never land in the cache) for `ValidationCache.ValidTtlHours` (default 12h), so unchanged links skip the HEAD request on later passes. Negative results are never cached. Backed by `MemoryValidationCache` (default) or `RedisValidationCache` (`IDistributedCache`), selected by `ValidationCache.Provider` in [Program.cs](Program.cs); `NullValidationCache` when disabled. Monitoring paths validate through `ValidateLinkCachedAsync`; `ValidateStreamUrlAsync` remains the raw HEAD.
- **[IFileSystemService](Services/IFileSystemService.cs)** — file-system abstraction (singleton) so file operations are mockable/testable.
- **[ValidationSchedulerService](Services/ValidationSchedulerService.cs)** — a `BackgroundService`. Runs wanted/missing checks on startup and on an interval (default 15 min), plus a periodic "series monitoring" sweep that fires at `HourlySeriesMonitoringMinute` but only once `SeriesMonitoringIntervalHours` (default **24h** — daily; set to 1 for the old hourly cadence) has elapsed. TV and movie wanted/missing checks run concurrently; movie checks are skipped gracefully if Radarr isn't configured.
- **[Common/](Common/)** — `AppSettings` (strongly-typed config, bound from the `AppSettings` config section + `PostConfigure` overrides from legacy env-var names), `RetryPolicy` (static retry helper; does **not** retry on 401/403), and middleware: `CorrelationIdMiddleware` (adds/echoes `X-Correlation-ID`) and `ExceptionHandlingMiddleware` (RFC 7807 `problem+json`).

## Key behaviors to preserve

- **Stream validation**: `ValidateStreamUrlAsync` issues an HTTP `HEAD` request and treats a redirect to `error.starlite.best` as invalid, even on a 2xx. Keep this redirect check.
- **Movie vs TV URL shape**: movie stream URLs are derived from the TV template by replacing `/tvshow/` → `/movie/` and stripping the `{season}`/`{episode}` segments. See `ProcessMovieForMonitoringAsync` / `ProcessMovieAsync`.
- **Idempotency**: every `.strm` write checks for an existing file first. Rescans are only triggered when a new file was actually created — this now holds for the periodic series sweep too (`ProcessSeriesMonitoringAsync` rescans a series only when `SeriesValidationResult.StrmFilesCreated > 0`). The `seriesAdd` webhook still rescans unconditionally (one-time, fresh add). Preserve this — rescans are expensive on the *arr side.
- **Cleanup on invalid link**: if a link is no longer valid, the existing `.strm` is deleted. Existing `.strm` files are never purged by the "delete other artifacts" sweep (`DeleteExistingEpisodeFilesAsync`).
- **`seriesAdd` flow**: monitors series + all regular seasons (specials/season 0 off), validates every episode, writes `.strm` files, then rescans.
- **`movieAdd` flow**: sets the movie to monitored + **SDTV** quality profile, validates the link, deletes existing movie files, writes the `.strm`, sets the movie **unmonitored**, then rescans.

## Gotchas

- Nested settings validation is handled by [AppSettingsValidator](Common/AppSettingsValidator.cs), registered as an `IValidateOptions<AppSettings>` and forced at startup via `ValidateOnStart()`. Plain `ValidateDataAnnotations()` does **not** recurse into the nested settings, so the validator runs them explicitly — missing/invalid Sonarr, Radarr, Apollo, Strm, Scheduling, or ValidationCache values now fail fast at startup (including a missing `RedisConnectionString` when `Provider` is `Redis`).
- The Dockerfile and the project both target .NET 8 — keep them in sync if you bump the TFM.

## Conventions

- Nullable reference types and implicit usings are enabled.
- Services depend on interfaces (`ISonarrService`, `IStrmFileService`, etc.), not concrete types.
- All service methods take a `CancellationToken` and thread it through; honor cancellation in any new long-running loops.
- API responses use the records in [Models/ApiResponses.cs](Models/ApiResponses.cs) (`WebhookResponse`, `MonitorWantedResponse`, etc.). Errors go through `ExceptionHandlingMiddleware` as problem details.
