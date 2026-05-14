"""F5.e aggregator — analyzes B.6 synthetic flood attacker output.
Loads the per-round CSV, computes rank-uniformity tests (KS), produces report
markdown summarizing whether the real seed is statistically distinguishable
from the K^M - 1 wrong-password decryptions across all 4 metrics.

Null hypothesis (flood defense validated):
- real_rank_{metric} is uniformly distributed over [1, K^M] across all
  (transcript, round) samples.
- Deviation from uniformity indicates a detectable metric and hence a
  distinguishability leak in the flood regime.
"""

import argparse
import csv
import math
import os
import statistics
from collections import defaultdict


def ks_uniform_test(ranks, n_max):
    """One-sample KS test of ranks against discrete uniform [1, n_max].
    Returns the KS D statistic and an approximate p-value (large-n approximation).
    """
    if not ranks:
        return (float("nan"), float("nan"))
    # Transform ranks to [0,1] approximate-continuous values: (r - 0.5) / n_max
    xs = sorted((r - 0.5) / n_max for r in ranks)
    n = len(xs)
    d_plus = max((i + 1) / n - x for i, x in enumerate(xs))
    d_minus = max(x - i / n for i, x in enumerate(xs))
    d = max(d_plus, d_minus)
    # Kolmogorov distribution p-value approximation
    sqrt_n = math.sqrt(n)
    # Kolmogorov's asymptotic: P(K > d) ≈ 2·sum((-1)^(k-1) · exp(-2·k²·n·d²))
    arg = (sqrt_n + 0.12 + 0.11 / sqrt_n) * d
    p = 0.0
    for k in range(1, 101):
        term = (-1) ** (k - 1) * math.exp(-2 * k * k * arg * arg)
        p += term
        if abs(term) < 1e-12:
            break
    p = max(0.0, min(1.0, 2 * p))
    return (d, p)


