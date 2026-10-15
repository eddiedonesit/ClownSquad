#!/usr/bin/env bash
# The regression suite: every headless probe, run in parallel, one line each.
# Exits non-zero if any check fails, so it can gate a commit or a CI job.
#
#   bash tools/run_probes.sh            the checks that must always pass
#   bash tools/run_probes.sh --balance  also the bot-vs-boss balance runs (they can lose)
#
# GODOT overrides the engine path.
set -u
cd "$(dirname "$0")/.."
GODOT="${GODOT:-/c/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe}"
OUT="$(mktemp -d)"

dotnet build ClownSquad.csproj -v q -nologo >"$OUT/build.txt" 2>&1 || { cat "$OUT/build.txt"; echo "BUILD FAILED"; exit 1; }

MUST=(
  "movetest"
  "weapontest"
  "weapontest skills all"
  "bindtest"
  "animtest"        # trace only: passes unless it crashes
  "aitest"          # trace only: passes unless it crashes
  "wavetest stage 1-1"
  "losetest stage 1-1"
  "wavetest stage 1-*"
  "wavetest stage 3-B level 12 fair"
)
BALANCE=(
  "wavetest stage 7-B level 22"
  "wavetest stage 10-B level 30"
  "wavetest bossrush"
)
RUNS=("${MUST[@]}")
[ "${1:-}" = "--balance" ] && RUNS+=("${BALANCE[@]}")

i=0
for r in "${RUNS[@]%%#*}"; do
  # shellcheck disable=SC2086
  (timeout 900 "$GODOT" --headless --path . -- $r >"$OUT/$i.txt" 2>&1) &
  i=$((i + 1))
done
wait

fail=0
i=0
for r in "${RUNS[@]%%#*}"; do
  line=$(grep -hE "PROBE: ALL PASS|PROBE: [0-9]+ FAIL|PROBE (PASS|FAIL)|AI PROBE|ANIMATION PROBE|Unhandled exception|Exception:" "$OUT/$i.txt" | tail -1)
  [ -z "$line" ] && line="(no result: crashed or timed out)"
  case "$line" in *FAIL*|*xception*|"(no result"*) fail=$((fail + 1)); mark="FAIL" ;; *) mark=" ok " ;; esac
  printf '[%s] %-36s %s\n' "$mark" "$r" "$line"
  i=$((i + 1))
done
echo "logs: $OUT"
[ $fail -eq 0 ] && echo "ALL GREEN" || echo "$fail RUN(S) FAILED"
exit $fail
