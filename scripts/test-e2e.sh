#!/usr/bin/env bash
# Starts the published build (out/) on a throwaway port, runs the real-browser tests (src/frontend/tankstat-react/e2e) against it, always cleans up.
# Playwright runs natively with E2E_NATIVE=1 (CI, which installs Chromium), otherwise in the Playwright image of the installed version
# (this machine's Linux is not one Playwright supports). Arguments go to `playwright test`: scripts/test-e2e.sh --project phone
# Besides it, six instances that wait for their setup (two of them set up first, so their setup reopens), on the next ports, for the
# setup's tests (E2E_SETUP); two more for the import from a backup (E2E_IMPORT), whose run locks SQLite for everyone on its server,
# with a backup file the script writes for them from an instance of its own; and two Standalone ones for Restore cars (E2E_RESTORE, #340).
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
if [[ ! -f out/Tankstat.Api.dll ]]; then
  echo "No build in out/: run 'mise run build' first (mise run test:e2e does)." >&2
  exit 1
fi

port="${E2E_PORT:-5097}"
export E2E_URL="http://127.0.0.1:${port}"
# The app, the six waiting servers, the import fixture's source, the two import servers and the two restore servers take twelve ports from
# it: one already answering (another run, another session's) would answer for them, and the tests would fail on its data (#236).
for p in $(seq "$port" $((port + 11))); do
  if curl -s -o /dev/null --max-time 2 "http://127.0.0.1:${p}"; then
    echo "Something already answers on port ${p} (this run takes ${port}-$((port + 11))): stop it, or choose another start with E2E_PORT." >&2
    exit 1
  fi
done

# Nothing of the developer's own setup: the mode, the database, the folders, the schedule, the photo reading, the telemetry, the
# defaults the pages show (a date format), the logging the start is read from. .NET reads the variables' names in any case.
shopt -s nocasematch
for name in $(compgen -e); do
  case "$name" in
    Auth__* | Database__* | Storage__* | Settings__* | Backups__* | Recognition__* | DataProtection__* | Setup__* | Smtp__* | \
      Defaults__* | Notifications__* | Sync__* | Logging__* | Kestrel__* | ASPNETCORE_* | OTEL_* | Telemetry__*) unset "$name" ;;
  esac
done
shopt -u nocasematch

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
# root@example.com, whom the tests sign in as (src/frontend/tankstat-react/e2e/support/app.ts).
app > "${files}/app.log" 2>&1 &
pids=($!)
# The import's backup file is written inside the repository, which Playwright's container sees (the temporary folder it does not).
fixtures="$repo/src/frontend/tankstat-react/e2e/files/generated/${port}" # a folder per run: two runs on other ports do not share it
trap 'kill "${pids[@]}" 2>/dev/null || true; wait "${pids[@]}" 2>/dev/null || true; rm -rf "$files" "$fixtures"' EXIT

# Waits for a server's API: false when it stopped or never answered.
answers() {
  for _ in $(seq 1 150); do
    if curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$1/graphql" >/dev/null; then return 0; fi
    kill -0 "$2" 2>/dev/null || return 1
    sleep 0.2
  done
  return 1
}

# Waits for the one-time setup code a server's start logs (its log $1, its process $2) and prints it: false when the server stopped
# first or logged none. The start logs it before its API answers; a reopened server's first start (below) logs none.
setup_code() {
  local code
  for _ in $(seq 1 300); do
    code="$(grep -oE 'setup code [A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}' "$1" | tail -n 1 | cut -d' ' -f3 || true)"
    if [[ -n "$code" ]]; then echo "$code"; return 0; fi
    kill -0 "$2" 2>/dev/null || return 1
    sleep 0.2
  done
  return 1
}

