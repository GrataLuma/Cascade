#!/usr/bin/env bash
# F_iot_5 + F_iot_6 — F13-v2 IoT (L=16) re-validation under reference v2.
# Config: configs/low_resource_iot.json (v2 update: s=4, CandMax=-8 disabled, MaxIter=500).
# Acceptance: convergence 200/200 FH AND KM; NIST >=10/12 PASS (BMR likely N/A for L=16).
# Expected wall time: ~5 min (convergence) + ~2 min (NIST).
set -u
cd "$(dirname "$0")/.."

ATTACKER_EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
NIST_EXE="./EmpiricalEvaluation/NistTests/bin/Debug/net9.0/NistTests.exe"
CONFIG="configs/low_resource_iot.json"
OUT_DIR="reports/refv2/f13"
mkdir -p "$OUT_DIR"

log() { echo "[$(date -Iseconds)] $*" ; }

log "F13-v2 validation START (IoT L=16 under reference v2)"

log "[F_iot_5] convergence 200 runs..."
"$ATTACKER_EXE" convergence \
  --config "$CONFIG" \
  --runs 200 \
  --output "$OUT_DIR/iot_convergence.md" \
  > "$OUT_DIR/iot_convergence.stdout" 2>&1
conv_rc=$?
log "  convergence exit=$conv_rc"
tail -10 "$OUT_DIR/iot_convergence.stdout" | sed 's/^/    /'

log "[F_iot_6] NIST 100 keys..."
"$NIST_EXE" run \
  --config "$CONFIG" \
  --runs 100 \
  --max-iter 500 \
  --output "$OUT_DIR/iot_nist.md" \
  --csv "$OUT_DIR/iot_nist.csv" \
  > "$OUT_DIR/iot_nist.stdout" 2>&1
nist_rc=$?
log "  NIST exit=$nist_rc (0=all PASS, 3=one+ FAIL)"
tail -12 "$OUT_DIR/iot_nist.stdout" | sed 's/^/    /'

log "F13-v2 validation DONE (convergence_rc=$conv_rc, nist_rc=$nist_rc)"
