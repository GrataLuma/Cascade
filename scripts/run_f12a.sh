#!/usr/bin/env bash
# F12.a — SeedMinMax basic sweep, 8 s values in parallel (max 5 concurrent).
# Expected wall time: ~30-60 min.
set -euo pipefail
cd "$(dirname "$0")/.."

EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
mkdir -p reports/f12

log() { echo "[$(date -Iseconds)] $*" ; }

log "F12.a START (8 s values, max 5 concurrent)"

run_one() {
  local s="$1"
  log "  [s=$s] starting..."
  "$EXE" calibrate-p-upd \
    --config "configs/f12_s${s}.json" \
    --p-upd-values "0.75" \
    --runs 200 \
    --max-iter 5000 --early-abort-window 30 \
    --memory-guard-mb 2048 \
    --output "reports/f12/s${s}.md" \
    --output-json "reports/f12/s${s}.json" \
    > "reports/f12/s${s}.log" 2>&1
  local rc=$?
  log "  [s=$s] done (exit $rc)"
  return $rc
}

export -f run_one log
export EXE

# xargs -P 5 gives max 5 concurrent
printf "%s\n" 1 2 3 5 7 10 15 30 | xargs -I {} -P 5 -n 1 bash -c 'run_one "$@"' _ {}

log "F12.a DONE"