# The servers the setup's tests take over (src/frontend/tankstat-react/e2e/setup.e2e.ts), one per test and size: each is an instance waiting for its setup,
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
    # Set up once with sign-in off (the environment completes it), stopped, then started in Standalone. The first start is this
    # subshell's own child, which the script's cleanup does not reach: stopped here whenever the subshell ends first.
    Auth__Mode=None Setup__Enabled=false dotnet Tankstat.Api.dll --urls "http://127.0.0.1:$2" &
    local first=$!
    trap 'kill "$first" 2>/dev/null || true' EXIT
    trap 'exit 143' TERM
    answers "http://127.0.0.1:$2" "$first" || exit 1
    kill "$first" && wait "$first" || true
    trap - EXIT TERM
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
if ! code="$(setup_code "${files}/app.log" "${pids[0]}")"; then
  echo "The app logged no setup code on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi
finish="{\"query\":\"mutation(\$i: SetupFinishInput!) { setupFinish(input: \$i) { restarting } }\",\"variables\":{\"i\":{\"code\":\"${code}\",\"email\":\"root@example.com\",\"password\":\"initial-password-1\",\"passwordConfirmation\":\"initial-password-1\"}}}"
if ! curl -sf -H 'Content-Type: application/json' -d "$finish" "$E2E_URL/graphql" | grep -q '"restarting":false'; then
  echo "The app's setup could not be finished on $E2E_URL:" >&2
  cat "${files}/app.log" >&2
  exit 1
fi
# Each waiting server's address and its one-time code, from the line its start logs, for the tests (E2E_SETUP, JSON by name). The code
# first: a reopened server's first start answers on the same port without one, and only the start that logs it waits for the tests.
setup="" sep=""
for i in "${!setup_names[@]}"; do
  name="${setup_names[$i]}" url="http://127.0.0.1:$((port + 1 + i))" log="${files}/setup-${setup_names[$i]}.log"
  if ! code="$(setup_code "$log" "${pids[$((i + 1))]}")" || ! answers "$url" "${pids[$((i + 1))]}"; then
    echo "The waiting server $name did not start on $url, or logged no setup code:" >&2
    cat "$log" >&2
    exit 1
  fi
  setup+="${sep}\"$name\":{\"url\":\"$url\",\"code\":\"$code\"}" sep=","
done
export E2E_SETUP="{${setup}}"

# The import from a backup (src/frontend/tankstat-react/e2e/import.e2e.ts, #337). Its file: an instance of its own (sign-in off) is given two
# cars through the API, an Octavia with enough fill-ups for a run to be cancelled while it goes, and a Fabia; it is stopped, and its
# backup written by the command line. Its servers: one per size with sign-in off, since a run locks SQLite for every other test.
import_env() {
  local dir="${files}/import-$1"
  mkdir -p "$dir"
  export ASPNETCORE_ENVIRONMENT=Production Database__Provider=Sqlite Database__ConnectionString="Data Source=${dir}/tankstat.db" \
    Storage__Path="${dir}/uploads" Storage__PhotosPath="${dir}/photos" Backups__Path="${dir}/backups" Imports__ScratchFolder="${dir}/scratch" \
    DataProtection__KeysPath="${dir}/keys" Settings__Path="${dir}/settings.json" Auth__Mode=None Setup__Enabled=false
}
# One GraphQL request of the fixture: fails on an error in the answer too (an error comes with HTTP 200), saying which request.
gql() {
  local answer
  if ! answer="$(curl -sf -H 'Content-Type: application/json' -d "$2" "$1/graphql")" || [[ "$answer" == *'"errors"'* ]]; then
    echo "The import fixture's request failed: $2" >&2
    echo "${answer:-}" >&2
    exit 1
  fi
  echo "$answer"
}
source_url="http://127.0.0.1:$((port + 7))"
(import_env source; cd out; exec dotnet Tankstat.Api.dll --urls "$source_url") > "${files}/import-source.log" 2>&1 &
source_pid=$!
pids+=("$source_pid")
if ! answers "$source_url" "$source_pid"; then
  echo "The import fixture's source did not start on $source_url:" >&2
  cat "${files}/import-source.log" >&2
  exit 1
