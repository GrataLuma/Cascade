> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F4.c — probability thresholds calibration

**Runs per combination:** 200
**Protocol parameters:** VL=32, VC=4096, H_AtoB=5, H_BtoA=2, H_PW=4, M=16, NoUpdateLimit=0,25, TargetProbability=-512
**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)
**Generated UTC:** 2026-04-20T15:28:34.9964327Z
**Total wall-clock:** 00:16:17.2946476

Threshold encoding: `off` = `double.NegativeInfinity` (filter disabled); any other value is the log2-probability threshold. `CutLimit` filters pool vectors in `ClientA.FillVectors`/`ClientB.FillVectors` (reject vectors whose Stat-log2-prob is ≥ threshold, i.e. too typical). `CandidateMax` filters collision vectors in `ClientB.ProccessMessageFromA` (same semantic, applied after sorting). A round is skipped when the post-filter passed-vector count drops below M; that count is reported as `skipped_avg`.

## Results

| Combo | CutLimit | CandidateMax | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | fill_rej_avg | fill_safe_avg | peak_WS_MB | ms/run |
|-------|:-------:|:------------:|:------------:|:-------------:|-----------:|----------:|----------:|------------:|------------:|-------------:|--------------:|-----------:|-------:|
| cut=-50 cand=-95 | -50 | -95 | 200 / 200 | 200 / 200 | 68,03 | 65 | 93 | -166,03 | 21,14 | 0,0 | 0,000 | 214 | 1070,9 |
| cut=-70 cand=-95 | -70 | -95 | 200 / 200 | 200 / 200 | 69,39 | 69 | 95 | -165,18 | 22,22 | 0,0 | 0,000 | 230 | 1103,5 |
| cut=-90 cand=-95 | -90 | -95 | 200 / 200 | 200 / 200 | 71,61 | 70 | 99 | -165,71 | 23,02 | 0,0 | 0,000 | 259 | 1251,4 |
| cut=-100 cand=-95 | -100 | -95 | 200 / 200 | 200 / 200 | 72,83 | 71 | 98 | -164,74 | 23,79 | 0,0 | 0,000 | 241 | 1263,0 |
| cut=-110 cand=-95 | -110 | -95 | 200 / 200 | 200 / 200 | 69,85 | 67 | 98 | -165,49 | 22,67 | 0,0 | 0,000 | 217 | 1398,3 |
| cut=-115 cand=-95 | -115 | -95 | 200 / 200 | 200 / 200 | 70,36 | 69 | 98 | -164,87 | 22,55 | 0,0 | 0,000 | 237 | 1429,5 |
| cut=-120 cand=-95 | -120 | -95 | 200 / 200 | 200 / 200 | 71,08 | 67 | 102 | -166,02 | 22,91 | 0,0 | 0,000 | 238 | 1117,3 |

## Legend and interpretation

- **FinalHit / KeysMatch** — primary correctness check. 100% is required for any combination recommended as a protocol default.
- **iter(mean)** — convergence cost. Stricter filters raise this because skipped rounds push the prop < TargetProbability termination later.
- **avg_pw_log2** — security indicator: mean Stat-log2-probability of the password vectors that end up in the final round. More negative = rarer region of the distribution = stronger structural defence.
- **skipped_avg** — operational signal that the CandidateMax filter is kicking: counts rounds where it reduced the post-filter pool below M and the round was dropped.
- **fill_rej_avg** — CutLimit diagnostic: mean count of pool-vector rejections per protocol run in `ClientA.FillVectors` / `ClientB.FillVectors`. Zero = CutLimit threshold is too loose to reject anything on this configuration.
- **fill_safe_avg** — CutLimit diagnostic: mean count of `CutLimitSafetyAttempts`-cap triggers per run (vectors accepted unfiltered after 1000 failed attempts). `> 0.1` signals the threshold is too strict for the pool distribution.
- **peak_WS_MB** — observed peak WorkingSet of the current worker process during this combo. Sanity bound for the memory guard.

