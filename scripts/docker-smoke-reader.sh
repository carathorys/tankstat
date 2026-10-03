#!/usr/bin/env bash
# Starts the photo reader image the way a self-hoster would (environment variables only) and checks that it is healthy, guards
# its API key and reads a receipt. The test photo comes from the image itself (its generator). Usage:
#   scripts/docker-smoke-reader.sh <image> [expected-version]
set -euo pipefail

image="${1:?usage: docker-smoke-reader.sh <image> [expected-version]}"
expected_version="${2:-}"
name="tankstat-reader-smoke-$$"
port="${SMOKE_PORT:-18098}"
url="http://127.0.0.1:${port}"
key="smoke-key"
work="$(mktemp -d)"

cleanup() {
  docker logs "$name" 2>&1 | tail -n 40 || true
  docker rm -f "$name" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

# An English fuel receipt (seed 2) that every Tesseract language set reads; the container user must be able to write it.
chmod 777 "$work"
docker run --rm -v "$work:/out" "$image" --synth /out --count 1 --seed 2 --kind fuel-receipt >/dev/null
photo="$work/synthetic-0001-fuel-receipt.webp"
total="$(grep -o '"total": "[0-9.]*"' "$work/synthetic-0001-fuel-receipt.expected.json" | grep -o '[0-9.]*')"
[ -s "$photo" ] && [ -n "$total" ] || { echo "FAIL: the image did not generate its test photo" >&2; exit 1; }

docker run -d --name "$name" -p "${port}:8081" --health-interval=2s -e Reader__ApiKey="$key" "$image" >/dev/null

for _ in $(seq 1 60); do
  curl -sf "${url}/v1/health" >/dev/null 2>&1 && break
  sleep 1
done
health="$(curl -sf "${url}/v1/health")" || { echo "FAIL: the reader did not come up" >&2; exit 1; }
echo "health: $health"
grep -q '"status":"ok"' <<<"$health" || { echo "FAIL: health is not ok" >&2; exit 1; }
for lang in hun eng deu; do
  grep -q "\"$lang\"" <<<"$health" || { echo "FAIL: Tesseract language $lang is missing" >&2; exit 1; }
done
if [ -n "$expected_version" ]; then
  grep -q "\"version\":\"${expected_version}\"" <<<"$health" || { echo "FAIL: expected version ${expected_version}" >&2; exit 1; }
fi

status="$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: image/webp' --data-binary @"$photo" "${url}/v1/read")"
[ "$status" = "401" ] || { echo "FAIL: reading without the API key answered $status, not 401" >&2; exit 1; }

result="$(curl -sf -H "X-Api-Key: ${key}" -H 'Content-Type: image/webp' --data-binary @"$photo" \
  "${url}/v1/read?kinds=odometer,fuel-receipt&locale=en&today=2026-10-01")" || { echo "FAIL: the reading failed" >&2; exit 1; }
echo "read: $result"
grep -q '"kind":"fuel-receipt"' <<<"$result" || { echo "FAIL: the receipt was not recognised" >&2; exit 1; }
grep -q "\"name\":\"total\",\"value\":\"${total}\"" <<<"$result" || { echo "FAIL: expected the total ${total}" >&2; exit 1; }

for _ in $(seq 1 30); do
  [ "$(docker inspect -f '{{.State.Health.Status}}' "$name")" = "healthy" ] && break
  sleep 1
done
[ "$(docker inspect -f '{{.State.Health.Status}}' "$name")" = "healthy" ] || { echo "FAIL: the container health check does not pass" >&2; exit 1; }

user="$(docker exec "$name" id -u)"
[ "$user" != "0" ] || { echo "FAIL: the container runs as root" >&2; exit 1; }

echo "OK: $image is healthy, guards its API key and read the receipt (total ${total}; version check: ${expected_version:-skipped}, user $user)"
