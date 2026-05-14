# Config File Format (F9)

This document describes the JSON config schema used by all CLI modules in
`EmpiricalEvaluation.sln`. Any change to protocol parameters for a given run
is expressed by supplying a different config file via `--config <path>`. No
recompilation is required.

**Schema version:** `1.0`. A hard reject fires if a config declares any other
version; breaking changes will bump to `"2.0"` with a migration tool.

## Invocation

```
HashFirstAttacker.exe <command> --config configs/reference.json [other options]
NistTests.exe <command> --config configs/reference.json [other options]
```

If `--config` is omitted, the loader searches for `configs/reference.json` in:
1. The current working directory (`{CWD}/configs/reference.json`)
2. The executable directory (`{exe_dir}/configs/reference.json`)
3. Up to 5 parent directories walked upward from the CWD

If none exist, the module fails with `FileNotFoundException` listing every
path that was tried. The built-in `ProtocolConfiguration.Reference` (hardcoded
default) is *not* used as an implicit fallback — every report must reference a
concrete file on disk for auditability.

Every module's markdown report embeds the full JSON body of the config used
for that run, under a `### Configuration` section, so a reviewer can reproduce
or audit the run without access to the original `configs/` directory.

## Structure

```json
{
  "schema_version": "1.0",
  "name": "reference",
  "description": "free-form human-readable summary",
  "calibration_status": "finalized_f4_f10",
  "protocol": {
    "vector_length": 32,
    "vector_count": 4096,
    "hash_length_ab": 5,
    "hash_length_ba": 2,
    "hash_length_password": 4,
    "seed_min_max": 3,
    "aes_passed_vectors_count": 16,
    "update_probability": 0.75,
    "termination_threshold_log2": -512,
    "cut_limit_probability_log2": -8,
    "candidate_max_probability_log2": -95,
    "cut_limit_safety_attempts": 1000
  },
  "rng": {
    "type": "crypto",
    "seed": null
  }
}
```

### Top-level fields

| Field                | Type   | Required | Notes                                                                 |
|----------------------|--------|----------|-----------------------------------------------------------------------|
| `schema_version`     | string | yes      | Must equal `"1.0"`. Hard reject otherwise.                            |
| `name`               | string | yes      | Non-empty. Convention: matches the filename stem.                      |
| `description`        | string | no       | Free-form.                                                            |
| `calibration_status` | string | no       | Informational: `finalized_f4_f10`, `placeholder_f11`, `placeholder_f8`. Unknown keys are ignored by the parser. |
| `protocol`           | object | yes      | Protocol parameters — see below.                                      |
| `rng`                | object | yes      | RNG configuration — see below.                                        |

### `protocol` block

Unless marked SOFT, all ranges are hard-enforced at load time.

| Field                             | Type   | Range             | Meaning / impact |
|-----------------------------------|--------|-------------------|------------------|
| `vector_length`                   | int    | [8, 64]           | Bytes per vector (`L` in the paper). Larger = wider Stat distribution, stronger but more bandwidth. |
| `vector_count`                    | int    | [64, 65536]       | Vector pool size (`N`). Drives memory footprint (~`L × N` bytes per client). |
| `hash_length_ab`                  | int    | [1, 64]           | Bytes per A→B hash. Dominant per-round bandwidth term. |
| `hash_length_ba`                  | int    | [1, 64]           | Bytes per B→A hash. |
| `hash_length_password`            | int    | [1, 64]           | Bytes per Final-slot password hash. Sets per-slot brute-force cost (2^(8·v)). |
| `seed_min_max`                    | int    | [1, 64]           | Controls R seed variation per round. F12 sweeps up to `s=30`. |
| `aes_passed_vectors_count`        | int    | [1, vector_count] | Vectors entering the AES password derivation per round. |
| `update_probability`              | double | **[0.6, 0.95]** (HARD) | Per-vector update probability `p_upd`. Outside this band the protocol either fails to converge (`< 0.6`) or suffers Stat distribution collapse (`> 0.95`). Empirically calibrated in F10. Stored internally as `NoUpdateLimit = 1 − p_upd`. |
| `termination_threshold_log2`      | double | [-8192, 0]        | log2 target probability for convergence. Lower = stricter. `-512` is the reference. |
| `cut_limit_probability_log2`      | double | [-256, 0] SOFT    | log2 threshold for rejecting over-typical vectors during pool fill. `-8` is a passive safety net; values << -8 begin starving the pool. |
| `candidate_max_probability_log2`  | double | [-256, 0] SOFT    | log2 threshold for excluding over-typical collision vectors from password candidate selection. `-95` is the reference optimum (F4.c). Optimum depends on `L`, `N` — F11 recalibrates per profile. |
| `cut_limit_safety_attempts`       | int    | [1, 100000]       | Per-round fill retry cap before the CutLimit filter gives up and accepts unfiltered. |