fi
car_id() { grep -oE '"id":"[0-9a-f-]{36}"' | head -n 1 | cut -d'"' -f4; }
octavia="$(gql "$source_url" '{"query":"mutation { addVehicle(input: { name: \"Octavia\", licensePlate: \"IMP-001\", fuelType: DIESEL }) { id } }"}' | car_id)"
fabia="$(gql "$source_url" '{"query":"mutation { addVehicle(input: { name: \"Fabia\", fuelType: PETROL }) { id } }"}' | car_id)"
for i in $(seq 1 240); do
  day="$(date -u -d "2024-01-01 + $((i * 3)) days" +%F)"
  gql "$source_url" "{\"query\":\"mutation { logRefueling(input: { vehicleId: \\\"${octavia}\\\", date: \\\"${day}\\\", volume: 40, totalCost: 60, currency: \\\"EUR\\\", odometer: $((10000 + i * 600)), isFullTank: true }) { id } }\"}" >/dev/null
done
gql "$source_url" "{\"query\":\"mutation { addExpense(input: { vehicleId: \\\"${octavia}\\\", date: \\\"2024-03-01\\\", title: \\\"Car wash\\\", amount: 12, currency: \\\"EUR\\\" }) { id } }\"}" >/dev/null
gql "$source_url" "{\"query\":\"mutation { addRecurringExpense(input: { vehicleId: \\\"${octavia}\\\", title: \\\"Oil change\\\", kind: TIME, intervalMonths: 12 }) { id } }\"}" >/dev/null
gql "$source_url" "{\"query\":\"mutation { logRefueling(input: { vehicleId: \\\"${fabia}\\\", date: \\\"2025-05-01\\\", volume: 30, totalCost: 45, currency: \\\"EUR\\\", odometer: 55000, isFullTank: true }) { id } }\"}" >/dev/null
# A Polo for the details of a merge (#338): three fill-ups and three expenses, nothing with a photo or a tyre log line.
polo="$(gql "$source_url" '{"query":"mutation { addVehicle(input: { name: \"Polo\", fuelType: PETROL }) { id } }"}' | car_id)"
for f in "2025-01-10 30000" "2025-01-20 30500" "2025-01-30 31000"; do
  read -r day odo <<< "$f" # never `set --`: the arguments are the tests to run
  gql "$source_url" "{\"query\":\"mutation { logRefueling(input: { vehicleId: \\\"${polo}\\\", date: \\\"${day}\\\", volume: 35, totalCost: 52, currency: \\\"EUR\\\", odometer: ${odo}, isFullTank: true }) { id } }\"}" >/dev/null
done
for e in "2025-01-12 Wash 10" "2025-01-15 Wipers 20" "2025-01-18 Polish 15"; do
  read -r day title amount <<< "$e"
  gql "$source_url" "{\"query\":\"mutation { addExpense(input: { vehicleId: \\\"${polo}\\\", date: \\\"${day}\\\", title: \\\"${title}\\\", amount: ${amount}, currency: \\\"EUR\\\" }) { id } }\"}" >/dev/null
done
# A Corolla with its tyres (#339): a winter set on the car since November 2025, tracked tyre by tyre (W1 to W4), and a puncture of W1
# with an amount. The ids are made here, so the later calls can name them.
corolla="$(gql "$source_url" '{"query":"mutation { addVehicle(input: { name: \"Corolla\", fuelType: PETROL }) { id } }"}' | car_id)"
corolla_fill="$(gql "$source_url" "{\"query\":\"mutation { logRefueling(input: { vehicleId: \\\"${corolla}\\\", date: \\\"2025-10-01\\\", volume: 40, totalCost: 60, currency: \\\"EUR\\\", odometer: 80000, isFullTank: true }) { id } }\"}" | car_id)"
winter="$(cat /proc/sys/kernel/random/uuid)" w1="$(cat /proc/sys/kernel/random/uuid)"
gql "$source_url" "{\"query\":\"mutation { addTyreSet(input: { id: \\\"${winter}\\\", vehicleId: \\\"${corolla}\\\", name: \\\"Nordic winter\\\", season: WINTER, since: \\\"2024-10-01\\\", fitted: true, day: \\\"2025-11-01\\\" }) { vehicleId } }\"}" >/dev/null
gql "$source_url" "{\"query\":\"mutation { breakUpTyreSet(input: { setId: \\\"${winter}\\\", day: \\\"2025-11-01\\\", tyres: [{ id: \\\"${w1}\\\", label: \\\"W1\\\", corner: FRONT_LEFT }, { label: \\\"W2\\\", corner: FRONT_RIGHT }, { label: \\\"W3\\\", corner: REAR_LEFT }, { label: \\\"W4\\\", corner: REAR_RIGHT }] }) { vehicleId } }\"}" >/dev/null
gql "$source_url" "{\"query\":\"mutation { addTyreEntry(input: { vehicleId: \\\"${corolla}\\\", kind: PUNCTURE_REPAIRED, day: \\\"2025-12-02\\\", setId: \\\"${winter}\\\", note: \\\"A nail\\\", tyres: [{ tyreId: \\\"${w1}\\\" }], fee: { amount: 25, currency: \\\"EUR\\\", category: \\\"Tyres\\\", title: \\\"Puncture W1\\\" } }) { vehicleId } }\"}" >/dev/null
# The Corolla's media (#339): a photo of its fill-up (a PNG's signature is all the app checks), a shared chart, and the vehicle's dashboard
# showing it.
printf '\x89PNG\r\n\x1a\n\x00\x00\x00\x0dcorolla' > "${files}/corolla.png"
if ! curl -sf -X PUT -H 'Content-Type: image/png' -H 'X-Requested-With: XMLHttpRequest' --data-binary "@${files}/corolla.png" "$source_url/media/refuelings/${corolla_fill}/photos" >/dev/null; then
  echo "The import fixture's photo could not be added." >&2
  exit 1
fi
chart="$(gql "$source_url" "{\"query\":\"mutation { saveVehicleChart(input: { vehicleId: \\\"${corolla}\\\", title: \\\"Corolla spend\\\", shared: true, config: { metric: TOTAL_SPEND, grouping: MONTH, kind: BAR, range: ALL, stacked: false } }) { id } }\"}" | car_id)"
gql "$source_url" "{\"query\":\"mutation { saveDashboardLayout(input: { vehicleId: \\\"${corolla}\\\", personal: false, layoutVersion: 3, hidden: [], tiles: [{ id: \\\"chart:${chart}\\\", x: 0, y: 0, w: 2, h: 2 }] }) }\"}" >/dev/null
kill "$source_pid" && wait "$source_pid" 2>/dev/null || true
mkdir -p "$fixtures"
if ! (import_env source; cd out; dotnet Tankstat.Api.dll backup --photos --output "$fixtures/tankstat-backup.zip") > "${files}/import-backup.log" 2>&1; then
  echo "The import fixture's backup could not be written:" >&2
  cat "${files}/import-backup.log" >&2
  exit 1
fi
import="" sep=""
for i in 0 1; do
  name="$([[ $i == 0 ]] && echo phone || echo desktop)" url="http://127.0.0.1:$((port + 8 + i))"
  (import_env "$name"; cd out; exec dotnet Tankstat.Api.dll --urls "$url") > "${files}/import-${name}.log" 2>&1 &
  pids+=($!)
  if ! answers "$url" "$!"; then
    echo "The import server $name did not start on $url:" >&2
    cat "${files}/import-${name}.log" >&2
    exit 1
  fi
  import+="${sep}\"$name\":{\"url\":\"$url\"}" sep=","
done
export E2E_IMPORT="{${import},\"backup\":\"$fixtures/tankstat-backup.zip\"}"

# Restore cars (src/frontend/tankstat-react/e2e/restore-cars.e2e.ts, #340): one Standalone server per size (ports + 10 and + 11), set up
# like the shared one (root@example.com), with folders of its own: a restore's run holds SQLite's lock, and its people sign in.
restore_app() {
  local dir="${files}/restore-$1"
  mkdir -p "$dir"
  cd out
  ASPNETCORE_ENVIRONMENT=Production Database__Provider=Sqlite Database__ConnectionString="Data Source=${dir}/tankstat.db" \
    Storage__Path="${dir}/uploads" Storage__PhotosPath="${dir}/photos" Backups__Path="${dir}/backups" Imports__ScratchFolder="${dir}/scratch" \
    DataProtection__KeysPath="${dir}/keys" Settings__Path="${dir}/settings.json" Auth__Mode=Standalone \
    exec dotnet Tankstat.Api.dll --urls "$2"
}
restore="" sep=""
for i in 0 1; do
  name="$([[ $i == 0 ]] && echo phone || echo desktop)" url="http://127.0.0.1:$((port + 10 + i))"
  restore_app "$name" "$url" > "${files}/restore-${name}.log" 2>&1 &
  pids+=($!)
  pid=$!
  if ! code="$(setup_code "${files}/restore-${name}.log" "$pid")" || ! answers "$url" "$pid"; then
    echo "The restore server $name did not start on $url, or logged no setup code:" >&2
    cat "${files}/restore-${name}.log" >&2
    exit 1
  fi
  finish="{\"query\":\"mutation(\$i: SetupFinishInput!) { setupFinish(input: \$i) { restarting } }\",\"variables\":{\"i\":{\"code\":\"${code}\",\"email\":\"root@example.com\",\"password\":\"initial-password-1\",\"passwordConfirmation\":\"initial-password-1\"}}}"
  if ! curl -sf -H 'Content-Type: application/json' -d "$finish" "$url/graphql" | grep -q '"restarting":false'; then
    echo "The restore server $name's setup could not be finished:" >&2
    cat "${files}/restore-${name}.log" >&2
    exit 1
  fi
  restore+="${sep}\"$name\":{\"url\":\"$url\"}" sep=","
done
export E2E_RESTORE="{${restore}}"

# Playwright runs from the web app's folder: its config, node_modules, test-results/ and playwright-report/ are there.
web="$repo/src/frontend/tankstat-react"
status=0
if [[ "${E2E_NATIVE:-}" == 1 ]]; then
  (cd "$web" && npx playwright test "$@") || status=$?
else
  version="$(cd "$web" && node -p "require('@playwright/test/package.json').version")"
  # As the host user, so nothing it writes (test-results/, the report) belongs to root; the web app's own node_modules, the image's browsers.
  # The whole repository is mounted: a test reads a backup file from tests/backend/contracts.
  docker run --rm --network host --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e E2E_URL -e E2E_SETUP -e E2E_IMPORT -e E2E_RESTORE \
    -v "$repo:$repo" -w "$web" "mcr.microsoft.com/playwright:v${version}-noble" npx playwright test "$@" || status=$?
fi
if [[ "$status" != 0 ]]; then
  echo "The app's last log lines:" >&2
  tail -n 40 "${files}/app.log" >&2
  for name in "${setup_names[@]}"; do
    echo "The waiting server $name's last log lines:" >&2
    tail -n 20 "${files}/setup-$name.log" >&2
  done
  for name in phone desktop; do
    echo "The import server $name's last log lines:" >&2
    tail -n 20 "${files}/import-$name.log" >&2
    echo "The restore server $name's last log lines:" >&2
    tail -n 20 "${files}/restore-$name.log" >&2
  done
  echo "In src/frontend/tankstat-react: the report: npx playwright show-report; a failed test's video: test-results/e2e/<test>/video.webm, its trace: npx playwright show-trace test-results/e2e/<test>/trace.zip" >&2
fi
exit "$status"
