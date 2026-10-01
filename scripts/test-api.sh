#!/usr/bin/env bash
# Starts the API on a throwaway port, runs the contract tests against it, always cleans up.
set -euo pipefail

port="${TANKSTAT_API_PORT:-5099}"
export TANKSTAT_API_URL="http://localhost:${port}"

dotnet build src/backend/Tankstat.Api -c Release --nologo -v q
# Throwaway DB so the run never touches a developer database.
# Schema introspection (used by the contract tests) is Development-only in HotChocolate.
export ASPNETCORE_ENVIRONMENT=Development
export Database__ConnectionString="Data Source=:memory:"
dotnet src/backend/Tankstat.Api/bin/Release/net10.0/Tankstat.Api.dll --urls "$TANKSTAT_API_URL" &
pid=$!
trap 'kill "$pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 50); do
  curl -sf -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "$TANKSTAT_API_URL/graphql" >/dev/null && break
  sleep 0.2
done

dotnet test tests/backend/Tankstat.Api.ApiTests
