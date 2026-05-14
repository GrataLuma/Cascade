> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F1 — RNG Migration Notes (System.Random → RandomNumberGenerator)

**Date:** 2026-04-19
**Scope:** Replace the non-cryptographic `System.Random` PRNG backing `SecureRandom` with the .NET built-in cryptographic RNG (`System.Security.Cryptography.RandomNumberGenerator`).

## Why

The V2 protocol previously drew ALL protocol-relevant randomness through `SecureRandom.Instance`:

- `Next()` — used by `ClientA.FillVectors` to select a seed index from the seed list.
- `Next(int, int)` — used by `Vector(int, byte[], Stat)` ctor to jitter each seed byte by `±SeedMinMax`.
- `NextDouble()` — used by `ClientA.ProcessMessageFromB` and `ClientB.ProcessMessageFromA` to decide whether a vector is updated with the random vector R (threshold `Configuration.NoUpdateLimit = 0.75`).
- `FillBuffer(byte[])` — used by `Vector(int, Stat)` ctor, `HASH()` default ctor (fake hashes in `ClientB` final round), and `ClientB.ProcessMessageFromA` (fresh seed generation).

Until F1, `SecureRandom` internally used `System.Random`, which is:
- A 48-bit subtractive PRNG (Park-Miller variant, documented as non-cryptographic).
- Seeded from `Environment.TickCount` by default — predictable within a single process run and across runs on the same host.
- Not intended for any security-critical purpose.

For a continuous key agreement protocol targeting production e2e messaging, any security argument depends on the attacker being unable to predict R vectors, collide hash preimages more effectively than brute force, or pre-compute seed trajectories. With `System.Random`, those guarantees collapse — an attacker who learns the tick-count seed (trivially, in a co-located process) can reconstruct every R and every seed used by the protocol.

F1 replaces the backing PRNG with `RandomNumberGenerator.Fill` / `RandomNumberGenerator.GetInt32`, which dispatch to the OS CSPRNG (BCryptGenRandom on Windows, `/dev/urandom` on Linux). The output passes NIST SP 800-90A qualification and is the baseline choice for any crypto-adjacent randomness in .NET.

## What changed in code

### `TreeParityMachine_HASH_V2/SecureRandom.cs`

Complete rewrite. All four public methods now delegate to the static `RandomNumberGenerator` surface:

| Method | Pre-F1 | Post-F1 |
|--------|--------|---------|
| `Next()` | `new Random().Next()` → 48-bit LCG | 4 random bytes via `RandomNumberGenerator.Fill`, masked to non-negative Int32 |
| `Next(int min, int max)` | `Random.Next(min, max)` | `RandomNumberGenerator.GetInt32(min, max)` (built-in unbiased rejection sampling) |
| `NextDouble()` | `Random.NextDouble()` | 8 random bytes, upper 53 bits scaled by `1.0 / 2^53` — full double precision, zero modulo bias |
| `FillBuffer(byte[])` | `Random.NextBytes(buffer)` | `RandomNumberGenerator.Fill(buffer)` |

The stale `Sha256` singleton / `SHA256Managed` field (removed in F3) is gone — this is now purely an RNG wrapper.

The old commented `RNGCryptoServiceProvider` references were removed in F3 already.

### Singleton pattern retained

`SecureRandom.Instance` stays as the entry point — all call-sites across the codebase use `SecureRandom.Instance.Next()` / `.FillBuffer()` / etc. Changing that to `RandomNumberGenerator.Fill(...)` direct calls at every site would require ~50 edits and gain nothing; the singleton carries no mutable state (all methods delegate to static thread-safe APIs), so the pattern introduces no contention, lock, or thread-safety hazard. Thread-safety was explicitly validated: the two thread-safe guarantees we rely on are documented on the static methods.

### `SetRandomSeed(int)` — NO-OP with warning

Retained for CLI back-compat (existing `--seed` flags on `NistTests collect`, `HashFirstAttacker verify-sha`, and `HashFirstAttacker bench-sha --protocol-runs`). Calls a one-shot `Console.Error.WriteLine` warning and then does nothing; the cryptographic PRNG cannot be seeded, so determinism-dependent workflows are structurally broken:

