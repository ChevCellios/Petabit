# Petabit runtime hardening, 13 October 2026

The previous Ubuntu runtime scan reported 14 OS package findings (nine MEDIUM,
five LOW), with no fixed Ubuntu package versions listed. The application does not
need the full distribution's login, PAM, systemd, PCRE2 or GNU tar tools. Instead
of removing essential packages from Ubuntu, the Dockerfile uses Microsoft's
supported .NET 10 Alpine 3.24 images for both build and runtime.

The runtime uses the `extra` variant: full ICU data, time zones and CA certificates
remain available for Croatian, German and English localization and HTTPS data
sources. The Alpine release is explicit; .NET patch updates and signed package
updates within that release remain available. `apk upgrade --no-cache` applies
published distribution fixes during each build, followed by installation of the
small `su-exec` privilege-switching package. Package inventories and vulnerability
reports remain intact; no CVE or unfixed finding is suppressed.

Railway currently starts the container with `RAILWAY_RUN_UID=0` because its volume
is mounted as root. The entrypoint initializes only `/app/App_Data`, changes
ownership without following symbolic links, and immediately replaces itself with
`su-exec` and then the .NET process as UID/GID 1654. It does not launch a root web
server. The application files remain owned by root. Normal Docker startup and the
recovery drill's arbitrary non-root UID bypass this initialization. Railway's
dynamic `PORT` setting still determines the listening port.

Chiseled-extra was considered. Its lack of a shell and ownership tools conflicts
with this existing Railway initialization. Introducing a custom privileged native
launcher would create additional maintenance and security responsibilities, so
the supported Alpine runtime is the smaller change for this project.

The Security workflow scans the complete final image, including application
dependencies. All HIGH/CRITICAL findings still fail the release, including
unfixed findings; the complete report retains lower-severity findings. A clean
scan records currently known advisories, not an assurance against all attacks.
Daily scans and rebuilds remain necessary. Building an updated image does not
replace the already running Railway deployment.

Runtime regression checks run in the private Docker lab alongside the existing
HTTP boundary probes. A production release also requires the existing protected
CI checks, normal public functional checks and a complete backup/restore drill
against the deployed revision. Backups use the Railway SFTP volume-files command,
not GNU tar in the application container.

Sources: [Microsoft image variants](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images),
[official Alpine extra Dockerfile](https://github.com/dotnet/dotnet-docker/blob/main/src/runtime-deps/10.0/alpine3.24-extra/amd64/Dockerfile),
[Railway volume permissions](https://docs.railway.com/volumes#permissions),
[su-exec implementation](https://github.com/ncopa/su-exec/blob/master/su-exec.c),
[Railway CLI SFTP implementation](https://github.com/railwayapp/cli/blob/v5.62.1/src/commands/volume/sftp.rs).
