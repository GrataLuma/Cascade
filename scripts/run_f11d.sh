#!/usr/bin/env bash
# F11.d — Validation runs for the 4 calibrated profiles.
# Two passes per profile: (1) 200-run convergence, (2) 100 NIST keys + test battery.
# Profiles run sequentially; within each, convergence and NIST run sequentially too.
# Expected wall time: ~30-45 min total.
# NIST exits non-zero when any test fails — that's a DATA SIGNAL, not a script error.
# We capture exit codes explicitly rather than relying on pipefail.
set -u
cd "$(dirname "$0")/.."

ATTACKER_EXE="./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe"
NIST_EXE="./EmpiricalEvaluation/NistTests/bin/Debug/net9.0/NistTests.exe"
mkdir -p reports/f11/validation

log() { echo "[$(date -Iseconds)] $*" ; }

log "F11.d START"

for profile in low_resource_iot mobile_bandwidth high_security_classical high_security_pq128; do
  log "[$profile] convergence 200 runs..."
  "$ATTACKER_EXE" convergence \
    --config "configs/${profile}.json" \
    --runs 200 \
    --output "reports/f11/validation/${profile}_convergence.md" \
    > "reports/f11/validation/${profile}_convergence.stdout" 2>&1
  conv_rc=$?
  log "  [$profile] convergence exit=$conv_rc"
  tail -10 "reports/f11/validation/${profile}_convergence.stdout" | sed 's/^/    /'

  log "[$profile] NIST 100 keys..."
  "$NIST_EXE" run \
    --config "configs/${profile}.json" \
    --runs 100 \
    --max-iter 500 \
    --output "reports/f11/validation/${profile}_nist.md" \
    --csv "reports/f11/validation/${profile}_nist.csv" \
    > "reports/f11/validation/${profile}_nist.stdout" 2>&1
  nist_rc=$?
  log "  [$profile] NIST exit=$nist_rc (0=all PASS, 3=one+ FAIL, other=error)"
  tail -8 "reports/f11/validation/${profile}_nist.stdout" | sed 's/^/    /'
done

log "F11.d DONE"
