# Security hardening

The application is a public, read-only dashboard with a protected language preference form. These controls preserve its existing ISS, video and Starlink behavior.

## Request and browser boundaries

- Kestrel accepts at most 16 KiB of request body and headers, 64 headers and a 15-second header reception window. Forms have explicit field, key and value limits. The application does not provide uploads.
- A shared 32-request concurrency limit bounds simultaneous application work. Per-client limits cover dynamic pages and static files (240 requests/minute), with an additional 10/minute limit on ISS-related data routes. Liveness remains available when these limits are reached. Readiness participates in the global limits and admits at most four simultaneous checks without queuing; probes share a 15-second upstream result.
- Allowed hosts, HTTPS, HSTS, secure cookies and antiforgery protection remain enforced. CSP allows only the existing required origins, explicitly restricts workers to this site and prohibits inline event handlers. COOP and CORP use `same-origin`; no cross-origin isolation/COEP is enabled because the YouTube preview requires embedding.
- Structured JSON metadata uses HTML-safe escaping, including when configuration contains script closing tags. ISS output caching ignores irrelevant query parameters to prevent arbitrary cache-busting requests from multiplying source downloads.

## Upstream data and saved state

- Fixed HTTPS upstream clients do not automatically follow redirects or accept cookies. Source URL changes therefore require explicit review. NASA responses are bounded to 2 MB, CelesTrak responses to 12 MB, and ISS JSON responses to 64 KiB. ISS has an explicit eight-second deadline covering headers and JSON body reads, in addition to its resilience pipeline.
- NASA video visitors share one refresh with a 15-second full-response deadline. A disconnected visitor cancels only their wait. Failed requests and unsupported embeds have a 30-second retry cooldown instead of triggering another upstream call for every visitor.
- A visitor disconnect cancels only their wait for Starlink data; the shared, time-bounded refresh continues. Failed refreshes retain the last validated data and keep the two-hour source cooldown.
- Starlink numeric JSON types, finite values, record counts, cache sizes and cached orbital schema/count/epoch consistency are checked. Oversized station caches are rejected. Bad source data must not replace the last validated snapshot.
- Unknown Starlink status values remain unknown/other; inherited JavaScript property names cannot become operational. The renderer propagates only supported near-Earth records, skipping deep-space calculations. Invalid bootstrap gzip data is rejected while valid saved/source data remains usable.
- Railway persists station and orbital snapshots in `/app/App_Data`; the web process runs as the unprivileged `app` user after volume ownership initialization.

## Continuous checks

`dotnet test Petabit.sln -c Release` verifies request rejection, CSP/nonce protection, hostile metadata escaping, cache-busting resistance, source failures, client disconnects, corrupt caches and saturated-service liveness alongside existing functional tests. Node tests cover orbital calculations and five seconds of actual video playback.

The Security workflow audits all NuGet dependencies, analyzes C# and JavaScript with CodeQL, and audits the exact `satellite.js` runtime package. NuGet audit rejects missing or unreachable advisory feeds as well as known vulnerabilities; Docker restore uses the same explicit `NuGet.Config` and strict audit. CI and security actions are pinned to reviewed commit IDs; repository settings enforce full SHA pinning. Checkouts do not retain GitHub credentials. Dependabot patch auto-merge also checks the event's exact head SHA. `security/frontend` is an audit-only npm manifest; it is excluded from the Docker build context. CI installs its locked package with lifecycle scripts disabled and compares the vendored distribution byte-for-byte with the integrity-verified npm package. Dependabot watches this manifest. When upgrading satellite.js, update both its vendored distribution and the audit manifest/lockfile.

Git ignores local environment/credential files and private key material. Docker context also excludes these files and local agent configurations, so local builds do not send them into the build stage. These exclusions do not remove previously tracked files or erase existing Git history.

Sources: [Kestrel limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0), [HTTP redirect behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect), [CodeQL workflow configuration](https://docs.github.com/en/code-security/reference/code-scanning/workflow-configuration-options).

## Operational limits

Rate limits reduce application-level resource abuse; distributed DDoS protection still belongs at the hosting ingress. Railway trusts one ingress hop because its proxy addresses are dynamic: do not expose the container directly or broaden forwarding trust for another hosting environment. Multiple replicas need shared rate limits and a shared Starlink refresh coordinator.

Data Protection keys currently live in the container's temporary directory, so language-form tokens issued before a deployment can require a reload after deployment. Persisting these keys should include a separately managed encryption-at-rest mechanism. GitHub and Railway account MFA, access reviews and secret rotation are account-owner responsibilities. Unused jQuery and legacy validation plugins have been removed, including the unreferenced validation partial; Bootstrap JavaScript is excluded from publication. A passing audit is not a penetration-test certification.

## Recovery and additional security gates (2026-10-09)

See [disaster recovery](disaster-recovery.md) for encrypted backup, key separation, Railway plan limitations, restore drills and incident response. `security/backup` is an operator tool excluded from the published image; it is not a web download or admin endpoint.

Archive validation rejects Windows reserved device names, the restore manifest's reserved directory, and file/directory collisions before creating any destination. Portable archive paths are limited to 4096 characters and 64 segments; the standard payload contains the Git bundle and application data files.

Production backups record the active Railway deployment's commit, not an assumed Git HEAD. Deployment identity and commit are checked before and after volume export; ambiguous metadata or a deployment transition rejects the export before upload. The recorded commit must be included in the Git bundle and is checked out during restore verification.

Security CI now scans complete committed history with Gitleaks, scans the built container's OS/runtime packages with Trivy, and probes a disposable application on an internal Docker network. Scanner images and GitHub actions are pinned to digests/commit IDs. Container scanning reads an exported image instead of receiving the Docker socket. All HIGH/CRITICAL findings block the container job, including unfixed findings; complete lower-severity results are retained for review. Security runs daily as well as on changes; Docker base-image updates are covered by Dependabot.

The private lab runs as an unprivileged user with a read-only root filesystem, writable disposable tmpfs only, dropped Linux capabilities and no new privileges. It has no production data, credentials, public ingress or Internet egress. Its single proxy address is trusted explicitly and sets forwarded headers for HTTPS behavior without weakening production cookie/CSP controls. The probes check actual form rejection, oversized requests, script reflection, private-file exposure and liveness. These checks and a passive ZAP baseline are useful coverage, not a full independent penetration-test certification.

GitHub's existing secret scanning and push protection were verified enabled, with zero open secret/dependency/CodeQL alerts at inspection. MFA status was not established by the available API; the account owner must verify it. Railway Under Attack Mode is available but left disabled during ordinary operation because its browser challenge can affect API consumers and monitoring.

The [13 October 2026 review](security-review-2026-10-13.md) records the new controls, validation and remaining operational limits.