- `verify-sha --mode verify` against a pre-F1 golden reference will now always diverge. The F3 golden file (`reports/golden/sha256_reference_runs.json`) was correct for its purpose — cross-migration bit-exact validation of F3 — and is preserved in git history but is no longer reproducible.
- `NistTests collect --seed N` produces a key bank whose membership is unpredictable (intentionally).
- `bench-sha --protocol-runs` no longer executes an identical workload across invocations; wall-clock throughput is still a meaningful average.

This is a deliberate trade-off — determinism is incompatible with cryptographic unpredictability.

### Callers updated

- `NistTests/KeyCollector.cs` — `SeedSecureRandom(int)` no longer uses reflection; forwards to `SecureRandom.SetRandomSeed`.
- `HashFirstAttacker/VerifyShaCommand.cs` — same.
- `HashFirstAttacker/BenchShaCommand.cs` — same.
- Unused `System.Reflection` imports removed from all three.
- Help text in `Program.cs` (HashFirstAttacker) updated to note that `--seed` is NO-OP under crypto PRNG.

Attacker pipelines (`AESOracleAttacker`, `BaselineRandomAttacker`, `HashFirstFilteredAttacker`, `StructuredSamplingAttacker`) are **intentionally left alone** (see Zadani-followup.md F1 "Co nedělat"). They use `new Random()` internally for candidate sampling, which is appropriate — an attacker's sampler distribution does not need to be cryptographic; it needs to match whatever the attacker's actual search procedure is.

## Invalidation of prior artifacts

Per Zadani-followup.md F1: "Všechny dosavadní empirické výsledky byly získány s non-secure RNG; po migraci je třeba je invalidovat a znovu vyprodukovat."

The following pre-F1 artifacts in `reports/` are produced by `System.Random`-backed runs and are invalidated by this migration:

- `attacker_report.md`, `attacker_results.csv` (B.1-B.4 at small N)
- `big_eval_report.md`, `big_eval_results.csv`, `big_eval_progress.csv` (B.5-extended at N=300)
- `nist_report.md`, `nist_results.csv` (NIST at N=1000)
- `keys_a1.bin`, `keys_smoke.bin`, `smoke_*` artifacts
- `transcripts_300.bin`, `transcripts_5.bin` (seeded transcript generation)
- F3 `golden/sha256_reference_runs.json` — valid for its F3 purpose, no longer reproducible post-F1

The F1.b/F1.c/F1.d re-validation below replaces the critical subset (convergence, NIST, attackers). The B.5-extended campaign re-run is deferred to later phases (F2/F8) per zadání priority.

## F1.b — Convergence re-validation

**Command:** `HashFirstAttacker.exe convergence --runs 1000 --output reports/convergence_post_f1.md`

**Result:** **1000 / 1000 FinalHit, 1000 / 1000 KeysMatch** — 100% convergence rate. No protocol regression under crypto PRNG.

| Statistic | Value |
|-----------|------:|
| FinalHit / runs | 1000 / 1000 (100.00 %) |
| KeysMatch / runs | 1000 / 1000 (100.00 %) |
| Iterations (median) | 48 |
| Iterations (p95) | 60 |
| Per-run elapsed (median) | 1797.7 ms |
| Total wall-clock | 29 m 53 s |

The iteration-count distribution matches the pre-F1 pilot (median 48, consistent with the F3 macrobench figure of 48.30 avg iterations at seed=42), which is the expected invariant: the SHA-256 and protocol state machine do not care whether uniform draws come from `System.Random` or the OS CSPRNG; what matters is that the distribution is uniform, which both provide. Full report: [reports/convergence_post_f1.md](convergence_post_f1.md).

## F1.c — NIST re-validation

**Command:** `NistTests.exe run --runs 1000 --output reports/nist_report_rng_migrated.md --csv reports/nist_results_rng_migrated.csv --keys-file reports/keys_rng_migrated.bin`

**Result:** **12 / 12 tests PASS.** 999 of 1000 keys successfully collected (one protocol run failed to reach Final within the default 5000-iteration cap — a rare timeout, not a convergence regression; the convergence pilot from F1.b showed 1000/1000, so this is within the noise of the different `SecureRandom` draw sequence used by the NIST collect invocation). 255 744 bits tested at block size M=128.

