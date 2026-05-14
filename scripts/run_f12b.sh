#!/usr/bin/env bash
# F12.b — B.4 oracle discrimination retest at dense pool (s=1,2,3).
# 30 transcripts per s value. Conditional on F12.a showing s=1,2 converge.
# 3 parallel processes (one per s), expected wall time ~30-45 min.
set -euo pipefail
cd "$(dirname "$0")/.."

EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
mkdir -p reports/f12

log() { echo "[$(date -Iseconds)] $*" ; }

log "F12.b START (s=1,2,3, 30 transcripts each, K=100k)"

run_b4() {
  local s="$1"
  log "  [s=$s] starting b4..."
  "$EXE" b4 \
    --config "configs/f12_s${s}.json" \
    --runs 30 \
    --disc-samples 100000 \
    > "reports/f12/b4_s${s}.log" 2>&1
  local rc=$?
  log "  [s=$s] done (exit $rc)"
  return $rc
}

export -f run_b4 log
export EXE

printf "%s\n" 1 2 3 | xargs -I {} -P 3 -n 1 bash -c 'run_b4 "$@"' _ {}

log "F12.b DONE"
