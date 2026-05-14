# REPRODUCE_PAPER.md — Reproducing the empirical claims

This document describes how to reproduce the empirical evaluations reported in the
paper *Grata Cascade: A Hash-Drift CKA Construction*. Three levels of reproduction
are provided, from a 10-minute sanity check to a multi-hour paper-grade rerun.

All commands assume the working directory is the unzipped distribution root.

## Level 0: Sanity check (~30 seconds)

Confirm the distribution runs at all. No paper claim depends on this output.

```
.\bin\EvalSuite.exe smoke
.\bin\EvalSuite.exe nist-selftest
.\bin\EvalSuite.exe nist-reftest
```

`smoke` prints a single derived `K*`. `nist-selftest` exercises the special
functions (incomplete gamma, complementary error function). `nist-reftest`
validates the NIST battery against the official SP 800-22 reference vectors.

## Level 1: Smoke reproduction (~10 minutes)

Reduced-scale version of the paper's reference evaluation. Useful as a pre-flight
check before committing to the full reproduction.

```
.\bin\EvalSuite.exe paper-reproduce
```

This runs:
1. **Convergence**: 100 protocol runs with `configs/reference.json` (~1 min)
2. **NIST**: 100 keys, full SP 800-22 battery (~5 min)
3. **B.1 baseline attacker**: 60-second budget against one captured transcript
   (~1 min)

Output lands in `reports/repro/`. Expected results:
- `convergence.md`: 100% success rate, median ~125 iterations
- `nist.md`: ≥10/12 PASS (reference battery on 100 keys)
- B.1 stdout: 0 / Final-slots matched (attacker fails as expected)

## Level 2: Per-profile validation (~1 hour)

Repeats the active production profiles (post-F11-v2 / F13-v2 cleanup). Each
profile is validated by 200-run convergence + NIST battery on ≥100 keys.

```
foreach ($profile in 'low_resource_iot', 'high_security_pq128', 'reference_h_AB6') {
    Write-Host "=== $profile ==="
    .\bin\EvalSuite.exe convergence `
        --config "configs/$profile.json" `
        --runs 200 `
        --output "reports/$($profile)_convergence.md"

    .\bin\EvalSuite.exe nist-run `
        --config "configs/$profile.json" `
        --runs 100 `
        --output "reports/$($profile)_nist.md" `
        --csv "reports/$($profile)_nist.csv"
}
```

Expected: each profile achieves 100% FH/200 + ≥99% KM/200 (per the F13-v2 /
F11-v2 closures) and ≥10/12 NIST PASS (BMR may be N/A for L=16 due to
insufficient input length — structural, not a regression).

The `mobile_bandwidth` and `high_security_classical` profiles from
earlier paper revisions were removed as part of the 2026-05-04 active-set
cleanup; `reference_h_AB6` is the v3 reference candidate (closes paper Open
Problem `prob:hab-residual` empirically — see paper §7.1.1 and
`reports/refv2/h_AB6_ablation/H_AB6_ablation_report.md`).

## Level 3: Paper-grade reproduction (multi-hour)

Reproduces the full empirical claims at the run counts cited in the paper.
Assumes ~24 hours of wall-clock on reference hardware.

### 3.1 Reference convergence (5×10⁴ runs, F2-v2 baseline)

The paper reports a 5×10⁴-run convergence campaign on reference v2 (paper §7.1).
Reproduce via five 10k shards aggregated into a single summary:

```
foreach ($s in 1..5) {
    .\bin\EvalSuite.exe convergence `
        --config configs/reference.json `
        --runs 10000 `
        --output "reports/conv_10k_s$s.md" `
        --csv "reports/conv_10k_s$s.csv"
}
# Concatenate (preserve header from shard 1, append data from 2..5)
Get-Content reports/conv_10k_s1.csv -Head 1 > reports/conv_50k.csv
1..5 | ForEach-Object { Get-Content "reports/conv_10k_s$_.csv" | Select-Object -Skip 1 } >> reports/conv_50k.csv
.\bin\EvalSuite.exe convergence-summary `
    --csv reports/conv_50k.csv --output reports/conv_50k_summary.md
```

