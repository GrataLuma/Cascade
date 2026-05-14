#!/usr/bin/env bash
# F11-v2 / high_security_pq128 — re-validation under reference v2.
# Config: configs/high_security_pq128.json
#   (L=64, N=8192, h_AB=6, h_BA=3, h_P=9, M=16, s=4, CandMax=-8 disabled, MaxIter=500;
#    lambda = 144 bits classical, 144 B/round Final BW)
# Acceptance: convergence 200/200 FH AND KM; NIST >=10/12 PASS.
# Expected wall time: ~17 min (convergence) + ~3 min (NIST).
set -u
cd "$(dirname "$0")/.."

ATTACKER_EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
NIST_EXE="./EmpiricalEvaluation/NistTests/bin/Debug/net9.0/NistTests.exe"
CONFIG="configs/high_security_pq128.json"
OUT_DIR="reports/refv2/f11/pq128"
mkdir -p "$OUT_DIR"

log() { echo "[$(date -Iseconds)] $*" ; }

log "F11-v2 pq128 validation START"

log "[conv] convergence 200 runs..."
"$ATTACKER_EXE" convergence \
  --config "$CONFIG" \
  --runs 200 \
  --output "$OUT_DIR/pq128_convergence.md" \
  > "$OUT_DIR/pq128_convergence.stdout" 2>&1
conv_rc=$?
log "  convergence exit=$conv_rc"
tail -10 "$OUT_DIR/pq128_convergence.stdout" | sed 's/^/    /'

log "[nist] NIST 100 keys..."
"$NIST_EXE" run \
  --config "$CONFIG" \
  --runs 100 \
  --max-iter 500 \
  --output "$OUT_DIR/pq128_nist.md" \
  --csv "$OUT_DIR/pq128_nist.csv" \
  > "$OUT_DIR/pq128_nist.stdout" 2>&1
nist_rc=$?
log "  NIST exit=$nist_rc (0=all PASS, 3=one+ FAIL)"
tail -12 "$OUT_DIR/pq128_nist.stdout" | sed 's/^/    /'

log "F11-v2 pq128 validation DONE (convergence_rc=$conv_rc, nist_rc=$nist_rc)"
