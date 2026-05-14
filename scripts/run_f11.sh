#!/usr/bin/env bash
# F11 — Parametric profile calibration of CandidateMax, 4 profiles sequential,
# each using 6 internal workers.
# Tighter safety guards (vs initial run): --max-iter 500 caps non-convergent
# runs at ~5s each (initial run had 5000-iter cap → 60+ min/run on IoT/Mobile
# non-convergent cand values). Expected wall time: ~1-2h total.
set -euo pipefail
cd "$(dirname "$0")/.."

EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
mkdir -p reports/f11

log() { echo "[$(date -Iseconds)] $*" ; }

log "F11 START"

log "[1/4] IoT profile sweep..."
"$EXE" calibrate-f4 \
  --config configs/low_resource_iot.json \
  --cand-values "-40,-55,-70,-85,-100,-115,-130" \
  --cut-values "-8" \
  --runs 100 --workers 6 \
  --max-iter 500 --early-abort-window 15 \
  --memory-guard-mb 1024 \
  --output reports/f11/iot_sweep.md \
  2>&1 | tail -30

log "[2/4] Mobile profile sweep..."
"$EXE" calibrate-f4 \
  --config configs/mobile_bandwidth.json \
  --cand-values "-40,-55,-70,-85,-100,-115,-130" \
  --cut-values "-8" \
  --runs 100 --workers 6 \
  --max-iter 500 --early-abort-window 15 \
  --memory-guard-mb 1024 \
  --output reports/f11/mobile_sweep.md \
  2>&1 | tail -30

log "[3/4] HighSec Classical profile sweep..."
"$EXE" calibrate-f4 \
  --config configs/high_security_classical.json \
  --cand-values "-60,-80,-95,-110,-125" \
  --cut-values "-8" \
  --runs 100 --workers 6 \
  --max-iter 500 --early-abort-window 15 \
  --memory-guard-mb 1024 \
  --output reports/f11/highsec_classical_sweep.md \
  2>&1 | tail -30

log "[4/4] HighSec PQ128 profile sweep..."
"$EXE" calibrate-f4 \
  --config configs/high_security_pq128.json \
  --cand-values "-100,-150,-200,-250,-300" \
  --cut-values "-8" \
  --runs 50 --workers 6 \
  --max-iter 500 --early-abort-window 10 \
  --memory-guard-mb 1024 \
  --output reports/f11/highsec_pq128_sweep.md \
  2>&1 | tail -30

log "F11 DONE"
