#!/usr/bin/env bash
set -euo pipefail
tool_dir="$RUNNER_TEMP/petabit-tools"
mkdir -m 700 "$tool_dir"
cd "$tool_dir"
curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 --retry 3 --max-time 180 -o railway.tar.gz https://github.com/railwayapp/cli/releases/download/v5.62.1/railway-v5.62.1-x86_64-unknown-linux-gnu.tar.gz
echo '80fedf59bea027031e18affe1e646255a79839b9b8675f2513aa678d2b6afb06  railway.tar.gz' | sha256sum --check --strict
tar -xzf railway.tar.gz
curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 --retry 3 --max-time 180 -o rclone.zip https://github.com/rclone/rclone/releases/download/v1.75.2/rclone-v1.75.2-linux-amd64.zip
echo '349ac8fba6ff65d6247043f1750cdcb518ec5d500ef91463a10d37c0ccdf3702  rclone.zip' | sha256sum --check --strict
unzip -q rclone.zip
install -m 755 rclone-v1.75.2-linux-amd64/rclone "$tool_dir/rclone"
"$tool_dir/railway" --version
"$tool_dir/rclone" version
echo "$tool_dir" >> "$GITHUB_PATH"
