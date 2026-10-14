# Starlink tracking

`Ping Starlink` and `Ping ISS` open independent panels, initially hidden without visitor data requests. Each panel has an X button; closing cancels visitor requests and stops rendering/propagation without canceling a shared server refresh. `Ping ISS` alone opens the scrolling LED ticker and a compact floating live-video preview. Video stops after five seconds of actual playback, excluding buffering; its own X closes it immediately.

## Data and counts

- Orbital elements: https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=JSON
- Catalog: https://celestrak.org/satcat/records.php?NAME=STARLINK&FORMAT=JSON
- Operational status definitions: https://celestrak.org/satcat/status.php

Only Starlink payloads with Earth as their orbit center, `ORB` orbit type and no decay date count as in orbit. **Operational** is strictly `OPS_STATUS_CODE = +`. `P` is shown separately. Active (`+, P, B, S, X`) is broader than operational and is not substituted for it. Catalog status is an external classification, not a guarantee a satellite is currently serving internet customers.

Counts include cataloged satellites without available GP data. Blue denotes operational (+), gold partially operational (P), red nonoperational (-), and gray standby/spare/other statuses. The plotted count reports usable positions across all in-orbit statuses against the complete catalog count. Failed propagation is omitted, never replaced with synthetic locations. Objects without cataloged identities are outside this count.

## Position calculation

Satellite.js 7.1.0 (MIT, vendored in `wwwroot/lib/satellite-js`) consumes JSON OMM elements, including six-digit IDs, and uses SGP4. A module worker propagates satellites every second at real-time speed (1×), or every 250 ms in the explicitly labeled 30× simulation (the default for visible motion). Motion starts enabled, as requested by the owner; the motion checkbox freezes/resumes positions. Earth-fixed position/velocity interpolation smooths motion; this never accelerates upstream downloads. Earth occludes satellites behind it. Drag the globe horizontally to change the view. Hidden/closed panels stop propagation and animation. Real-time resume catches up to wall time; simulation preserves its paused clock.

Zoom grows the complete square canvas in a responsive, scrollable viewport. Both poles and horizontal edges remain reachable with scrollbars, keyboard navigation or touch scrolling. Ordinary wheel input scrolls; Ctrl/Cmd + wheel zooms. Reset restores the scale, orientation and center. Internal canvas resolution is capped at 2560 pixels per dimension to bound memory use at 3× zoom on high-density screens.

Each manual Ping Starlink click starts the full locally hosted modem recording (approximately 28.6 seconds) at 35% volume, with no sound toggle. Closing the panel or hiding/leaving the page stops playback. There is no audio on page load or background refresh. See [media credits](media-credits.md). Indeterminate feedback distinguishes the request from orbit calculation; no invented completion percentage is shown. Failed refreshes retain the last moving view and explicitly identify the failed source check.

These are predicted positions from published elements, not direct satellite telemetry. Retrieval health is separate from individual orbital age: the retrieval badge ages after four hours or reports a failed source check. Successfully propagated positions are counted separately by epoch, using the existing 3.5-day threshold. A small set of old observations does not imply a failed retrieval. Dim rings preserve each satellite's status color while identifying old or unknown epochs. The optional older-position filter starts enabled and never changes catalog totals. UTC/offset epochs are normalized before propagation, and epoch metadata stays aligned when unsupported/invalid positions are skipped. Age uses wall time, independently of the simulated clock, and refreshes while motion is paused.

On mobile the two main counts, retrieval/orbital health, zoom and globe precede the collapsible statistics/layer controls. Data provenance and instructions remain available in a second disclosure. Opening focuses the relevant panel; Escape closes the focused or last-opened tracker and X returns focus to its Ping button. The globe's main-thread drawing targets 30 fps while worker propagation keeps its existing cadence. Reduced-motion preferences pause decorative backgrounds; requested satellite motion remains on by default.

## Caching and deployment

All users share one singleton cache. CelesTrak GP updates at two-hour intervals and enforces one download per update; repeated pings reuse the cached data. The snapshot is atomically persisted to `App_Data/starlink-status.json` and restored on restart. An unsuccessful refresh preserves the last validated snapshot and waits two hours before another attempt, also protecting against re-downloading one successful half of a failed two-source refresh. With no saved snapshot the endpoint returns 503 rather than an invented count. There is no upstream request before the first Starlink ping.

The public `GET /Home/StarlinkData` JSON response negotiates Brotli or gzip at the fastest compression level to reduce the transfer size of the full orbital dataset. Clients may decline compression; the decoded JSON and `no-store` policy remain identical. Compression is deliberately restricted to this fixed public route. HTML, per-request security tokens and other API responses are excluded from this HTTPS compression policy.

Production should use one app replica and a persistent `/app/App_Data` volume, as with NASA station synchronization. Multiple independent replicas require a shared cache/refresh coordinator. Local cached data and test downloads are excluded from publish and Git.

Snapshots now contain catalog-derived `PETABIT_STATUS` on each orbital element and `NonOperationalCount`, `OtherCount`, `StatusCoverageComplete`. Older operational-only snapshots remain readable: the UI shows an unknown nonoperational count rather than zero, until the next scheduled two-hour refresh provides the complete classification. Migration does not bypass the source download cooldown.

The page emits content hashes for the worker, orbital module and clock. The worker URL carries each dependency hash and uses it for module imports, so returning visitors cannot combine a new dashboard with cached old frame formats or clocks. A missing status array is handled defensively.

## Validation

`dotnet test Petabit.sln --configuration Release` covers catalog classification, invalid data, six-digit IDs, deduplication, persisted cache/restart and failure retention. `node --test Petabit.Tests/*.test.cjs Petabit.Tests/*.test.mjs` covers real-time propagation, Earth-fixed velocity interpolation and invalid-orbit omission, plus ISS preview playback.

Vendored npm tarball SHA-256: `B812DA8AF116EA123F9D3763CE2FBCF7A0559C6DD1AF25B0AC9DA567CCF2A1B5`. No browser CDN or WebAssembly dependency is needed.
