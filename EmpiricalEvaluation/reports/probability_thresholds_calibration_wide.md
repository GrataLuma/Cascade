> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F4.c — probability thresholds calibration

**Runs per combination:** 200
**Protocol parameters:** VL=32, VC=4096, H_AtoB=5, H_BtoA=2, H_PW=4, M=16, NoUpdateLimit=0,25, TargetProbability=-512
**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1)
**Generated UTC:** 2026-04-20T14:16:12.5027733Z
**Total wall-clock:** 00:31:20.9045292

Threshold encoding: `off` = `double.NegativeInfinity` (filter disabled); any other value is the log2-probability threshold. `CutLimit` filters pool vectors in `ClientA.FillVectors`/`ClientB.FillVectors` (reject vectors whose Stat-log2-prob is ≥ threshold, i.e. too typical). `CandidateMax` filters collision vectors in `ClientB.ProccessMessageFromA` (same semantic, applied after sorting). A round is skipped when the post-filter passed-vector count drops below M; that count is reported as `skipped_avg`.

## Results

| Combo | CutLimit | CandidateMax | FinalHit / N | KeysMatch / N | iter(mean) | iter(p50) | iter(p95) | avg_pw_log2 | skipped_avg | ms/run |
|-------|:-------:|:------------:|:------------:|:-------------:|-----------:|----------:|----------:|------------:|------------:|-------:|
| cut=-25 cand=-50 | -25 | -50 | 200 / 200 | 200 / 200 | 51,75 | 51 | 64 | -160,64 | 0,00 | 870,4 |
| cut=-25 cand=-80 | -25 | -80 | 200 / 200 | 200 / 200 | 55,38 | 55 | 71 | -162,06 | 5,65 | 945,8 |
| cut=-25 cand=-120 | -25 | -120 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 153,15 | 4449,3 |
| cut=-25 cand=-160 | -25 | -160 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 159,15 | 4477,3 |
| cut=-25 cand=-200 | -25 | -200 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 159,85 | 4390,8 |
| cut=-40 cand=-50 | -40 | -50 | 200 / 200 | 200 / 200 | 51,22 | 50 | 65 | -160,09 | 0,01 | 873,6 |
| cut=-40 cand=-80 | -40 | -80 | 200 / 200 | 200 / 200 | 55,31 | 54 | 71 | -160,91 | 5,30 | 949,9 |
| cut=-40 cand=-120 | -40 | -120 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 156,00 | 4419,9 |
| cut=-40 cand=-160 | -40 | -160 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 163,15 | 4393,4 |
| cut=-40 cand=-200 | -40 | -200 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 160,40 | 4436,3 |
| cut=-60 cand=-50 | -60 | -50 | 200 / 200 | 200 / 200 | 52,53 | 52 | 68 | -161,65 | 0,00 | 901,8 |
| cut=-60 cand=-80 | -60 | -80 | 200 / 200 | 200 / 200 | 56,22 | 55 | 73 | -161,83 | 5,70 | 989,2 |
| cut=-60 cand=-120 | -60 | -120 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 154,40 | 4420,9 |
| cut=-60 cand=-160 | -60 | -160 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 159,75 | 4396,1 |
| cut=-60 cand=-200 | -60 | -200 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 163,40 | 4410,6 |
| cut=-80 cand=-50 | -80 | -50 | 200 / 200 | 200 / 200 | 52,62 | 51 | 68 | -157,80 | 0,01 | 1323,8 |
| cut=-80 cand=-80 | -80 | -80 | 200 / 200 | 200 / 200 | 55,91 | 55 | 73 | -161,95 | 5,54 | 1909,1 |
| cut=-80 cand=-120 | -80 | -120 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 158,55 | 10281,2 |
| cut=-80 cand=-160 | -80 | -160 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 162,70 | 9321,5 |
| cut=-80 cand=-200 | -80 | -200 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 162,75 | 8749,1 |
| cut=-100 cand=-50 | -100 | -50 | 200 / 200 | 200 / 200 | 51,97 | 51 | 64 | -158,77 | 0,00 | 772,5 |
| cut=-100 cand=-80 | -100 | -80 | 200 / 200 | 200 / 200 | 55,74 | 55 | 72 | -162,04 | 5,49 | 860,0 |
| cut=-100 cand=-120 | -100 | -120 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 153,70 | 4016,4 |
| cut=-100 cand=-160 | -100 | -160 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 158,50 | 4131,4 |
| cut=-100 cand=-200 | -100 | -200 | 0 / 20 | 0 / 20 | 201,00 | 201 | 201 | NaN | 159,15 | 3810,2 |

## Legend and interpretation

- **FinalHit / KeysMatch** — primary correctness check. 100% is required for any combination recommended as a protocol default.
- **iter(mean)** — convergence cost. Stricter filters raise this because skipped rounds push the prop < TargetProbability termination later.
- **avg_pw_log2** — security indicator: mean Stat-log2-probability of the password vectors that end up in the final round. More negative = rarer region of the distribution = stronger structural defence.
- **skipped_avg** — operational signal that the filter is actually kicking: counts rounds where F4 reduced the post-filter pool below M and the round was dropped.

