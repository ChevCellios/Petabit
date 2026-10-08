# Security hardening

The application is a public, read-only dashboard with a protected language preference form. These controls preserve its existing ISS, video and Starlink behavior.

## Request and browser boundaries

- Kestrel accepts at most 16 KiB of request body and headers, 64 headers and a 15-second header reception window. Forms have explicit field, key and value limits. The application does not provide uploads.
- A shared 32-request concurrency limit bounds simultaneous application work. Per-client limits cover dynamic pages and static files (240 requests/minute), with an additional 10/minute limit on ISS-related data routes. Health endpoints remain available when these limits are reached; readiness probes share a 15-second upstream result.
- Allowed hosts, HTTPS, HSTS, secure cookies and antiforgery protection remain enforced. CSP allows only the existing required origins, explicitly restricts workers to this site and prohibits inline event handlers. COOP and CORP use `same-origin`; no cross-origin isolation/COEP is enabled because the YouTube preview requires embedding.
- Structured JSON metadata uses HTML-safe escaping, including when configuration contains script closing tags. ISS output caching ignores irrelevant query parameters to prevent arbitrary cache-busting requests from multiplying source downloads.

## Upstream data and saved state

- Fixed HTTPS upstream clients do not automatically follow redirects or accept cookies. Source URL changes therefore require explicit review. NASA responses are bounded to 2 MB, CelesTrak responses to 12 MB, and ISS buffered responses to 64 KiB, with existing finite timeouts/resilience limits.
- A visitor disconnect cancels only their wait for Starlink data; the shared, time-bounded refresh continues. Failed refreshes retain the last validated data and keep the two-hour source cooldown.
- Starlink numeric JSON types, finite values, record counts, cache sizes and cached orbital schema/count/epoch consistency are checked. Oversized station caches are rejected. Bad source data must not replace the last validated snapshot.
- Railway persists station and orbital snapshots in `/app/App_Data`; the web process runs as the unprivileged `app` user after volume ownership initialization.

## Continuous checks

`dotnet test Petabit.sln -c Release` verifies request rejection, CSP/nonce protection, hostile metadata escaping, cache-busting resistance, source failures, client disconnects, corrupt caches and saturated-service liveness alongside existing functional tests. Node tests cover orbital calculations and five seconds of actual video playback.

The Security workflow audits all NuGet dependencies, analyzes C# and JavaScript with CodeQL, and audits the exact `satellite.js` runtime package. CI and security actions are pinned to reviewed commit IDs and checkouts do not retain GitHub credentials. `security/frontend` is an audit-only npm manifest; it is excluded from the Docker build context. CI installs its locked package with lifecycle scripts disabled and compares the vendored distribution byte-for-byte with the integrity-verified npm package. Dependabot watches this manifest. When upgrading satellite.js, update both its vendored distribution and the audit manifest/lockfile.

Sources: [Kestrel limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0), [HTTP redirect behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect), [CodeQL workflow configuration](https://docs.github.com/en/code-security/reference/code-scanning/workflow-configuration-options).

## Operational limits

Rate limits reduce application-level resource abuse; distributed DDoS protection still belongs at the hosting ingress. Railway trusts one ingress hop because its proxy addresses are dynamic: do not expose the container directly or broaden forwarding trust for another hosting environment. Multiple replicas need shared rate limits and a shared Starlink refresh coordinator.

Data Protection keys currently live in the container's temporary directory, so language-form tokens issued before a deployment can require a reload after deployment. Persisting these keys should include a separately managed encryption-at-rest mechanism. GitHub and Railway account MFA, access reviews and secret rotation are account-owner responsibilities. Older unused jQuery/validation/Bootstrap JavaScript files are still in the asset tree; review or remove them before adding code that loads them. A passing audit is not a penetration-test certification.