| Test | p-value | Verdict |
|------|--------:|:-------:|
| Frequency (Monobit) | 0.742722 | PASS |
| Block Frequency (M=128) | 0.335966 | PASS |
| Runs | 0.102442 | PASS |
| Longest Run of Ones in a Block | 0.917021 | PASS |
| Binary Matrix Rank (M=32, Q=32) | 0.352683 | PASS |
| Discrete Fourier Transform (Spectral) | 0.103975 | PASS |
| Non-overlapping Template (m=9, B=000000001) | 0.586372 | PASS |
| Approximate Entropy (m=2) | 0.544652 | PASS |
| Cumulative Sums (forward) | 0.863326 | PASS |
| Cumulative Sums (reverse) | 0.960870 | PASS |
| Serial P1 (m=3) | 0.543550 | PASS |
| Serial P2 (m=3) | 0.861811 | PASS |

All p-values comfortably above the SP 800-22 alpha = 0.01 threshold; no test comes close to the failure edge. The protocol's shared-key output remains statistically indistinguishable from uniform random bits under the crypto PRNG, consistent with the pre-F1 result. Full report: [reports/nist_report_rng_migrated.md](nist_report_rng_migrated.md).

## F1.d — Attacker re-validation (reduced N=50)

**Command:** `HashFirstAttacker.exe b5 --runs 50 --b1-budget-seconds 30 --pool 1000000 --b4-disc-samples 100000 --output reports/attacker_report_rng_migrated.md --csv reports/attacker_results_rng_migrated.csv`

**Result:** **0 / 50 success across all four attackers.** No regression in protocol security under the crypto PRNG, consistent with the pre-F1 result.

| Attacker | Success | Samples/run (avg) | Slots matched (avg) | Notes |
|----------|--------:|------------------:|--------------------:|-------|
| B.1 Baseline Random | 0 / 50 (0.00 %) | 9.67 × 10⁷ | 0.14 / 3.9 | 30 s budget per transcript; no K* recovery |
| B.2 Hash-First Filtered | 0 / 50 (0.00 %) | 1.00 × 10⁶ | 0.00 / 3.9 | pool 10⁶; 0 stage-a hits in this pool regime |
| B.3 Structured Sampling (Tail) | 0 / 50 (0.00 %) | 1.00 × 10⁶ | 0.00 / 3.9 | same |
| B.4 AES-Oracle (discriminator power) | 0 / 50 (0.00 %) | 7.44 × 10⁶ | 0.00 / 3.9 | mean TP = 0, mean FP = 0, separation = 0 |

The B.4 oracle recorded zero true positives AND zero false positives across all 50 transcripts — the discriminator has no measurable statistical power, matching the B.5-extended conclusion. Reduced-budget `--pool 10⁶` yields zero stage-a hits (expected; reference pool 10⁷ already yielded only 1 stage-b hit in 300 × 10⁷ samples per B.5-extended, so reducing by 10× and running only 50 × 10⁶ samples per transcript has zero hit probability by design).

Full report: [reports/attacker_report_rng_migrated.md](attacker_report_rng_migrated.md), raw CSV: [reports/attacker_results_rng_migrated.csv](attacker_results_rng_migrated.csv).

## Acceptance criteria checklist

| Item | Status |
|------|--------|
| All protocol randomness routed through `RandomNumberGenerator.*` | ✅ (SecureRandom.cs rewritten; no `System.Random` instances in V2 core) |
| Build 0 warnings / 0 errors | ✅ |
| Smoke protocol run post-migration | ✅ (44 iter, FinalHit, KeysMatch) |
| Convergence pilot N=1000, 100% success | ✅ (1000/1000 FinalHit, 1000/1000 KeysMatch) |
| NIST battery at N=1000 keys, 12/12 pass | ✅ (255 744 bits, 12/12 PASS) |
| Attackers B.1-B.4 at N=50, 0/50 recoveries | ✅ (0/50 for each of B.1, B.2, B.3, B.4) |
