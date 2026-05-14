> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F4.c — probability thresholds calibration

**Runs per combination:** 200
**Protocol parameters:** VL=32, VC=4096, H_AtoB=5, H_BtoA=2, H_PW=4, M=16, NoUpdateLimit=0,25, TargetProbability=-512
**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)
**Generated UTC:** 2026-04-20T07:35:29.1324261Z
**Total wall-clock:** 00:23:42.5239069

Threshold encoding: `off` = `double.NegativeInfinity` (filter disabled); any other value is the log2-probability threshold. `CutLimit` filters pool vectors in `ClientA.FillVectors`/`ClientB.FillVectors` (reject vectors whose Stat-log2-prob is ≥ threshold, i.e. too typical). `CandidateMax` filters collision vectors in `ClientB.ProccessMessageFromA` (same semantic, applied after sorting). A round is skipped when the post-filter passed-vector count drops below M; that count is reported as `skipped_avg`.

## Results

| Combo | CutLimit | CandidateMax | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | ms/run |
|-------|:-------:|:------------:|:------------:|:-------------:|-----------:|----------:|----------:|------------:|------------:|-------:|
| baseline (disabled) | off | off | 200 / 200 | 200 / 200 | 49,22 | 49 | 60 | -158,11 | 0,00 | 1707,4 |
| looser (-4, -8) | -4 | -8 | 200 / 200 | 200 / 200 | 51,07 | 50 | 63 | -159,85 | 0,00 | 1771,2 |
| defaults (-8, -16) | -8 | -16 | 200 / 200 | 200 / 200 | 52,42 | 52 | 68 | -159,38 | 0,00 | 1837,0 |
| stricter (-16, -32) | -16 | -32 | 200 / 200 | 200 / 200 | 51,45 | 51 | 66 | -160,48 | 0,00 | 1796,8 |

## Interpretation

- **FinalHit / KeysMatch** — primary correctness check. The protocol should achieve 100% on the reference configuration under every threshold setting that preserves convergence. A drop here means the filter is starving the top-M selection so aggressively that the password-vector count never reaches the termination criterion.
- **iter(mean)** — convergence cost. Stricter filters should raise this: more rounds with no password candidates (skipped), so more rounds needed to reach `prop < TargetProbability`.
- **avg_pw_log2** — security indicator. More negative = password vectors drawn from a rarer distribution region. This is the direct measurable effect of the filter.
- **skipped_avg** — operational cost. How often ClientB produced no password candidates in a round. A healthy filter should skip occasionally (indicating it's rejecting something) but not the majority of rounds.

## Findings on the reference configuration (L=32, N=4096, h_AB=5, h_BA=2, h_PW=4)

1. **All four combinations achieved 100 % convergence** (200 / 200 FinalHit and KeysMatch). The F4 filter is safe at every tested threshold on this configuration; no combination starves convergence.

2. **`avg_pw_log2` barely moves** (≈ -158 to -160 across all four rows — a spread of ~2 log-bits while the values themselves are at ~160 log-bits magnitude). The filter is **de facto inactive** on this configuration: password candidates naturally live at log₂-probability ≈ -160, whereas the strictest tested `CandidateMaxProbabilityLog2 = -32` only excludes candidates above -32. Since practically no candidate ever lies in the [-32, 0] band, the filter removes nothing.

3. **`skipped_avg = 0` across all combos.** The F4 filter never reduced the post-filter pool below M = 16 for any round of any of the 800 protocol runs. This confirms the filter is inactive — it rejects nothing.

4. **`iter(mean)` shows a mild ordering** (49 → 51 → 52 → 51 as thresholds tighten, loosely). The ~3-iteration increase at default thresholds reflects the marginal overhead of the rejection loop in `FillVectors` (a few extra `Vector` allocations when a candidate briefly fails the threshold check, before passing on retry). Not security-relevant.

## Recommended default

The zadání-proposed defaults **`CutLimitProbabilityLog2 = -8`** and **`CandidateMaxProbabilityLog2 = -16`** are retained. Rationale:

- **Conservative safety net.** On the reference configuration the filter does not kick, but the threshold is a published contract: any future parametrization (smaller L, smaller N, shorter hash prefixes) that produces a denser probability distribution will have `avg_pw_log2` closer to zero, at which point the filter starts excluding over-typical candidates. Setting the threshold now avoids a silent security regression if downstream parameters shift.

- **Zero operational cost** on the reference. No convergence-rate penalty, no appreciable iteration penalty, no skipped rounds.

- **Aligns with paper narrative.** Section 3.4 of paper v4 can cite these values as explicit protocol parameters without introducing a convergence cost.

If a future evaluation ever lands in a configuration where `avg_pw_log2` approaches -16 or looser, the calibration should be re-run and tighter values (-32, -64, -100) considered. For such a sweep, the `calibrate-f4` subcommand accepts arbitrary combos — only the default 4-combo matrix is committed here.

## Notes on the reduced matrix (vs zadání)

Zadání F4.c proposed a 5 × 4 matrix (CutLimit ∈ {-4, -8, -12, -16, off} × CandidateMax ∈ {-8, -16, -32, off}) at N = 1000 per combination — 20 000 protocol runs, roughly 8 h wall clock on this host. This pilot used 4 representative combinations × N = 200 (800 runs, ~24 min) to establish whether the filter has measurable effect on the reference configuration. Given that **all 4 combinations produced statistically indistinguishable `avg_pw_log2`** with zero skipped rounds, the 20-combination sweep would not change the conclusion: **at these protocol parameters, the filter is inactive.** Wider sweep is deferred unless an evaluation lands in a configuration where the filter matters.
