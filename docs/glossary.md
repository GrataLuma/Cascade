# Glossary — Grata Cascade CKA Protocol

Reference for symbols, constants, and naming conventions used across the codebase,
specifications (`Zadani.md`, `Zadani-followup.md`), reports under `reports/`, and
the paper drafts under `paper/`. Maintained as a single source of truth.

**Status:** living document. Update when introducing new parameters or renaming
existing ones.

---

## 1. Parties and Entities

| Symbol | Meaning |
|--------|---------|
| **A** | ClientA — initiator. Holds private vector set 𝒱_A; sends `H_AB` hash lists per round. |
| **B** | ClientB — responder. Holds private vector set 𝒱_B; replies with `H_BA` hashes, perturbation R, and AES-encrypted seed message. |
| **E** | Eavesdropper / attacker. Observes only the public transcript. |
| **𝒱_A, 𝒱_B** | Private vector sets, each of size N. |
| **U** | Universe of vectors. \|U\| = 256^L = 2^(8L). |
| **K\*** | Shared key derived by both parties (32 bytes, AES-256 key material). |
| **K_A, K_B** | Per-party derived keys. May differ when a hash collision causes A and B to agree on different password vectors (KM=false). |

---

## 2. Structural Parameters (Configuration.cs)

Hardcoded defaults in `GrataCascade.Core/Configuration.cs`; overridable via JSON
configs loaded by `ConfigLoader.Load(--config <path>)`. JSON schema documented in
`docs/config_format.md`.

| Symbol | JSON field | C# field | Reference default | Meaning | Unit |
|--------|------------|----------|---:|---------|------|
| **L** | `vector_length` | `VectorLength` | 32 | Length of one private vector | bytes |
| **N** | `vector_count` | `VectorCount` | 4096 | Pool size (vectors per party) | vectors |
| **h_AB** | `hash_length_ab` | `HASHLength_AtoB` | 5 | Length of A→B published hash (prefix of SHA-256) | bytes |
| **h_BA** | `hash_length_ba` | `HASHLength_BtoA` | 2 | Length of B→A reply hash | bytes |
| **h_P** | `hash_length_password` | `HASHLength_Password` | **8** | Length of Final-slot (password) hash | bytes |
| **M** | `aes_passed_vectors_count` | `AESPassedVectorsCount` | **8** | Password vector count = number of Final slots | vectors |
| **s** | `seed_min_max` | `SeedMinMax` | **4** | Per-byte perturbation range when generating pool from a seed (vector_byte = seed_byte ± s mod 256) | bytes |
| **MaxIter** | `max_iterations` | `MaxIterations` | **500** | Hard cap on iter rounds before a run is declared FH=false (structural break) | iterations |

---

## 3. Probability Parameters

| Symbol | JSON field | Reference default | Meaning | Unit |
|--------|------------|---:|---------|------|
| **p_upd** | `update_probability` | 0.75 | Probability that the per-byte Stat distribution updates in a given round | [0, 1] |
| **NoUpdateLimit** | (derived) | 0.25 | = 1 − p_upd; legacy bridge field used inside the protocol switch | [0, 1] |
| **CutLimit_log2** | `cut_limit_probability_log2` | -8 | Threshold for rejecting too-typical vectors during pool fill (log₂ probability) | bits |
| **CandMax_log2** | `candidate_max_probability_log2` | **-8** (effectively off) | Maximum log₂ probability for a vector to qualify as a password candidate. Reference v2 disables the filter (= same as cut limit); see paper §4 for the configurable Tier-3 defense at e.g. -95 (the F4 v1 calibrated optimum). | bits |
| **TargetProb_log2** | `termination_threshold_log2` | -512 | Stat-concentration termination threshold | bits |
| **CutLimitSafetyAttempts** | `cut_limit_safety_attempts` | 1000 | Iter cap before CutLimit safety bypass kicks in | iterations |

---

## 4. Hashes and Security Bounds

### 4.1 Hash Functions

All three hash families are prefixes of `SHA-256(v)`:

| Symbol | Definition | Reference value | Bits |
|--------|------------|---:|---:|
| **H_AB(v)** | First `h_AB` bytes of SHA-256(v) | 5 bytes | 40 |
| **H_BA(v)** | Bytes [h_AB, h_AB + h_BA) of SHA-256(v) | 2 bytes | 16 |
| **H_P(v)** | Bytes [h_AB + h_BA, h_AB + h_BA + h_P) of SHA-256(v) | **8 bytes** | **64** |

