#!/usr/bin/env bash
# Measures start-up time (NFR-1) and idle memory (NFR-2) of the packaged app.
#
#   build/package.sh <rid>          # first, so the published app exists
#   build/measure.sh <rid> [runs]   # default: 5 runs
#
# Generates a 500-request workspace, starts the published app with --measure for each run and prints the
# median. The first run includes cold disk caches; later runs are warm. Targets: start-up < 1500 ms,
# idle memory < 200 MB.
set -euo pipefail

rid="${1:?usage: build/measure.sh <rid> [runs]}"
runs="${2:-5}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish="$root/artifacts/publish/$rid"
workspace="$root/artifacts/perf/workspace"
reports="$root/artifacts/perf/reports"

native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }

case "$rid" in
  win-*) app="$publish/JetApi.exe" ;;
  *) app="$publish/JetApi" ;;
esac
[ -x "$app" ] || { echo "No published app at $app. Run build/package.sh $rid first." >&2; exit 1; }

dotnet run "$root/build/perf/generate-workspace.cs" -- "$(native "$workspace")"
rm -rf "$reports"
mkdir -p "$reports"

field() { sed -n "s/.*\"$1\": *\([0-9.]*\).*/\1/p" "$2"; }
median() { sort -n | awk '{ v[NR] = $1 } END { print (NR % 2) ? v[(NR + 1) / 2] : (v[NR / 2] + v[NR / 2 + 1]) / 2 }'; }

for i in $(seq 1 "$runs"); do
  report="$reports/run-$i.json"
  "$app" --measure "$(native "$workspace")" "$(native "$report")"
  # A GUI app on Windows returns before it exits; wait for the report.
  for _ in $(seq 1 120); do [ -s "$report" ] && break; sleep 0.5; done
  [ -s "$report" ] || { echo "Run $i produced no report" >&2; exit 1; }
  sleep 1
  echo "run $i: ready $(field workspaceReadyMs "$report") ms, window $(field windowShownMs "$report") ms, working set $(field idleWorkingSetMb "$report") MB, private $(field idlePrivateMb "$report") MB"
done

echo "median ($runs runs, $(field requests "$reports/run-1.json") requests):"
echo "  workspace ready: $(for f in "$reports"/*.json; do field workspaceReadyMs "$f"; done | median) ms   (NFR-1: < 1500 ms)"
echo "  window shown:    $(for f in "$reports"/*.json; do field windowShownMs "$f"; done | median) ms"
echo "  idle working set: $(for f in "$reports"/*.json; do field idleWorkingSetMb "$f"; done | median) MB   (NFR-2: < 200 MB)"
echo "  idle private:     $(for f in "$reports"/*.json; do field idlePrivateMb "$f"; done | median) MB"
