#!/usr/bin/env bash
# Starts the published build (out/) on a throwaway port, runs the real-browser tests (tests/e2e) against it, always cleans up.
# Playwright runs natively with E2E_NATIVE=1 (CI, which installs Chromium), otherwise in the Playwright image of the installed version
# (this machine's Linux is not one Playwright supports). Arguments go to `playwright test`: scripts/test-e2e.sh --project phone
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
if [[ ! -f out/Tankstat.Api.dll ]]; then
  echo "No build in out/: run 'mise run build' first (mise run test:e2e does)." >&2
  exit 1
fi

port="${E2E_PORT:-5097}"
export E2E_URL="http://127.0.0.1:${port}"

# Nothing of the developer's own setup: the mode, the database, the folders, the schedule, the photo reading.
for name in $(compgen -e); do
  case "$name" in
    Auth__* | Database__* | Storage__* | Settings__* | Backups__* | Recognition__* | DataProtection__* | Setup__* | Smtp__* | ASPNETCORE_*) unset "$name" ;;
  esac
done

files="$(mktemp -d -t tankstat-e2e-XXXXXX)"
app() {
  # From out/: the content root (wwwroot, the web app) is the working directory, not the dll's folder.
  cd out
  ASPNETCORE_ENVIRONMENT=Production \
    Database__Provider=Sqlite Database__ConnectionString="Data Source=${files}/tankstat.db" \
    Storage__Path="${files}/uploads" Storage__PhotosPath="${files}/photos" Backups__Path="${files}/backups" \
    DataProtection__KeysPath="${files}/keys" Settings__Path="${files}/settings.json" \
    Auth__Mode=Standalone Auth__Standalone__AdminEmail=root@example.com Auth__Standalone__AdminPassword=initial-password-1 \
    Setup__Enabled=false \
    Auth__Oidc__Authority=https://auth.example-identity.test/application/o/tankstat-production/ \
    Auth__Oidc__ClientId=1qViVYDsILFCoXa8Rk2mT5pWn9QeZ4uB7yHcJ3dE Auth__Oidc__ClientSecret=made-up-secret \
    Auth__Oidc__Scopes__0=groups Auth__AdminEmails__0=first.administrator@example.com \
    Backups__Every=03:00 Backups__Days=None \
    exec dotnet Tankstat.Api.dll --urls "$E2E_URL"
}
# The environment above, besides the run's own folders: the OIDC values, an administrator list and a backup time (on no day, so
# nothing is scheduled) make the read-only rows of the Server settings page. Setup__Enabled=false keeps a new instance from waiting for
# its setup (#90); the bootstrap administrator is the one the tests sign in as (tests/e2e/support/app.ts).
app > "${files}/app.log" 2>&1 &
pid=$!
trap 'kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; rm -rf "$files"' EXIT

up=""
for _ in $(seq 1 100); do
  if curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$E2E_URL/graphql" >/dev/null; then up=1; break; fi
  kill -0 "$pid" 2>/dev/null || break
  sleep 0.2
done
if [[ -z "$up" ]]; then
  echo "The app did not start on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi

status=0
if [[ "${E2E_NATIVE:-}" == 1 ]]; then
  npx playwright test "$@" || status=$?
else
  version="$(node -p "require('@playwright/test/package.json').version")"
  # As the host user, so nothing it writes (test-results/, the report) belongs to root; the repository's own node_modules, the image's browsers.
  docker run --rm --network host --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e E2E_URL \
    -v "$repo:$repo" -w "$repo" "mcr.microsoft.com/playwright:v${version}-noble" npx playwright test "$@" || status=$?
fi
if [[ "$status" != 0 ]]; then
  echo "The app's last log lines:" >&2
  tail -n 40 "${files}/app.log" >&2
  echo "Report: npx playwright show-report; a failed test's trace: npx playwright show-trace test-results/e2e/<test>/trace.zip" >&2
fi
exit "$status"