Expected: 100% FH / 50000, ≥99.99% KM / 50000 (4 KM divergences
nominally; Clopper--Pearson upper 95% CI on combined failure rate
2.05×10⁻⁴).

### 3.1.1 H_AB=6 ablation (50k, v3 candidate)

```
foreach ($s in 1..5) {
    .\bin\EvalSuite.exe convergence `
        --config configs/reference_h_AB6.json --runs 10000 `
        --output "reports/h_AB6_s$s.md" --csv "reports/h_AB6_s$s.csv"
}
# Aggregate as in 3.1.
```

Expected: 100% FH / 50000, 100% KM / 50000 (zero divergences); CP upper
95% CI on failure rate ~7.4×10⁻⁵, sitting below the reference v2
baseline of ~8.0×10⁻⁵. Iter median 49 (= reference v2; +1 byte h_AB
does not slow convergence).

### 3.2 NIST validation (1000 keys, reference)

```
.\bin\EvalSuite.exe nist-run `
    --config configs/reference.json `
    --runs 1000 `
    --output reports/nist_ref_1000.md `
    --csv reports/nist_ref_1000.csv
```

Expected: ≥10/12 PASS, no test deteriorating from the 100-key result.

### 3.3 Big-eval attacker battery (B.5-extended, 300 transcripts × 5 attackers)

```
.\bin\EvalSuite.exe b5-extended `
    --config configs/reference.json `
    --runs 300
```

The battery runs five passive adversary classes on a shared transcript corpus:

| Class | Strategy                                                  |
|-------|-----------------------------------------------------------|
| B.1   | Baseline random (uniform L-byte sampling, time-budget)    |
| B.2   | Hash-First filtered (cascaded h_AB+h_BA+h_P prefixes)     |
| B.3   | Tail-informed sampler (Stat-tail biased random)           |
| B.4   | AES-Oracle discriminator (TP/FP rates, Cohen's d)         |
| B.7   | Stat-Sorted Enumeration (deterministic top-K, dual-mode)  |

Expected on reference v2: 0/300 success per class; Clopper-Pearson upper
95% CI 1.22% per class; B.4 oracle Cohen's d ≈ 0.009 (no measurable
discriminator power across 2.05×10¹⁰ queries). Wall-clock ~6.5 hours on
22 worker threads with SHA-NI extensions.

B.7 evaluation against reference v2 is currently deferred; the B.7 row in
the paper (Table tab:attackers) reports the `break_demo` dual-mode
investigation (see paper §sec:adv-b7 and
`reports/refv2/break_demo/break_demo_summary.md`).

### 3.4 Synthetic flood (B.6, F5 validation)

```
.\bin\EvalSuite.exe b6 `
    --config configs/reference.json `
    --k 2 `
    --runs 10 `
    --output-csv reports/b6_k2_10tr.csv
```

Expected: ~300x byte-level filter reduction; residual combinatorial cost remains
infeasible.

## Validating against the bundled reports

Reports produced by these commands should match the historical reports in the
project repository (under `reports/`) within statistical fluctuation. If your
results diverge sharply (e.g. NIST PASS count drops by more than 1, or
convergence rate falls below 99%), check:

1. **CPU SHA-NI**: Run `EvalSuite.exe bench-sha`. Throughput should be in the
   high tens or low hundreds of millions of ops/sec on a SHA-NI CPU; ~2 M ops/sec
   indicates SHA-NI is inactive (older CPU or VM).
2. **Config integrity**: `EvalSuite.exe verify-config configs/reference.json`
   prints the resolved parameters. Compare against the values cited in the
   paper.
3. **Small-N variance**: Convergence runs <200 may show transient deficits; rerun
   with higher N before drawing conclusions.

## Reporting issues

For reproduction discrepancies, capture the report files plus the relevant
`*.log` from `reports/` and open an issue in the project repository.
