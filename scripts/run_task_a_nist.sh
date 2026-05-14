#!/usr/bin/env bash
# Task A — NIST disambiguation runs (post-F13 reporter upgrade).
# 5 parallel processes: 4 profiles × 100-key + 1 PQ128 × 500-key.
# Expected wall time: ~60 min (limited by PQ128 500-key).
set -u
cd "$(dirname "$0")/.."

NIST="./EmpiricalEvaluation/NistTests/bin/Debug/net9.0/NistTests.exe"
mkdir -p reports/nist_rerun

log() { echo "[$(date -Iseconds)] $*" ; }

run_nist() {
  local tag="$1"       # output filename prefix
  local profile="$2"   # config name (without path/extension)
  local runs="$3"      # number of keys
  log "[$tag] starting ($profile, $runs keys)..."
  "$NIST" run \
    --config "configs/${profile}.json" \
    --runs "$runs" \
    --max-iter 500 \
    --output "reports/nist_rerun/${tag}.md" \
    --csv "reports/nist_rerun/${tag}.csv" \
    > "reports/nist_rerun/${tag}.stdout" 2>&1
  local rc=$?
  log "  [$tag] done (exit $rc)"
}
export -f run_nist log
export NIST

log "Task A START — 5 NIST processes in parallel"

run_nist iot_100        low_resource_iot         100 &
P1=$!
run_nist mobile_100     mobile_bandwidth         100 &
P2=$!
run_nist classical_100  high_security_classical  100 &
P3=$!
run_nist pq128_100      high_security_pq128      100 &
P4=$!
run_nist pq128_500      high_security_pq128      500 &
P5=$!

wait $P1 $P2 $P3 $P4 $P5

log "Task A DONE"