### 4.2 Security Bounds (derived)

| Symbol | Formula | Reference value | Meaning |
|--------|---------|---:|---------|
| **λ** (lambda) | 8·(h_AB + h_BA + h_P) | **120 bits** | Cumulative preimage barrier — the bits an attacker must "guess right" across all three hashes. |
| **N²/\|U\|** | N² / 2^(8L) | 2⁻²³² | Foundational asymmetry bound — probability that a random attacker vector lands in 𝒱_A ∩ 𝒱_B. |
| **ρ_pool** | N / (2s + 1)^L | **≈ 2⁻⁸⁹** | Pool density inside the seed perturbation subspace. Larger s → sparser pool → harder for attacker to reconstruct. |

---

## 5. Per-Run Outcome Metrics

Captured per protocol run in `Outcome` (`ProtocolRunner.cs`) and persisted as
columns in F2's per-run CSV (`reports/f2/convergence_*.csv`).

| Symbol | Meaning |
|--------|---------|
| **FH** (Final Hit) | Boolean. Protocol reached a Final round (B sent message tagged `"Final"`) before hitting MaxIterations. FH=false ⇒ structural break: A and B failed to converge on a shared candidate set. |
| **KM** (Keys Match) | Boolean. K_A equals K_B byte-by-byte. Defined only when FH=true. KM=false despite FH=true ⇒ A and B both terminated successfully but derived different keys due to a hash collision that gave each party a different "consistent" set of password vectors. |
| **iter** | Number of iter rounds executed before Final (or MaxIterations cap if FH=false). Reference median ≈ 70. |
| **MaxIter** | Hard cap on iter rounds. Default 5000; overridable per command via `--max-iter`. |
| **success** | Convenience column = FH ∧ KM. |

### 5.1 Reference baseline

**Reference v1** (pre 2026-05-02 pivot, F2 N=50 000 runs):

| Outcome | Count | Rate |
|---------|-----:|-----:|
| FH=true ∧ KM=true (success) | 49 991 | 99.9820% |
| FH=true ∧ KM=false (divergence) | 9 | 0.0180% |
| FH=false (structural break) | 0 | 0.0000% |

Clopper-Pearson upper 95% CI on combined failure rate: 3.417×10⁻⁴.
Iter median 70, p95 99, max 181. Elapsed median 2.6 sec/run.

**Reference v2** (post 2026-05-02 pivot, F2-v2 N=50 000 runs):

| Outcome | Count | Rate |
|---------|-----:|-----:|
| FH=true ∧ KM=true (success) | 49 996 | 99.9920% |
| FH=true ∧ KM=false (divergence) | 4 | 0.0080% |
| FH=false (structural break) | 0 | 0.0000% |

Clopper-Pearson upper 95% CI on combined failure rate: **2.048×10⁻⁴**
(40% tighter than v1's 3.417×10⁻⁴).
Iter median 49, p95 62, max 90. Elapsed median 1.7 sec/run.

Per-shard distribution of KM divergences: {1, 0, 2, 0, 1} — Poisson-consistent
with rate ~8×10⁻⁵.

**Empirical takeaway**: h_P 4→8 cut KM divergences ~2.25× as predicted by the
Final-slot collision model, but did not eliminate them. The residual ~8×10⁻⁵
rate is consistent with H_AB collision (40-bit, unchanged) becoming the
dominant divergence source once Final-slot collisions were suppressed.

### 5.2 Attacker baseline (B.5-extended-v2, N=300 transcripts)

All four passive attackers fail to recover K\* across 300 independent transcripts:

| Attacker | Successes | CP upper 95% CI | Avg time/run | Avg samples/run |
|----------|----------:|----------------:|-------------:|----------------:|
| B.1 Baseline Random | 0 / 300 | 1.222% | 600 s (budget cap) | 2.47×10⁸ |
| B.2 Hash-First Filtered | 0 / 300 | 1.222% | 56.7 s | 10⁷ |
| B.3 Structured (Tail) | 0 / 300 | 1.222% | 130.5 s | 10⁷ |
| B.4 AES-Oracle | 0 / 300 | 1.222% | 335.1 s | 6.84×10⁷ |

Total compute: ~10¹¹ SHA-256 evaluations across all attackers. Wall-clock 6h33m
on 22 workers (HT-aware on an 11-core CPU).

B.4 oracle discriminator strength: mean TP=0.003411, mean FP=0.002923,
**Cohen's d = 0.009** (no detectable signal; consistent with SHA-256 random-oracle
baseline). 10 262 round pairs measured, 2.05×10¹⁰ discriminator queries total.

