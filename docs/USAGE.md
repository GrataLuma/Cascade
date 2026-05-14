# USAGE — Empirical Evaluation Suite

Detailed reference for every command. All commands accept `--help` for inline
documentation and `--config <path>` to specify the JSON configuration file
(default: `configs/reference.json`).

## Common conventions

- Working directory: invoke from the unzipped distribution root so relative paths
  to `configs/` and `reports/` resolve correctly.
- Output: files written under `reports/` (or wherever `--output` / `--output-dir`
  points). Existing files are overwritten unless noted.
- Exit codes: `0` success, `1` runtime error, `2` CLI parse error, `3` protocol
  failed to converge.
- All commands write status to `stderr` and structured output to `stdout` and/or
  files. Pipe-friendly: `command 2>nul` to suppress progress chatter.

## Orchestrator: `EvalSuite.exe`

| Command            | Description                                                  |
|--------------------|--------------------------------------------------------------|
| `list`             | List all subcommands.                                        |
| `paper-reproduce`  | Run convergence + NIST + B.1 baseline as a single pipeline.  |
| `<subcommand>`     | Forwards to `HashFirstAttacker.exe` or `NistTests.exe`.      |

### `EvalSuite.exe paper-reproduce`

Runs a short reproduction pipeline (defaults are smoke-scale; raise counts for
paper-grade runs).

```
EvalSuite.exe paper-reproduce
  [--config <path>]            Default: configs/reference.json
  [--output-dir <path>]        Default: reports/repro
  [--convergence-runs <N>]     Default: 100
  [--nist-keys <N>]            Default: 100
  [--b1-budget <seconds>]      Default: 60
```

For paper-grade reproduction (50k convergence, 1000 NIST keys, 5-attacker
B.5-extended battery × 300 transcripts) see **REPRODUCE_PAPER.md**.

## Protocol commands (HashFirstAttacker.exe)

### `smoke`
Quick end-to-end protocol run. No CSV output; prints `K*` and timing.

### `convergence --runs N --output <path>`
Runs the protocol N times and writes a markdown report with success rate, iteration
distribution, and timing percentiles.

### `calibrate-f4`
Sweeps `cut_limit_probability_log2` × `candidate_max_probability_log2` and reports
which threshold combinations preserve convergence.

### `calibrate-p-upd`
Sweeps `update_probability` (default values: 0.55, 0.65, 0.75, 0.85, 0.95) and
reports convergence + average password distribution properties for each.

### `verify-config <path>`
Validates a JSON config file (schema, ranges, consistency) and prints the resolved
parameters. No protocol runs.

### `verify-sha`
Recomputes the SHA-256 outputs of golden reference vectors (committed in the
repository under `EmpiricalEvaluation/reports/golden/`) and verifies bit-exact
match with the embedded hashes. Used to validate cross-runtime/cross-CPU SHA
implementations.

### `bench-sha`
Microbenchmark: SHA-256 hashes per second on the current CPU (per-thread). Use to
confirm whether SHA-NI hardware extensions are active.

## Attackers (HashFirstAttacker.exe)

### `b1 --budget-seconds <S>`
Baseline random attacker. Samples uniformly random vectors; tests against captured
transcript. Useful as a sanity floor on attacker capability.

### `b2 --runs <N>`
Hash-first filtered attacker. Builds a candidate pool, filters by intermediate
hash matches, and tries to recover `K*`.

### `b3 --runs <N>`
Structured-sampling attacker. Uses Stat distribution to bias candidate selection.

### `b4 --runs <N>`
AES-Oracle discriminator. Uses AES decryption oracle to score candidates without
attempting full recovery. Reports Cohen's d, TP/FP rates per round.

### `b5 --runs <N>`
Big-eval pipeline: runs b2 against N independent transcripts and aggregates
success rate with Clopper-Pearson confidence intervals.

### `b5-extended --runs <N>`
Same as b5 plus throughput logging (samples/sec per worker, SHA-256 ops/sec) for
performance analysis.

### `b6 --k <K> --runs <N> --output-csv <path>`
F5 Synthetic flood attacker. Generates K candidates per slot via byte-level
synthesis; measures filter pass rate and post-filter combinatorial cost.

## NIST suite (NistTests.exe)

### `nist-collect --runs N --output <path>`
Collects N protocol-derived keys to a binary file (one key per `Configuration.AesKeyLengthBytes`
slot). Used as input to subsequent `nist-run` calls.

### `nist-run --runs N --output <path> --csv <path>`
Runs the NIST SP 800-22 statistical test battery on N keys. If `--keys-file` is
not provided, collects fresh keys first. Default battery: 12 tests
(Frequency, BlockFrequency, Runs, LongestRunOfOnes, BinaryMatrixRank, DFT,
NonOverlappingTemplate, OverlappingTemplate, ApproximateEntropy, CumulativeSums,
RandomExcursions, RandomExcursionsVariant). For configurations where a test is
not applicable (e.g. BinaryMatrixRank requires sufficient bits), the test is
marked **N/A** in the report rather than failed.

### `nist-selftest`
Self-test of NIST helper functions (incomplete gamma, error function, etc.).

### `nist-reftest`
Validates the implementation against the official NIST SP 800-22 reference
vectors. Should pass on all platforms.

## Configurations

See **config_format.md** for the JSON schema. Pre-built configs in `configs/`:

| File                          | Vector L | Pool N | Notes                                              |
|-------------------------------|----------|--------|----------------------------------------------------|
| `reference.json`              | 32       | 4096   | Paper reference v2 parameters                      |
| `reference_h_AB6.json`        | 32       | 4096   | v3 reference candidate (h_AB=5→6 ablation)         |
| `low_resource_iot.json`       | 16       | 1024   | IoT profile (F13-v2)                               |
| `high_security_pq128.json`    | 64       | 8192   | High-security post-quantum (F11-v2)                |
| `break_demo.json`             | 8        | 256    | Research-only break-demo (paper §sec:adv-b7)       |
| `f12_s*.json`                 | 32       | 4096   | SeedMinMax sweep (F12, historical)                 |

To create your own profile, copy `reference.json`, edit the `protocol` block,
then validate with `EvalSuite.exe verify-config configs/myprofile.json`.

## Tips

- For long-running campaigns redirect stderr to a log file:
  `EvalSuite.exe b5-extended --runs 300 2> reports/b5_extended.log`
- Most commands honor an internal worker count via the `--workers N` flag where
  parallelism applies.
- For reproducible runs use `--seed <int>` where supported.
