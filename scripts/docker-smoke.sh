#!/usr/bin/env bash
# Starts the container image the way a self-hoster would (environment variables only) and checks that it serves the
# web app and the API. Usage: scripts/docker-smoke.sh <image> [expected-version]
set -euo pipefail

image="${1:?usage: docker-smoke.sh <image> [expected-version]}"
expected_version="${2:-}"
name="tankstat-smoke-$$"
port="${SMOKE_PORT:-18099}"
url="http://127.0.0.1:${port}"

cleanup() {
  docker logs "$name" 2>&1 | tail -n 40 || true
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker volume rm "${name}-photos" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# The photos of logs kept apart on a volume of their own (opt-in, as docs/self-hosting.md describes): the unprivileged user must be able to write to it.
docker run -d --name "$name" -p "${port}:8080" -v "${name}-photos:/data/photos" -e Storage__PhotosPath=/data/photos -e Auth__Mode=None "$image" >/dev/null

gql() { curl -sf -H 'Content-Type: application/json' -d "$1" "${url}/graphql"; }

for _ in $(seq 1 60); do
  gql '{"query":"{ __typename }"}' >/dev/null 2>&1 && break
  sleep 1
done
gql '{"query":"{ __typename }"}' >/dev/null || { echo "FAIL: the API did not come up" >&2; exit 1; }

health="$(gql '{"query":"{ health { status version databaseReachable } session { mode } }"}')"
echo "health: $health"
grep -q '"status":"ok"' <<<"$health" || { echo "FAIL: health is not ok" >&2; exit 1; }
grep -q '"databaseReachable":true' <<<"$health" || { echo "FAIL: database not reachable (migrations?)" >&2; exit 1; }
if [ -n "$expected_version" ]; then
  grep -q "\"version\":\"${expected_version}" <<<"$health" || { echo "FAIL: expected API version ${expected_version}" >&2; exit 1; }
fi

# The same container serves the built web app, including client-side routes (SPA fallback).
curl -sf "${url}/" | grep -q '<title>Tankstat</title>' || { echo "FAIL: web app not served at /" >&2; exit 1; }
curl -sf "${url}/vehicles" | grep -q '<title>Tankstat</title>' || { echo "FAIL: SPA fallback not working" >&2; exit 1; }

# The web app is installable: the manifest with its content type, the service worker, and the cache rules the API adds
# (fixed names are revalidated, hashed assets are kept for good).
curl -sfI "${url}/manifest.webmanifest" | grep -qi '^content-type: application/manifest+json' || { echo "FAIL: manifest not served as application/manifest+json" >&2; exit 1; }
curl -sf "${url}/manifest.webmanifest" | grep -q '"short_name"' || { echo "FAIL: manifest content" >&2; exit 1; }
curl -sfI "${url}/sw.js" >/dev/null || { echo "FAIL: service worker not served" >&2; exit 1; }
curl -sf "${url}/sw.js" | grep -q 'auth' || { echo "FAIL: the service worker must leave /auth to the server" >&2; exit 1; }
curl -sfI "${url}/" | grep -qi '^cache-control: no-cache' || { echo "FAIL: index.html must be no-cache" >&2; exit 1; }
asset="$(curl -sf "${url}/" | grep -o '/assets/[^"]*\.js' | head -n 1 || true)"
[ -n "$asset" ] || { echo "FAIL: index.html names no script under /assets" >&2; exit 1; }
curl -sfI "${url}${asset}" | grep -qi 'immutable' || { echo "FAIL: hashed assets must be immutable" >&2; exit 1; }
# The web app carries its own version (shown in the footer, also while the API is out of reach); the entry script holds it.
if [ -n "$expected_version" ]; then
  curl -sf "${url}${asset}" | grep -qF "$expected_version" || { echo "FAIL: expected web app version ${expected_version}" >&2; exit 1; }
fi

car="$(gql '{"query":"mutation { addVehicle(input: { name: \"Smoke\", fuelType: PETROL }) { id } }"}' | sed -nE 's/.*"id":"([^"]+)".*/\1/p' || true)"
[ -n "$car" ] || { echo "FAIL: cannot write to the database" >&2; exit 1; }

# A vehicle's picture goes to the pictures (/data/uploads), the photo of a refuelling to the photos of logs (/data/photos).
log="$(gql "{\"query\":\"mutation { logRefueling(input: { vehicleId: \\\"$car\\\", date: \\\"2026-01-01\\\", volume: 40, totalCost: 60, currency: \\\"EUR\\\", odometer: 1000, isFullTank: true }) { id } }\"}" | sed -nE 's/.*"id":"([^"]+)".*/\1/p' || true)"
[ -n "$log" ] || { echo "FAIL: cannot log a refuelling" >&2; exit 1; }
jpeg() { printf '\xff\xd8\xff\xe0smoke'; }
jpeg | curl -sf -X PUT -H 'Content-Type: image/jpeg' --data-binary @- "${url}/media/vehicles/${car}/picture" >/dev/null || { echo "FAIL: cannot upload a vehicle picture" >&2; exit 1; }
jpeg | curl -sf -X PUT -H 'Content-Type: image/jpeg' --data-binary @- "${url}/media/refuelings/${log}/photos" >/dev/null || { echo "FAIL: cannot upload the photo of a refuelling" >&2; exit 1; }
files="$(docker exec "$name" find /data/uploads /data/photos -type f 2>&1 || true)"
grep -q '^/data/uploads/vehicles/[0-9a-f]*/picture/' <<<"$files" || { echo "FAIL: the vehicle picture is not under /data/uploads" >&2; echo "$files" >&2; exit 1; }
grep -q '^/data/photos/vehicles/[0-9a-f]*/refuelings/' <<<"$files" || { echo "FAIL: the photo of a refuelling is not under /data/photos" >&2; echo "$files" >&2; exit 1; }

user="$(docker exec "$name" id -u)"
[ "$user" != "0" ] || { echo "FAIL: the container runs as root" >&2; exit 1; }

echo "OK: $image serves the web app (installable, cache rules in place) and the API (version check of both: ${expected_version:-skipped}, user $user), pictures and photos in their folders"
