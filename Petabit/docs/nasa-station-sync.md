# NASA station synchronization

The application checks NASA's station blog RSS and the dated configuration caption on the Station Overview page on startup and every 120 seconds. It does not use the potentially outdated `TODAY` counters. The browser reads station status and orbital position independently every 30 seconds; no browser needs to be open for NASA synchronization to run.

## Configuration and Railway storage

`StationSync:Enabled` defaults to `true`. `StationSync:PollSeconds` defaults to 120 and is bounded to 60–3600 seconds. `StationSync:StoragePath` defaults to `App_Data/station-status.json`, relative to the application's content root. Environment variables use double underscores, e.g. `StationSync__PollSeconds=120`.

The Docker image creates `/app/App_Data` with write access for its non-root runtime user. Configure a **Railway persistent volume mounted at `/app/App_Data`** to preserve snapshots between deployments/container replacements. Without that volume, the local container file can be lost when Railway replaces the container. The environment variable `StationSync__StoragePath` can point to a different writable persistent mount. Run a single application replica with this file store; multiple replicas need a shared database and a single synchronization worker.

NASA data is written to a temporary file and then atomically replaces the saved snapshot. A successful network check with failed disk storage remains available in memory and emits a persistence warning. Do not put the snapshot under `wwwroot`. `App_Data` is excluded from source control. The initial offline snapshot was manually confirmed from NASA's October 1 configuration and October 2 crew report; it is clearly marked unverified until a live check succeeds.

## Rules and limits

- Completed docking updates spacecraft. Hatch entry updates crew separately.
- Completed undocking removes the identified vehicle and the crew assigned to that mission.
- Future plans, launch coverage, and departure announcements remain news and do not change station state.
- NASA headlines are kept in English with a direct source link. Displayed event times are publication times in the browser's timezone, not inferred physical event times. Date-only configuration observations are displayed as dates.
- Supported vehicle identifiers currently include Crew-N Dragon, CRS-N Dragon, Soyuz MS-N, Progress N / MS-N, and Cygnus / Cygnus XL. New families and ambiguous wording require review. A Cygnus without a numbered identifier represents the single named Cygnus in the dated configuration.
- Crew entry requires agency-prefixed names. ESA does not identify a nationality: it remains blank unless already known from the verified roster. No nationality is guessed.
- An incomplete configuration, malformed XML, unsupported event or missing RSS continuity preserves the last valid data. A successful HTTP response only confirms the feed was checked; it does not mean the crew changed recently.
- A date-only diagram cannot undo a newer timestamped event. A changed diagram without a confirmed crew departure flags review rather than inventing a crew change.
- RSS covers a finite recent window. Extended downtime may require manual reconciliation. `NeedsReview` stays set until an operator has checked and reconciled the saved roster.
- HTTP conditional requests use ETag/Last-Modified where NASA provides them. Polling latency is normally up to 120 seconds plus the browser's next 30-second refresh, **after NASA publishes the confirmation**.

## Review and recovery

`/Home/StationData` exposes the crew, vehicles, source links, individual confirmation dates, `stationStatusCheckedAt`, and stale/review/refresh/persistence flags. It remains available when the independent position provider is down. This endpoint never triggers a NASA download.

For a review: inspect the NASA source and runtime logs, stop the worker/application, back up the saved JSON, reconcile the affected crew/vehicles and their source/confirmation dates, and only then clear `NeedsReview`. Restart the application and confirm `/Home/StationData` reports a recent check. Do not clear review merely to hide the warning. If NASA changes its wording or HTML structure, update the parser with a regression fixture before resuming automatic interpretation.

The hourly GitHub workflow now checks production synchronization instead of expiring the static offline bootstrap. It requires the updated application to be deployed. It fails for stale data, pending review, refresh errors or failed persistence.

## Validation

Parser tests cover dated configurations, shared vehicle numbering, unknown vehicles, forecasts, docking versus crew entry, mission-specific undocking, agency/name extraction, XML safety and source URLs. Synchronizer tests cover persistence/restart/deduplication, upstream failures, changed HTML, invalid saved JSON and unwritable storage. Integration tests check the station endpoint when the position API fails. Tests use fixed NASA fixtures without making network requests.

## Manual Ping: LED and live camera

The green Matrix header is retained. The LED report and video panel appear only after a manual `Ping ISS`; scheduled data refreshes update existing reports without reopening video. The LED includes position, speed, crew, vehicles and NASA freshness. The requested horizontal ticker stays animated, with speed adjusted to the text length. The compact video panel floats in the bottom-right corner.

`/Home/VideoSource` reads the camera embed from NASA's official `https://eol.jsc.nasa.gov/ESRS/HDEV/` page, caches the identifier for 15 minutes, and rejects unexpected hosts or malformed IDs. YouTube's API and privacy-enhanced iframe load only after manual Ping. The player is muted and pauses after five seconds of actual playback; connection and buffering time are excluded. A new Ping recreates the player for a new live view. If autoplay is blocked, the user can press Play inside the player. Source/player failures show a message and a link to the NASA source. This is a live preview, not a downloaded recording.

Browser media timing tests run without network dependencies:

```powershell
node --test Petabit.Tests/iss-video-preview.test.cjs
```
