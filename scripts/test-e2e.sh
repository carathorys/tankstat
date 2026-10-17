#!/usr/bin/env bash
# Starts the published build (out/) on a throwaway port, runs the real-browser tests (tests/e2e) against it, always cleans up.
# Playwright runs natively with E2E_NATIVE=1 (CI, which installs Chromium), otherwise in the Playwright image of the installed version
# (this machine's Linux is not one Playwright supports). Arguments go to `playwright test`: scripts/test-e2e.sh --project phone
# Besides it, four new instances that wait for their setup, on the next ports, for the setup's tests (E2E_SETUP).
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
# The settings the shared server takes from its environment besides the machine's own: the OIDC values, an administrator list and a backup
# time on no day (Backups__Days=None, nothing scheduled) make the read-only rows of the Server settings page. The restore servers below set
# them too, as a move with the same compose file does: a backup records them, and a restore where one has no value is refused (#140).
instance_env=(
  Auth__Oidc__Authority=https://auth.example-identity.test/application/o/tankstat-production/
  Auth__Oidc__ClientId=1qViVYDsILFCoXa8Rk2mT5pWn9QeZ4uB7yHcJ3dE Auth__Oidc__ClientSecret=made-up-secret
  Auth__Oidc__Scopes__0=groups Auth__AdminEmails__0=first.administrator@example.com
  Backups__Every=03:00 Backups__Days=None
)
app() {
  # From out/: the content root (wwwroot, the web app) is the working directory, not the dll's folder.
  cd out
  ASPNETCORE_ENVIRONMENT=Production \
    Database__Provider=Sqlite Database__ConnectionString="Data Source=${files}/tankstat.db" \
    Storage__Path="${files}/uploads" Storage__PhotosPath="${files}/photos" Backups__Path="${files}/backups" \
    DataProtection__KeysPath="${files}/keys" Settings__Path="${files}/settings.json" \
    Auth__Mode=Standalone \
    exec env "${instance_env[@]}" dotnet Tankstat.Api.dll --urls "$E2E_URL"
}
# A new Standalone instance waits for its setup, the only way to its first administrator (#149): finished below the unattended way, as
# root@example.com, whom the tests sign in as (tests/e2e/support/app.ts).
app > "${files}/app.log" 2>&1 &
pids=($!)
trap 'kill "${pids[@]}" 2>/dev/null || true; wait "${pids[@]}" 2>/dev/null || true; rm -rf "$files"' EXIT

# Waits for a server's API: false when it stopped or never answered.
answers() {
  for _ in $(seq 1 150); do
    if curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$1/graphql" >/dev/null; then return 0; fi
    kill -0 "$2" 2>/dev/null || return 1
    sleep 0.2
  done
  return 1
}

# The servers the setup's tests take over (tests/e2e/setup.e2e.ts), one per test and size: each is an instance waiting for its setup,
# which its test finishes or restores a backup into, so none is shared. "fresh" runs the shipped sign-in off (the wizard chooses the
# mode) with nothing else set, "restore" Standalone without an administrator and the shared server's instance_env, "reopened" an
# instance set up with sign-in off and started again in Standalone, which has no administrator, so its setup reopens (#149); each with
# folders of its own.
setup_names=(phone-fresh desktop-fresh phone-restore desktop-restore phone-reopened desktop-reopened)
setup_app() {
  local dir="${files}/setup-$1"
  mkdir -p "$dir"
  cd out
  export ASPNETCORE_ENVIRONMENT=Production Database__Provider=Sqlite Database__ConnectionString="Data Source=${dir}/tankstat.db" \
    Storage__Path="${dir}/uploads" Storage__PhotosPath="${dir}/photos" Backups__Path="${dir}/backups" \
    DataProtection__KeysPath="${dir}/keys" Settings__Path="${dir}/settings.json"
  if [[ "$1" == *-restore ]]; then export Auth__Mode=Standalone "${instance_env[@]}"; fi
  if [[ "$1" == *-reopened ]]; then
    # Set up once with sign-in off (the environment completes it), stopped, then started in Standalone.
    Auth__Mode=None Setup__Enabled=false dotnet Tankstat.Api.dll --urls "http://127.0.0.1:$2" &
    local first=$!
    answers "http://127.0.0.1:$2" "$first" || exit 1
    kill "$first" && wait "$first" || true
    export Auth__Mode=Standalone
  fi
  exec dotnet Tankstat.Api.dll --urls "http://127.0.0.1:$2"
}
for i in "${!setup_names[@]}"; do
  setup_app "${setup_names[$i]}" $((port + 1 + i)) > "${files}/setup-${setup_names[$i]}.log" 2>&1 &
  pids+=($!)
done

if ! answers "$E2E_URL" "${pids[0]}"; then
  echo "The app did not start on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi
# The shared server's setup, finished without a browser: the code from its log and one setupFinish.
code="$(grep -oE 'setup code [A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}' "${files}/app.log" | head -n 1 | cut -d' ' -f3)"
finish="{\"query\":\"mutation(\$i: SetupFinishInput!) { setupFinish(input: \$i) { restarting } }\",\"variables\":{\"i\":{\"code\":\"${code}\",\"email\":\"root@example.com\",\"password\":\"initial-password-1\",\"passwordConfirmation\":\"initial-password-1\"}}}"
if ! curl -sf -H 'Content-Type: application/json' -H 'X-Requested-With: fetch' -d "$finish" "$E2E_URL/graphql" | grep -q '"restarting":false'; then
  echo "The app's setup could not be finished on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi
# Each waiting server's address and its one-time code, from the line its start logs, for the tests (E2E_SETUP, JSON by name).
setup="" sep=""
for i in "${!setup_names[@]}"; do
  name="${setup_names[$i]}" url="http://127.0.0.1:$((port + 1 + i))" log="${files}/setup-${setup_names[$i]}.log"
  code=""
  if answers "$url" "${pids[$((i + 1))]}"; then code="$(grep -oE 'setup code [A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}' "$log" | head -n 1 | cut -d' ' -f3)"; fi
  if [[ -z "$code" ]]; then
    echo "The waiting server $name did not start on $url, or logged no setup code:" >&2
    cat "$log" >&2
    exit 1
  fi
  setup+="${sep}\"$name\":{\"url\":\"$url\",\"code\":\"$code\"}" sep=","
done
export E2E_SETUP="{${setup}}"

status=0
if [[ "${E2E_NATIVE:-}" == 1 ]]; then
  npx playwright test "$@" || status=$?
else
  version="$(node -p "require('@playwright/test/package.json').version")"
  # As the host user, so nothing it writes (test-results/, the report) belongs to root; the repository's own node_modules, the image's browsers.
  docker run --rm --network host --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e E2E_URL -e E2E_SETUP \
    -v "$repo:$repo" -w "$repo" "mcr.microsoft.com/playwright:v${version}-noble" npx playwright test "$@" || status=$?
fi
if [[ "$status" != 0 ]]; then
  echo "The app's last log lines:" >&2
  tail -n 40 "${files}/app.log" >&2
  for name in "${setup_names[@]}"; do
    echo "The waiting server $name's last log lines:" >&2
    tail -n 20 "${files}/setup-$name.log" >&2
  done
  echo "Report: npx playwright show-report; a failed test's video: test-results/e2e/<test>/video.webm, its trace: npx playwright show-trace test-results/e2e/<test>/trace.zip" >&2
fi
exit "$status"