## Findings on the wide matrix

This sweep covered the **operating range of password-candidate probabilities** identified in the earlier narrow calibration (avg_pw_log2 ≈ -160 in the reference configuration). The two threshold dimensions behave very differently:

### CandidateMaxProbabilityLog2 — sharp bimodal transition between -80 and -120

| CandidateMax | 5 rows (one per CutLimit) |
|---|---|
| **-50** | 100 % convergence, `skipped_avg ≈ 0`, `avg_pw_log2 ≈ -160` → filter *inactive* |
| **-80** | 100 % convergence, `skipped_avg ≈ 5.3 – 5.7`, `avg_pw_log2 ≈ -161` → filter *kicks*, ~10 % of rounds get skipped, convergence preserved |
| **-120** | 0 / 20 convergence (early abort), `iter(p50) = 201` (hits MaxIter cap), `skipped_avg ≈ 153-158` → filter *breaks* protocol: ~75 % of rounds skipped |
| **-160** | 0 / 20 (same story) |
| **-200** | 0 / 20 (same story) |

The transition is **discontinuous within the sweep grid**: the protocol is healthy at CandidateMax = -80 and completely broken at -120. The sweet spot for "filter actively rejects over-typical candidates while preserving convergence" sits in the narrow band **(-80, -120)**, which the grid does not resolve.

At CandidateMax = -80, the filter drops roughly 5-6 password-candidate rounds per protocol run (out of ~50 total rounds). The effect on `avg_pw_log2` is marginal (~-161 vs. -160 without filter) because the dropped candidates were themselves near the threshold — removing them barely shifts the average.

### CutLimitProbabilityLog2 — inactive in every tested row

Across all five CutLimit values (-25, -40, -60, -80, -100), convergence and security metrics are statistically indistinguishable at fixed CandidateMax. The CutLimit filter in `FillVectors` never rejects any pool vector in the tested range: pool vectors at this protocol configuration (L=32, N=4096, VC=4096) have Stat-log2-prob structurally well below -100 even after repeated Stat updates, so no CutLimit in [-25, -100] excludes anything.

To see CutLimit activity on the reference configuration, values around -150 or lower would likely be required — but at that point the filter starts starving the pool and is unlikely to preserve convergence. CutLimit is essentially a **lower-dimensional-parameter safeguard**: on smaller L / smaller N configurations (IoT profile, Mobile profile) pool vectors live higher in log-prob, and CutLimit will kick there.

## Recommended defaults (revised)

- **`CandidateMaxProbabilityLog2 = -80`** — on the reference configuration, this is the strictest value that preserves 100 % convergence while producing a non-zero `skipped_avg` (~5.5). The filter is measurably active, not just a safety label. Tightening beyond -80 toward -120 would be a narrow-band sweep to see whether there is a usable setting between the two grid points; defaulting to -80 avoids that risk.
- **`CutLimitProbabilityLog2 = -8`** (unchanged from zadání). On the reference configuration the filter is inactive regardless of threshold in the tested range. Keeping the zadání default `-8` costs nothing and preserves the parameter as a published safety net for smaller-dimension profiles where pool vectors can legitimately reach higher log-prob.

A tighter narrow-band sweep of CandidateMax ∈ {-80, -90, -100, -110, -120} is the natural follow-up if a configuration with `skipped_avg` in the 20-40 range (meaningful filter activity, still converging) is desired. Until that sweep runs, `-80` is the defensible upper bound for active filter behavior on the reference.

## Wall-clock and RAM guards

This sweep ran 25 combos × N=200 via a 2-worker parallel orchestrator (`--workers 2`), each worker process handling a 12-13 combo batch sequentially with `--max-iter 200` and early abort after 20 runs at < 25 % convergence. Non-converging combos (15 / 25) finished their batch in ~70 s thanks to early abort; converging combos took ~3-4 min each. Total wall-clock **31 min** (vs. the unbounded first run that hit ~6 GB per worker and crashed on JSON serialization after 45 min).

Key guards that made this possible:

- `ProtocolRunner.MaxIterations = 200` caps non-converging runs: each run's `ClientA.allVectors` dictionary grows at most 200 × 4096 = 820 k entries ≈ 80 MB.
- `GC.Collect` every 5 runs inside a combo and between combos, preventing accumulation across the sequential loop.
- Early abort after 20 runs at < 25 % convergence declares a combo non-viable and skips the remaining 180 runs — the signal is already unambiguous.
- Per-combo try/catch + incremental JSON flush: partial results survive even if a later combo throws.
- `JsonNumberHandling.AllowNamedFloatingPointLiterals` in the JSON serializer — non-converging combos report `NaN` / `-Infinity` which strict JSON forbids; named literals serialize them safely.

Peak worker memory in this sweep was 56 MB, well within the threshold — two orders of magnitude below the pre-guard behavior.
