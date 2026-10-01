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
}
trap cleanup EXIT

docker run -d --name "$name" -p "${port}:8080" -e Auth__Mode=None "$image" >/dev/null

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

gql '{"query":"mutation { addVehicle(input: { name: \"Smoke\", fuelType: PETROL }) { id } }"}' | grep -q '"id"' || { echo "FAIL: cannot write to the database" >&2; exit 1; }

user="$(docker exec "$name" id -u)"
[ "$user" != "0" ] || { echo "FAIL: the container runs as root" >&2; exit 1; }

echo "OK: $image serves the web app and the API (version check: ${expected_version:-skipped}, user $user)"