def summarize_metric(name, ranks, n_max):
    d, p = ks_uniform_test(ranks, n_max)
    if not ranks:
        return name, 0, float("nan"), float("nan"), d, p
    mean = statistics.mean(ranks)
    median = statistics.median(ranks)
    return name, len(ranks), mean, median, d, p


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input-csv", required=True)
    ap.add_argument("--output-md", required=True)
    args = ap.parse_args()

    metrics = ["entropy", "chi2", "compression", "deviation"]
    # per_n_max[combs][group][metric] = [ranks...]
    per_n_max = defaultdict(lambda: {"real": {m: [] for m in metrics}, "ctrl": {m: [] for m in metrics}})
    n_rounds_by_combs = defaultdict(int)
    has_ctrl = False

    with open(args.input_csv, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            try:
                combs = int(row["combinations"])
                n_rounds_by_combs[combs] += 1
                for m in metrics:
                    r = int(row[f"real_rank_{m}"])
                    per_n_max[combs]["real"][m].append(r)
                    if f"ctrl_rank_{m}" in row and row[f"ctrl_rank_{m}"]:
                        has_ctrl = True
                        cr = int(row[f"ctrl_rank_{m}"])
                        per_n_max[combs]["ctrl"][m].append(cr)
            except (ValueError, KeyError):
                continue

    # Write the report
    lines = [
        "# F5.e — B.6 Synthetic Flood Attacker Validation Report",
        "",
        f"**Source:** `{args.input_csv}`",
        f"**Rounds aggregated:** {sum(n_rounds_by_combs.values())}",
        "",
        "## Rounds per combination count (K^M)",
        "",
        "| K^M combinations | # rounds |",
        "|---:|---:|",
    ]
    for c in sorted(n_rounds_by_combs.keys()):
        lines.append(f"| {c} | {n_rounds_by_combs[c]} |")
    lines.append("")

    lines.append("## Rank-uniformity tests per metric (all rounds pooled)")
    lines.append("")
    lines.append("Under H₀ (real seed indistinguishable from wrong-password AES decryptions), `real_rank_{metric}` should be uniformly distributed over [1, K^M]. The KS statistic measures deviation from uniformity; p > 0.05 indicates the data is consistent with H₀.")
    lines.append("")
    lines.append("**Per-K^M breakdown (tests applied separately per combination count to avoid mixing scales):**")
    lines.append("")
    for combs in sorted(per_n_max.keys()):
        lines.append(f"### K^M = {combs}")
        lines.append("")
        lines.append("| Metric | Group | N samples | mean rank | median rank | uniform median | KS D | KS p | Verdict |")
        lines.append("|--------|-------|----------:|----------:|------------:|---------------:|-----:|-----:|---------|")
        uniform_median = (combs + 1) / 2
        for m in metrics:
            for group in (["real", "ctrl"] if has_ctrl else ["real"]):
                rs = per_n_max[combs][group][m]
                if not rs:
                    continue
                name, n, mean_r, median_r, d, p = summarize_metric(m, rs, combs)
                verdict = "consistent with H₀" if p > 0.05 else ("rejects H₀" if p < 0.01 else "marginal")
                lines.append(f"| {m} | **{group}** | {n} | {mean_r:.1f} | {median_r:.1f} | {uniform_median:.1f} | {d:.4f} | {p:.4f} | {verdict} |")
        lines.append("")

    if has_ctrl:
        lines.append("### Control-sample interpretation")
        lines.append("")
        lines.append("- **ctrl** = independently-drawn SecureRandom 32-byte sample per round (NOT the real seed).")
        lines.append("- Under H₀, ctrl and real come from the same distribution (both SecureRandom). If ctrl rank pattern matches real rank pattern, the signal is a **SecureRandom-vs-AES-Decrypt artifact** (boring: ambient difference between two pseudorandom sources), not a **real-seed-specific leak**.")
        lines.append("- If ctrl ranks are uniform (consistent with H₀) while real ranks deviate, the signal IS specific to the real seed's relationship with its ciphertext (concerning: real flood-defense leak).")
        lines.append("")

    lines.append("## Interpretation")
    lines.append("")
    lines.append("- **All 4 metrics consistent with H₀ (p > 0.05)** → empirical validation of F5 flood defense indistinguishability claim.")
    lines.append("- **Any metric rejects H₀ (p < 0.05)** → statistical signal by which an attacker could preferentially filter candidate seeds → weakens the flood defense argument; revisit or scope the claim.")
    lines.append("")
    lines.append("## Metrics reference")
    lines.append("")
    lines.append("- **entropy**: Shannon entropy of decrypted 32-byte output, byte-level. Random bytes ~4.94.")
    lines.append("- **chi2**: Chi-square vs uniform over 256 bins (expected per-bin count 32/256=0.125). Random: mean ~255.")
    lines.append("- **compression**: DEFLATE compressed size / 32. Random: ~1.1 (incompressible, slightly expanded by headers).")
    lines.append("- **deviation**: max |byte − 128| across the 32 bytes. Random: varies 100-127.")

    with open(args.output_md, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    print(f"Report written: {args.output_md}")
    # Echo summary (ASCII-safe for cp1250 Windows consoles)
    for combs in sorted(per_n_max.keys()):
        print(f"K^M={combs}: {n_rounds_by_combs[combs]} rounds")
        for m in metrics:
            for group in (["real", "ctrl"] if has_ctrl else ["real"]):
                rs = per_n_max[combs][group][m]
                if rs:
                    _, _, mean_r, median_r, d, p = summarize_metric(m, rs, combs)
                    verdict = "PASS" if p > 0.05 else ("REJECT" if p < 0.01 else "MARGINAL")
                    print(f"  {group}/{m}: mean_rank={mean_r:.0f} (uniform={combs/2:.0f}) KS_D={d:.4f} p={p:.4f} {verdict}")


if __name__ == "__main__":
    main()