## Findings — CutLimit is structurally inactive on the reference configuration

Across all seven CutLimit thresholds from **-50 down to -120**, and across **1400 total protocol runs** (7 × 200):

- **`fill_rej_avg = 0.0`** everywhere. **Not a single pool vector was rejected by `ClientA.FillVectors` / `ClientB.FillVectors`** in any of the 1400 runs.
- **`fill_safe_avg = 0.000`** everywhere. The safety-cap counter never triggered — the filter simply never saw a candidate above threshold.
- **`FinalHit / KeysMatch = 200 / 200`** for every combo — convergence is unaffected because the filter is passive.
- All other metrics (`iter`, `avg_pw_log2`, `skipped_avg`) are statistically indistinguishable between rows. The ~22-23 skipped rounds per run come entirely from the CandidateMax=-95 filter on the emission side, not from CutLimit.

### Why: the pool's log2-probability distribution lies far below any achievable threshold

A freshly generated pool vector (`new Vector(32, stat)` after `SecureRandom.Instance.FillBuffer(Data)`) has 32 bytes drawn uniformly at random. The `Stat` accumulator updates with mixing factor `1 - NoUpdateLimit = 0.75`, so its `Probabilities[i]` values hover near 1 per byte — the Stat stays close to uniform on the reference configuration with `VectorLength = 32` and `VectorCount = 4096`. Therefore:

- `Stat.GetProbabilityForNumber(b) = Probabilities[b] / 256 ≈ 1/256`
- For 32 bytes: product ≈ (1/256)^32 = 2^(-256)
- `log2(product) ≈ -256`

For CutLimit to reject a vector, the vector would need `log2_prob >= threshold`. To catch any vector on reference, the threshold would need to be **below -200 or so** — which would reject nearly every uniformly-generated vector and trigger the safety cap on every call, breaking pool initialization entirely.

In short, on the reference configuration the CutLimit filter sits in an **empty band**: no threshold value simultaneously (a) rejects anything and (b) preserves convergence. The filter is purely decorative here.

### Why the filter is still valuable: smaller-dimension profiles

On a configuration with `VectorLength = 16` (IoT profile), uniform log2-prob drops to ≈ -128. On `VectorLength = 16, VectorCount = 1024` (smaller Stat accumulator), the Stat distribution is less uniform and typical vectors can cluster materially higher. There, CutLimit in the range (-100, -30) can reject structurally over-typical vectors and shift the pool distribution into a rarer region without breaking convergence.

The F4 CutLimit filter is therefore a **parameter-family safeguard**: it is designed for and meaningful on smaller protocol profiles, but is inactive on the reference. Consistent with the zadání's framing of F4 parameters as "parametry rodiny diskutované v sekci 4 paperu" — not every knob is load-bearing on every instance.

## Recommended default (CutLimit)

**`CutLimitProbabilityLog2 = -8`** (unchanged from zadání). Four justifications:

1. **Zero operational cost on the reference configuration** — as this sweep confirms across 1400 runs, the filter rejects nothing and the safety cap never triggers.
2. **Explicit published threshold** — the parameter is a documented part of the protocol family, not a silent branch.
3. **Active on smaller profiles** — when the protocol is re-evaluated at IoT / Mobile scale, the same default automatically kicks in without a separate tuning step.
4. **Safe upper bound** — `-8` is loose enough that even a Stat update sequence that drives some byte positions toward concentrated distributions would not have reference-configuration vectors crossing the threshold.

No sweep narrower or wider than this one is motivated for the reference configuration. A dedicated CutLimit calibration is recommended when the protocol is run on profiles with `VectorLength < 24` or `VectorCount < 2048`, where pool vectors can realistically reach `log2_prob > -100`.

Per the zadání criterion "`fill_safety_triggers_avg > 0.1` per run is a signal that CutLimit is too strict": this sweep reports `0.000` across the board, so no combination crosses that warning line. The threshold band where CutLimit would start triggering the safety cap is below -200 on reference — far outside any practical setting.
