#!/bin/sh
set -eu

# Railway mounts persistent volumes as root. Initialize only this application's
# data directory, then run the web process as the image's unprivileged app user.
if [ "$(id -u)" = "0" ]; then
    mkdir -p /app/App_Data
    chown -R "$APP_UID:$APP_UID" /app/App_Data
    exec runuser -u app -- sh -c 'exec dotnet Petabit.dll --urls "http://0.0.0.0:${PORT:-3000}"'
fi

exec dotnet Petabit.dll --urls "http://0.0.0.0:${PORT:-3000}"
