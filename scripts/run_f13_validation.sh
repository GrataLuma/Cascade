#!/usr/bin/env bash
# F13.b + F13.c — Validation for IoT profile post h_P=4 fix.
# Expected wall time: ~5 min (convergence) + ~2 min (NIST).
set -u
cd "$(dirname "$0")/.."

ATTACKER_EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
NIST_EXE="./EmpiricalEvaluation/NistTests/bin/Debug/net9.0/NistTests.exe"
mkdir -p reports/f13

log() { echo "[$(date -Iseconds)] $*" ; }

log "F13 validation START (IoT h_P=4, cand=-40)"

log "[F13.b] convergence 200 runs..."
"$ATTACKER_EXE" convergence \
  --config "configs/low_resource_iot.json" \
  --runs 200 \
  --output "reports/f13/iot_convergence.md" \
  > "reports/f13/iot_convergence.stdout" 2>&1
conv_rc=$?
log "  convergence exit=$conv_rc"
tail -10 "reports/f13/iot_convergence.stdout" | sed 's/^/    /'

log "[F13.c] NIST 100 keys..."
"$NIST_EXE" run \
  --config "configs/low_resource_iot.json" \
  --runs 100 \
  --max-iter 500 \
  --output "reports/f13/iot_nist.md" \
  --csv "reports/f13/iot_nist.csv" \
  > "reports/f13/iot_nist.stdout" 2>&1
nist_rc=$?
log "  NIST exit=$nist_rc (0=all PASS, 3=one+ FAIL)"
tail -12 "reports/f13/iot_nist.stdout" | sed 's/^/    /'

log "F13 validation DONE"
