#!/usr/bin/env bash
# Regenerates schema.graphql and the GraphQL TypeScript types and fails if they differ from what is on disk,
# i.e. someone changed the API schema or a .graphql document without running `mise run codegen`.
set -euo pipefail

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
cp schema.graphql "$tmp/schema.graphql"
cp src/frontend/gql/generated.ts "$tmp/generated.ts"

mise run codegen >/dev/null

status=0
cmp -s schema.graphql "$tmp/schema.graphql" || { echo "schema.graphql is out of date" >&2; status=1; }
cmp -s src/frontend/gql/generated.ts "$tmp/generated.ts" || { echo "src/frontend/gql/generated.ts is out of date" >&2; status=1; }
[ "$status" -eq 0 ] || echo "Run 'mise run codegen' and commit the result." >&2
exit "$status"