**Consistency rule (HARD):** `cut_limit_probability_log2 >= candidate_max_probability_log2`. The cut filter must be looser than candidate selection; inverting them starves the pool without tightening the candidate set.

### `rng` block

| Field  | Type          | Required | Notes |
|--------|---------------|----------|-------|
| `type` | string        | yes      | Must equal `"crypto"` in schema 1.0 (post-F1, only the crypto RNG is supported). |
| `seed` | int64 or null | yes      | `null` = non-deterministic (OS CSPRNG). Non-negative integer = deterministic seed for repro. |

## Pre-built configs

`configs/reference.json` is the canonical reference instance from paper v4
empirical evaluation, with `candidate_max_probability_log2` and
`update_probability` values finalized after F4 and F10.

Active production profiles (post-F11-v2 / F13-v2 cleanup; per
`docs/glossary.md` §11):

- `configs/reference.json` — paper reference v2, L=32, N=4096, λ=120 bits.
- `configs/low_resource_iot.json` — IoT, L=16, N=1024 (calibrated_f13_v2).
- `configs/high_security_pq128.json` — post-quantum, L=64, N=8192,
  M·h_P=144 B/round (calibrated_f11_v2).

Research-only / candidate configs:

- `configs/reference_h_AB6.json` — v3 reference candidate, L=32, N=4096,
  h_AB=5→6 (λ=128 bits, +4 KB/round). Closes paper Open Problem
  `prob:hab-residual` empirically (research_h_AB6_ablation status; promotion
  to active production reference pending v3 cutover decision).
- `configs/break_demo.json` — research-only weakened config (L=8, N=256,
  λ=56 bits, p_upd=1.0) cited in paper §sec:adv-b7 for the B.7 dual-mode
  attacker investigation. Not for production.

The `mobile_bandwidth` and `high_security_classical` profiles from earlier
paper revisions, the `crack_*bit.json` calibration probes, and the `f8_*`
grid-sweep configs were removed as part of the 2026-05-04 active-set
cleanup. F12 sweep configs (`f12_s*.json`) are retained as historical
artefacts.

## Validating your own config

Run the built-in validator:

```
HashFirstAttacker.exe verify-config --config path/to/my.json
```

It loads the file, runs all schema 1.0 validation, prints every resolved
field, and exits 0 on success or non-zero with an explicit error message
on failure. Typical errors:

- `InvalidDataException: unsupported schema_version 'x', expected '1.0'` — bump to `"1.0"` or wait for a future migration.
- `ArgumentOutOfRangeException: 'protocol.update_probability' = 0.5 is out of range [0.6, 0.95]` — the value violates the F10 operational band. Either adopt a value inside the band, or justify the exception and bump the schema to relax the range.
- `InvalidDataException: inconsistent thresholds: cut_limit_probability_log2=X must be >= candidate_max_probability_log2=Y` — swap or adjust the thresholds.

## Known limitations

- **Legacy bridge pattern (F9.c alternative).** Protocol classes (`ClientA`, `ClientB`, `Vector`, `Stat`, `SeedProvider`) still read parameters from the `Configuration.*` static fields. `ConfigLoader.Load` calls `Configuration.LoadFrom(cfg)` at startup to copy the `ProtocolConfiguration` instance into those statics. Consequence: **the current process can only hold one active config at a time.** For parametric evaluations that compare two or more configs, launch **separate worker processes** — one per config — rather than attempting to sweep configs inside a single process. The full dependency-injection refactor (each protocol class taking a `ProtocolConfiguration` in its constructor) is scheduled as **F13**, low priority. The bridge pattern is adequate for the present "one process = one config" deployment model used by every current module and by the planned `EvalSuite.exe` orchestrator (F7).

## What is *not* currently configurable (F9 scope limits)

- **Asymmetric A/B configurations.** F9 assumes `ClientA` and `ClientB` share all parameters; they differ only by their random init state. Per-side overrides are a larger protocol change (out of scope).
- **Config inheritance (`extends`).** Each file is self-contained. Composition patterns can be added in schema 2.0 if needed.
- **Multiple RNG back-ends.** Schema 1.0 only supports `"crypto"`. Pre-F1 insecure RNGs are permanently removed.
- **Parameter sweeps.** A single config = a single run. Batch orchestration across many configs is the responsibility of `EvalSuite.exe` (F7).