Shannon entropy of B.1 sampler (cross-check): 7.999996 bits/byte ≈ uniform.

B.1 budget reduced from spec's 1800s to 600s (no impact on bound — coupon-collector
ETA at v2 parameters places full Final-slot recovery far above any realistic budget).

---

## 6. Per-Round Protocol Artifacts

Recorded in `Transcript.IterateRound` and `Transcript.FinalRound` for attacker
analysis.

| Symbol | Meaning |
|--------|---------|
| **R** | B→A perturbation vector. Uniform random over {0..255}^L. Drives Stat distribution evolution (see `paper/notes/r_distribution.md`). |
| **AES message** | `AES_256_CBC_NoPadding(seed, password = SHA256(passedVectors))` — encrypted seed B uses to generate the next pool. |
| **Stat** | Per-byte probability distribution. 16 or 32 bytes × 256 buckets of BigInteger probability mass. Used by both parties to filter candidates and to recognize convergence. |

---

## 7. Attacker Catalogue

All attackers live under `EmpiricalEvaluation/HashFirstAttacker/Attackers/` and
are dispatched via `HashFirstAttacker.exe <code>` subcommands (see
`docs/USAGE.md`).

| Code | Class | Primary CLI flags | What it does |
|------|-------|-------------------|--------------|
| **B.1** | `BaselineRandomAttacker` | `--budget-seconds N` | Random sampling from the universe; brute-force per Final slot until time budget expires. |
| **B.2** | `HashFirstFilteredAttacker` | `--pool P --runs N` | Generate pool of P random vectors. Filter through iter rounds against published H_AB hashes. Final filter against Final-slot H_P hashes. Try to recover K_A from M lex-sorted survivors. |
| **B.3** | `StructuredSamplingAttacker` | `--runs N` | Stat-driven sampling; reconstructs the per-byte Stat distribution from the transcript and biases candidate generation toward it. |
| **B.4** | `AESOracleAttacker` | `--runs N` | AES-decrypt discriminator. Reports TP/FP rates and Cohen's d per round. Does not attempt full K\* recovery. |
| **B.5** | (orchestrator) | `--runs N` | Big-eval pipeline: B.2 against N independent transcripts, aggregated with Clopper-Pearson CIs. |
| **B.6** | `SyntheticFloodAttacker` | `--k K --runs N` | F5 byte-level synthetic flood. Generates K candidates per slot via byte-level synthesis; measures filter pass rate and post-filter combinatorial cost. |

### 7.1 Attacker Pool Size

| Symbol | Meaning | Default |
|--------|---------|--------:|
| **P** | Attacker pool size (B.2, B.3, B.6). The attacker's "set size" — number of random candidate vectors generated. | 10⁷ |

### 7.2 Theoretical B.2 Success Bound

Expected number of full-recovery candidates after iter and Final filters:

```
E[recovery candidates] = P · (N / 2^(8·h_AB))^m · (M / 2^(8·h_P))^M
```

where m is the iter count of the protocol run. **Crack point** is the parameter
region where E ≥ 1 at realistic P.

Reference (L=32, N=4096, h_AB=5, h_P=4, m=70, M=16, P=10⁷):
E ≈ 10⁷ · 2⁻¹⁹⁶⁰ · 2⁻⁴⁴⁸ ≈ 0 — astronomically safe.

---

## 8. F-Tasks Index

Full specs in `Zadani.md` (original) and `Zadani-followup.md` (post-review
follow-ups).

