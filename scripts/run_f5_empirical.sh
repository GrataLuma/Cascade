#!/usr/bin/env bash
# F5.a — Empirical flooding defense sweep.
# For each P value, spawn 10 parallel b2 processes (one per transcript).
# Sweep P ∈ {1e7, 1e8, 1e9, 1e10}. P=1e11 skipped (~10h per transcript).
# Expected wall time: ~70 min total (dominated by P=1e10).
set -u
cd "$(dirname "$0")/.."

EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
mkdir -p reports/f5

log() { echo "[$(date -Iseconds)] $*" ; }

run_b2() {
  local p="$1"        # pool size
  local i="$2"        # transcript index 0..9
  local p_label="$3"  # human label for filename (e.g., 1e8)
  local out="reports/f5/b2_${p_label}_t${i}.log"
  log "  [P=${p_label} t${i}] starting..."
  "$EXE" b2 --pool "$p" --runs 1 > "$out" 2>&1
  local rc=$?
  log "  [P=${p_label} t${i}] done (exit $rc)"
}
export -f run_b2 log
export EXE

log "F5.a START — P sweep {1e7, 1e8, 1e9, 1e10}, 10 transcripts each, 10 parallel"

for p_pair in "10000000:1e7" "100000000:1e8" "1000000000:1e9" "10000000000:1e10"; do
  P="${p_pair%%:*}"
  LABEL="${p_pair##*:}"
  log "[sweep P=${LABEL}] starting 10 transcripts..."
  PIDS=()
  for i in $(seq 0 9); do
    run_b2 "$P" "$i" "$LABEL" &
    PIDS+=($!)
  done
  wait "${PIDS[@]}"
  log "[sweep P=${LABEL}] all 10 transcripts done"
done

log "F5.a DONE"
