#!/usr/bin/env bash
set -euo pipefail

: "${SITE_URL:?SITE_URL is required}"
: "${EXPECTED_SHA:?EXPECTED_SHA is required}"
if [[ ! "$EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Expected commit must be a full lowercase Git SHA."
  exit 1
fi

for attempt in $(seq 1 "${MAX_ATTEMPTS:-8}"); do
  status_code="$(curl --silent --output /dev/null --write-out "%{http_code}" --connect-timeout 5 --max-time 15 \
    "$SITE_URL/health/live" || true)"
  version_response=""
  if [ "$status_code" = "200" ]; then
    version_response="$(curl --fail --silent --show-error --connect-timeout 5 --max-time 15 \
    "$SITE_URL/version" || true)"
  fi
  if jq --slurp --exit-status --arg sha "$EXPECTED_SHA" \
    'length == 1 and (.[0] | type == "object" and .commitSha == $sha)' <<< "$version_response" > /dev/null 2>&1; then
    echo "Production serves expected commit $EXPECTED_SHA on attempt $attempt."
    exit 0
  fi
  echo "Production has not reported the expected commit on attempt $attempt."
  if [ "$attempt" -lt "${MAX_ATTEMPTS:-8}" ]; then
    retry_seconds="${RETRY_SECONDS:-30}"
    if [ "$status_code" = "429" ]; then retry_seconds="${RETRY_SECONDS:-60}"; fi
    sleep "$retry_seconds"
  fi
done

echo "Production did not report the expected commit within the retry window."
exit 1