| Code | Title | Status | Artifacts |
|------|-------|--------|-----------|
| **F1** | RNG migration to crypto PRNG | done | F1 commits; built-in via `SecureRandom.Instance` |
| **F2** | 50 000-run convergence empirical artifact | done | `reports/f2/convergence_50k_summary.md` |
| **F3** | SHA-NI optimization (hardware-accelerated SHA-256) | done | `reports/sha256_*.md` |
| **F4** | Probability thresholds (CutLimit, CandMax) | done | `reports/probability_thresholds_calibration_*.md` |
| **F5** | Flooding defense + B.6 synthetic flood | done | `reports/f5/`, `reports/f5e/`, `reports/flooding_defense_*.md` |
| **F6** | Active attacker — paper revision (scope limitation) | pending | (paper-only task) |
| **F7** | Standalone Windows .exe distribution | done | `release/EmpiricalEvaluation-vX.zip`, `docs/README.md`, `docs/USAGE.md`, `docs/REPRODUCE_PAPER.md` |
| **F8** | Crack-point empirical analysis | done v1 (4/16 grid cells precheck-ed pre-pivot, wiped); **F8-v2 done 2026-05-04** — scope redirected from grid sweep to single `break_demo` config validating paper Open Problem `prob:breakdemo`. Config: L=8, N=256, h_AB=3, h_BA=1, h_P=3, M=4, p_upd=1.0, λ=56 bits. Convergence: 200/200 FH, 191/200 KM (4.5% divergence — measurably weakened legit side). B.5-extended battery (B.1+B.2+B.3+B.4+B.7) on n=300: **0/300 across all 5 attackers**. New B.7 Stat-Sorted Enum attacker. **B.7-Tail follow-up 2026-05-05:** dual-mode investigation (Typical + Tail) — both directions fail at different cascade stages (Typical at stage-c, Tail at stage-a). Protocol's cascaded h_AB+h_BA+h_P filter robust to Stat-prior attackers in either direction at 1M-sample budget. v1 grid + crack_*bit configs deleted as part of redirect. | `configs/break_demo.json`, `reports/refv2/break_demo/` |
| **F9** | JSON config-driven parametrization | done | `configs/`, `GrataCascade.Core/{ConfigLoader,ProtocolConfiguration}.cs` |
| **F10** | p_upd calibration (sweet spot 0.75) | done | `reports/p_upd_*.md` (in `EmpiricalEvaluation/reports/`) |
| **F11** | Parametric profiles | done v1 (iot, mobile, classical, pq128); **F11-v2 done 2026-05-04** — scope reduced to `pq128` only (mobile + classical removed from active set; iot handled separately as F13-v2). pq128 v2 redesign: λ 256→144 bits, M 32→16, h_AB 16→6, h_BA 8→3, h_P 8→9. 200/200 FH+KM, NIST 11/12 PASS + BMR N/A. | `reports/f11/` (v1 historical), `reports/refv2/f11/pq128/` (v2) |
| **F12** | SeedMinMax sweep | done | `reports/f12/`, `reports/f12_*.md` |
| **F13** | IoT hash sizing (h_P 3→4) | done v1; **F13-v2 done 2026-05-04** (re-validated under reference v2; 200/200 FH, 199/200 KM with H_AB residual finding consistent with `prob:hab-residual`; NIST 11/12 PASS, BMR N/A) | `reports/f13/` (v1), `reports/refv2/f13/` (v2) |
| **H_AB=6 ablation** | Empirical validation of paper `prob:hab-residual` at reference scale | **done 2026-05-05** — `reference_h_AB6` profile (clone of reference v2 with h_AB=5→6, λ=128 bits, +4 KB/round bandwidth). 50 000 runs (5×10k shards): **0/50 000 KM divergences**, CP upper 95% CI **7.38×10⁻⁵** (below reference v2 observed rate 8.0×10⁻⁵). Iter median 49 (= reference). 4th and most paper-relevant data point on H_AB scaling curve (32/40/48-at-N4096/48-at-N8192 bit). | `configs/reference_h_AB6.json`, `reports/refv2/h_AB6_ablation/` |

---

## 9. Statistical Conventions

| Symbol | Meaning |
|--------|---------|
| **CP CI** | Clopper-Pearson confidence interval. Exact binomial; default 95% two-sided (α/2 = 0.025 per tail). Implementation in `EmpiricalEvaluation/HashFirstAttacker/Reports/ClopperPearson.cs`. |
| **TP / FP** | True Positive / False Positive — used by B.4 oracle metric. |
| **Cohen's d** | Standardized mean difference. Used by B.4 to quantify discriminator strength. d ≈ 0 ⇒ no detectable signal. |

