> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F4.c — probability thresholds calibration

**Runs per combination:** 200
**Protocol parameters:** VL=32, VC=4096, H_AtoB=5, H_BtoA=2, H_PW=4, M=16, NoUpdateLimit=0,25, TargetProbability=-512
**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)
**Generated UTC:** 2026-04-20T14:42:58.2578571Z
**Total wall-clock:** 00:19:43.4942595

Threshold encoding: `off` = `double.NegativeInfinity` (filter disabled); any other value is the log2-probability threshold. `CutLimit` filters pool vectors in `ClientA.FillVectors`/`ClientB.FillVectors` (reject vectors whose Stat-log2-prob is ≥ threshold, i.e. too typical). `CandidateMax` filters collision vectors in `ClientB.ProccessMessageFromA` (same semantic, applied after sorting). A round is skipped when the post-filter passed-vector count drops below M; that count is reported as `skipped_avg`.

## Results

| Combo | CutLimit | CandidateMax | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | ms/run |
|-------|:-------:|:------------:|:------------:|:-------------:|-----------:|----------:|----------:|------------:|------------:|-------:|
| cut=-8 cand=-85 | -8 | -85 | 200 / 200 | 200 / 200 | 61,05 | 60 | 81 | -163,05 | 9,67 | 960,5 |
| cut=-8 cand=-90 | -8 | -90 | 200 / 200 | 200 / 200 | 63,56 | 62 | 84 | -164,61 | 14,92 | 1045,6 |
| cut=-8 cand=-95 | -8 | -95 | 200 / 200 | 200 / 200 | 71,81 | 70 | 100 | -165,92 | 22,98 | 1341,0 |
| cut=-8 cand=-100 | -8 | -100 | 200 / 200 | 200 / 200 | 82,56 | 80 | 120 | -166,92 | 34,16 | 1562,6 |
| cut=-8 cand=-105 | -8 | -105 | 198 / 200 | 198 / 200 | 109,78 | 105 | 174 | -169,18 | 59,02 | 2141,8 |
| cut=-8 cand=-110 | -8 | -110 | 136 / 200 | 136 / 200 | 162,14 | 165 | 201 | -173,99 | 104,37 | 3262,4 |
| cut=-8 cand=-115 | -8 | -115 | 2 / 20 | 2 / 20 | 195,20 | 201 | 201 | -180,40 | 142,60 | 4347,9 |

## Legend and interpretation

- **FinalHit / KeysMatch** — primary correctness check. 100% is required for any combination recommended as a protocol default.
- **iter(mean)** — convergence cost. Stricter filters raise this because skipped rounds push the prop < TargetProbability termination later.
- **avg_pw_log2** — security indicator: mean Stat-log2-probability of the password vectors that end up in the final round. More negative = rarer region of the distribution = stronger structural defence.
- **skipped_avg** — operational signal that the filter is actually kicking: counts rounds where F4 reduced the post-filter pool below M and the round was dropped.

## Findings (transition-zone resolution)

The wide sweep (`reports/probability_thresholds_calibration_wide.md`) showed a discontinuous jump from `CandidateMax = -80` (100% convergence, 5.5 skipped/run) to `-120` (broken). This narrow sweep resolves the transition with 5-bit resolution and reveals a **smooth monotonic degradation**, not a cliff:

| CandidateMax | iter(mean) | skipped_avg | **skipped / iter** | avg_pw_log2 | FinalHit / N |
|---:|---:|---:|---:|---:|---:|
| -85 | 61.05 | 9.67 | **15.8 %** | -163.05 | 200 / 200 |
| -90 | 63.56 | 14.92 | **23.5 %** | -164.61 | 200 / 200 |
| **-95** | **71.81** | **22.98** | **32.0 %** | **-165.92** | **200 / 200** |
| -100 | 82.56 | 34.16 | 41.4 % | -166.92 | 200 / 200 |
| -105 | 109.78 | 59.02 | 53.8 % | -169.18 | 198 / 200 |
| -110 | 162.14 | 104.37 | 64.4 % | -173.99 | 136 / 200 (68 %) |
| -115 | 195.20 | 142.60 | 73.1 % | -180.40 | 2 / 20 (early abort) |

Three regimes are visible:

1. **Active safe zone (-85 … -100).** The filter reliably kicks (≥16% of rounds skipped, up to 41%), convergence stays at 100 %, and `avg_pw_log2` moves from -163 to -167 (≈4 log-bits stricter than at -85, ~7 log-bits stricter than the disabled baseline of -160). Cost in iterations is a linear ramp (61 → 83 mean), fully absorbed under the `--max-iter 200` cap (p95 iter stays ≤ 120).
2. **Edge-of-viability zone (-105 … -110).** Convergence starts slipping: -105 drops 2/200 runs, and -110 fails 64/200 runs. `iter(p95)` hits or exceeds 174 — some runs narrowly miss the cap. The skipped/iter ratio crosses 50%.
3. **Broken zone (-115 and stricter).** Early abort triggers (2/20 convergence). Most rounds skipped (>70% of iterations), protocol cannot assemble enough password candidates to reach TargetProbability.

## Recommended default (final)

**`CandidateMaxProbabilityLog2 = -95`.** The strictest value in the active safe zone whose `skipped / iter` ratio stays under the agreed 40 % criterion (32.0 %).

- Convergence: 100 % (200 / 200), `iter(p50) = 70`, `iter(p95) = 100`, well under the operational `--max-iter 200` guard.
- Security indicator: `avg_pw_log2 = -165.92`, i.e. password vectors live ~8 log-bits deeper in the tail distribution than at the pre-F4 baseline (where `avg_pw_log2 ≈ -158`).
- Filter activity: ~23 rounds skipped per run (~32 % of iterations). Demonstrably kicking, not a symbolic default.
- Convergence budget: median iteration count grows from ~49 (disabled) to 70 (+43 %). Protocol runs 30-50 % slower on average.

At `-100` the ratio is 41.4 %, just over the 40 % criterion; taking `-95` gives a small safety margin. `-100` remains a reasonable "stretch" value if even stronger filter action is desired and the 40 % boundary is treated as a soft target.

This default has been applied to `Configuration.CandidateMaxProbabilityLog2` in `TreeParityMachine_HASH_V2` (previously `-16`). Any future evaluation that wants to compare against the pre-F4 baseline (filter disabled) should set `CandidateMaxProbabilityLog2 = double.NegativeInfinity` explicitly.

`CutLimitProbabilityLog2` stays at `-8` (unchanged) — the wide sweep confirmed that threshold is inactive across the tested range on the reference configuration, and no narrow sweep is motivated for it until a smaller-dimension profile (IoT / Mobile) is evaluated.
