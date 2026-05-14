> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# Convergence pilot — post-F1 (crypto PRNG)

**Runs:** 1000
**Protocol parameters:** VL=32, VC=4096, H_AtoB=5, H_BtoA=2, H_PW=4, M=16
**RNG:** `System.Security.Cryptography.RandomNumberGenerator` (post-F1 migration)
**Generated UTC:** 2026-04-19T22:08:56.9279824Z

## Convergence

- Final-hit rate : 1000/1000 = **100,00%**
- Keys-match rate: 1000/1000 = **100,00%**

Both rates are expected to be 100% on the reference configuration; any miss is a regression.

## Iteration-count distribution (rounds-to-Final)

| Statistic | Value |
|-----------|------:|
| min       | 31 |
| p05       | 39 |
| median    | 48 |
| mean      | 48,92 |
| p95       | 60 |
| p99       | 68 |
| max       | 76 |

## Per-run elapsed

| Statistic | Value (ms) |
|-----------|-----------:|
| min       | 468,6 |
| median    | 1797,7 |
| mean      | 1793,9 |
| p95       | 2601,1 |
| max       | 4801,6 |

**Total wall-clock:** 00:29:53.8786688
