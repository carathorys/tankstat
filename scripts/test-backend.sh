#!/usr/bin/env bash
# Runs the backend's unit and in-process integration tests side by side, as CI does (the solution must be built: --no-build), with
# coverage and a .trx per run in ./test-results; each run's output is printed, folded on GitHub, once it is done.
#
# The integration tests share one xUnit collection (one host and process-wide environment variables, see ApiCollection), so in one
# process they run one after another: most of the backend's time. They are split into three processes by the first letter of the test
# class, sized by how long the classes take; the last takes every class the first two do not, so a new class is never left out.
set -uo pipefail
cd "$(dirname "$0")/.."

integration=tests/backend/Tankstat.Api.IntegrationTests
prefix() { local out="" p; for p in "$@"; do out+="${out:+|}FullyQualifiedName~IntegrationTests.$p"; done; echo "$out"; }
none_of() { local out="" p; for p in "$@"; do out+="${out:+&}FullyQualifiedName!~IntegrationTests.$p"; done; echo "$out"; }
first=(A B C D E F G H I J K L M)
second=(N O P Q R Sc Sh)

runs=() # name|project or test assembly|filter
for project in tests/backend/*.UnitTests; do runs+=("$(basename "$project")|$project|"); done
# Coverage rewrites the test assemblies where they are while a run lasts, so each process of the integration tests runs its own copy of
# the build output; two on one folder break each other's assemblies.
built="$(dirname "$(ls -d $integration/bin/*/net*/Tankstat.Api.IntegrationTests.dll | head -1)")"
rm -rf test-shards
for n in 1 2 3; do mkdir -p "test-shards/$n" && cp -r "$built/." "test-shards/$n/"; done
runs+=("IntegrationTests-1|test-shards/1/Tankstat.Api.IntegrationTests.dll|$(prefix "${first[@]}")")
runs+=("IntegrationTests-2|test-shards/2/Tankstat.Api.IntegrationTests.dll|$(prefix "${second[@]}")")
runs+=("IntegrationTests-3|test-shards/3/Tankstat.Api.IntegrationTests.dll|$(none_of "${first[@]}" "${second[@]}")")

mkdir -p test-results test-logs
pids=() names=()
for run in "${runs[@]}"; do
  IFS='|' read -r name project filter <<<"$run"
  dotnet test "$project" --no-build ${filter:+--filter "$filter"} --settings coverlet.runsettings --collect:"XPlat Code Coverage" \
    --logger "trx;LogFileName=$name.trx" --results-directory test-results > "test-logs/$name.log" 2>&1 &
  pids+=("$!") names+=("$name")
done

status=0
for i in "${!pids[@]}"; do
  if wait "${pids[$i]}"; then result=passed; else result=FAILED; status=1; fi
  echo "::group::${names[$i]} ($result)"
  cat "test-logs/${names[$i]}.log"
  echo "::endgroup::"
  [ "$result" = passed ] || echo "::error::${names[$i]} failed (see its group above)"
done
exit "$status"
