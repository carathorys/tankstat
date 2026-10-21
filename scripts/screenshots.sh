#!/usr/bin/env bash
# Takes the README's screenshots: starts the published build (out/) on a throwaway port with sign-in off and the seeder's data, runs
# src/frontend/tankstat-react/e2e/screenshots.ts against it (playwright.screenshots.config.ts: phone and desktop, dark and light) into
# docs/screenshots, always cleans up. Playwright runs as scripts/test-e2e.sh runs it: natively with E2E_NATIVE=1, otherwise in the
# Playwright image of the installed version. Arguments go to `playwright test`: scripts/screenshots.sh --project desktop-dark
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
if [[ ! -f out/Tankstat.Api.dll ]]; then
  echo "No build in out/: run 'mise run build' first (mise run screenshots does)." >&2
  exit 1
fi

port="${E2E_PORT:-5098}"
export E2E_URL="http://127.0.0.1:${port}"

# Nothing of the developer's own setup: the mode, the database, the folders, the schedule, the photo reading.
for name in $(compgen -e); do
  case "$name" in
    Auth__* | Database__* | Storage__* | Settings__* | Backups__* | Recognition__* | DataProtection__* | Setup__* | Smtp__* | ASPNETCORE_*) unset "$name" ;;
  esac
done

files="$(mktemp -d -t tankstat-screenshots-XXXXXX)"
trap 'kill "${pid:-}" 2>/dev/null || true; wait "${pid:-}" 2>/dev/null || true; rm -rf "$files"' EXIT

# A few vehicles with a couple of years of logs each and a drawn picture each (never a downloaded photo: those carry licences of their
# own, and this needs no network), the same data every time (the seed); the cards' colours follow the vehicles' ids, which are new
# each run, so the files still differ.
Auth__Mode=None dotnet run --project src/backend/Tankstat.Seeder -- --yes \
  --connection "Data Source=${files}/tankstat.db" --uploads "${files}/uploads" --photos "${files}/photos" --settings "${files}/settings.json" \
  --vehicles 4 --refuelings 40-70 --recurring 2-4 --expenses 6-12 --notifications 4 --seed 2026 --drawn-pictures

# From out/: the content root (wwwroot, the web app) is the working directory, not the dll's folder. An instance that holds data counts
# as set up, and with sign-in off everything the seeder made is on the page.
(
  cd out
  ASPNETCORE_ENVIRONMENT=Production \
    Database__Provider=Sqlite Database__ConnectionString="Data Source=${files}/tankstat.db" \
    Storage__Path="${files}/uploads" Storage__PhotosPath="${files}/photos" Backups__Path="${files}/backups" \
    DataProtection__KeysPath="${files}/keys" Settings__Path="${files}/settings.json" \
    Auth__Mode=None Setup__Enabled=false \
    exec dotnet Tankstat.Api.dll --urls "$E2E_URL"
) > "${files}/app.log" 2>&1 &
pid=$!

for _ in $(seq 1 150); do
  if curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$E2E_URL/graphql" >/dev/null; then break; fi
  if ! kill -0 "$pid" 2>/dev/null; then break; fi
  sleep 0.2
done
if ! curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$E2E_URL/graphql" >/dev/null; then
  echo "The app did not start on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi

web="$repo/src/frontend/tankstat-react"
status=0
if [[ "${E2E_NATIVE:-}" == 1 ]]; then
  (cd "$web" && npx playwright test --config playwright.screenshots.config.ts "$@") || status=$?
else
  version="$(cd "$web" && node -p "require('@playwright/test/package.json').version")"
  # As the host user, so the pictures belong to them; the whole repository is mounted, since they land in docs/screenshots.
  docker run --rm --network host --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e E2E_URL \
    -v "$repo:$repo" -w "$web" "mcr.microsoft.com/playwright:v${version}-noble" npx playwright test --config playwright.screenshots.config.ts "$@" || status=$?
fi
if [[ "$status" != 0 ]]; then
  echo "The app's last log lines:" >&2
  tail -n 40 "${files}/app.log" >&2
fi
exit "$status"