---

## 10. Hardcoded Constants

These constants are **not** configurable via JSON. Changing them requires source
edits.

| Constant | Value | Location |
|----------|------:|----------|
| AES key size | 256 bits | `AES.cs:25` |
| AES block size | 128 bits (16 bytes) | `AES.cs:26` |
| AES mode | CBC | `AES.cs:27` |
| AES padding | None (zero-pad seed up to 16-byte multiple for L<16, F8 fix) | `AES.cs:28`, `ClientB.cs:215+` |
| Rfc2898DeriveBytes iterations | 10 | `AES.cs:13` |
| AES salt | `{1, 2, 3, 4, 5, 6, 7, 8}` | `AES.cs:17` |
| MaxIterations static fallback | 500 | `Configuration.cs` (overridden by JSON `max_iterations`) |
| ProtocolRunner default MaxIterations | `Configuration.MaxIterations` | `ProtocolRunner.cs:15` |
| SHA-256 output length | 32 bytes | (standard) |
| Schema version | "1.0" | `ProtocolConfiguration.cs:26` |

---

## 11. Configuration Profiles

Calibrated profiles ready for use; any new config should derive from one of
these.

| Profile | L | N | h_AB | h_BA | h_P | M | s | p_upd | Purpose | Status |
|---------|--:|--:|----:|----:|----:|--:|--:|------:|---------|--------|
| `reference` | 32 | 4096 | 5 | 2 | **8** | **8** | **4** | 0.75 | Paper baseline v2 (2026-05-02 pivot) | active |
| `low_resource_iot` | 16 | 1024 | 4 | 2 | 4 | 8 | **4** | 0.75 | IoT (h_P=4 since F13; CandMax disabled, s=4 since F13-v2) | **calibrated_f13_v2** (λ=80 bits) |
| `high_security_pq128` | 64 | 8192 | 6 | 3 | 9 | 16 | 4 | 0.75 | Post-quantum high-security (λ=144 bits classical, ~72 bits Grover-bounded per round; M·h_P = 144 B/round) | **calibrated_f11_v2** |
| `f12_s{1..30}` | 32 | 4096 | 5 | 2 | 4 | 16 | varies | 0.75 | F12 SeedMinMax sweep | **STALE** (historical) |

### Research-only configurations (NOT for production)

| Profile | L | N | h_AB | h_BA | h_P | M | s | p_upd | Purpose | Status |
|---------|--:|--:|----:|----:|----:|--:|--:|------:|---------|--------|
| `break_demo` | 8 | 256 | 3 | 1 | 3 | 4 | 4 | **1.0** | Paper `prob:breakdemo` empirical pilot; deliberately weakened (λ=56 bits, sub-extreme; p_upd=1.0 deterministic update) | **research_breakdemo** |
| `reference_h_AB6` | 32 | 4096 | **6** | 2 | 8 | 8 | 4 | 0.75 | H_AB=6 ablation cell for paper `prob:hab-residual` (clone of reference v2, h_AB+1; λ=128 bits, +4 KB/round). 50k runs 0/50k KM divergences, CP upper 7.38e-5. | **research_h_AB6_ablation** |

Active production profile set after 2026-05-04 cleanup: `reference`,
`low_resource_iot`, `high_security_pq128`. Removed: `mobile_bandwidth`,
`high_security_classical` (F11-v2 scope cut), `crack_*bit` (F4 v1 calibration
probes), `f8_*` grid (F8-v2 redirect). All v2 follow-ups closed by 2026-05-05
(F11-v2, F13-v2, F8-v2 closed 2026-05-04; H_AB=6 ablation closed 2026-05-05
with 4th paper-relevant data point on H_AB scaling curve).

---

## 12. Cross-references

- **Specs:** `Zadani.md`, `Zadani-followup.md`, `paper_v6_foundational_asymmetry_TODO.md`
- **Config schema:** `docs/config_format.md`
- **End-user docs:** `docs/README.md`, `docs/USAGE.md`, `docs/REPRODUCE_PAPER.md`
- **Paper drafts:** `paper/v5/`, `paper/v6/`, `paper/paper_v6_TODO.md`
- **Reports index:** `reports/` (per-task subdirectories)
