# Grata Cascade

**Live demo:** [**gratacascade.com**](https://gratacascade.com/) — paper-companion
Blazor WASM + SignalR testbed. Open two browser tabs (or one tab + a friend's
phone), pair, and compare the Safety Number out-of-band. Public testbed; not
to be used with sensitive keys. Deployment guide: [`deploy/README.md`](deploy/README.md).

A **one-shot key agreement primitive** built on a foundational probabilistic
asymmetry derived from the generalised birthday problem, **without**
number-theoretic (Diffie–Hellman, RSA) or lattice-based (ML-KEM) assumptions.
Positioned as a candidate **third independent layer** in hybrid post-quantum
key establishment (e.g.\ X25519 ⊕ ML-KEM-768 ⊕ Grata Cascade), where
efficiency is secondary to assumption diversification. Extension to a
*Continuous* Key Agreement (CKA) primitive — for messaging-style continuous
key rotation — is an open research direction (see `prob:cka-extension` in
paper §9).

## Status

- **Paper:** v9 (active) — [`paper/v9/files/paper_v9_en.tex`](paper/v9/files/paper_v9_en.tex),
  [`paper/v9/files/paper_v9_cz.tex`](paper/v9/files/paper_v9_cz.tex). v9 is a
  **scope correction** of earlier framings: reframed from "CKA construction"
  to "one-shot KE primitive" (the protocol outputs one K* per session and
  never claimed CKA's FS/PCS guarantees; v9 makes the framing match what is
  actually proved/measured). See [`paper/v9/CHANGES.md`](paper/v9/CHANGES.md)
  for the per-section diff. PDF rebuild via `pdflatex` / `latexmk`
  (no toolchain in repo).
- **Reference configuration v2** (2026-05-02 pivot, paper v9 baseline):
  `L=32, N=4096, h_AB=5, h_BA=2, h_P=8, M=8, s=4, p_upd=0.75`,
  `λ_preimage = 120 bits`. Full schema in
  [`configs/reference.json`](configs/reference.json).
- **Reference v3 candidate** (closes paper Open Problem `prob:hab-residual`
  empirically — 0/50000 KM divergences, CP upper 7.38×10⁻⁵ below the v2
  baseline 8×10⁻⁵): [`configs/reference_h_AB6.json`](configs/reference_h_AB6.json)
  (`h_AB = 5 → 6`, `λ = 128 bits`, +4 KB/round at N=4096). Promotion pending
  v3 cutover decision.
- **Active production profiles:** `reference` (v2),
  `low_resource_iot`, `high_security_pq128`. Research-only: `break_demo`,
  `reference_h_AB6`. See [`docs/glossary.md`](docs/glossary.md) §11.

## Empirical headline

Reference v2 evaluation (full reports and reproducibility artefacts under
[`EmpiricalEvaluation/reports/`](EmpiricalEvaluation/reports/)):

| Metric                                | Result                       |
|---------------------------------------|------------------------------|
| Convergence runs                      | 5 × 10⁴                      |
| Final-hit rate                        | 100.0000 %                   |
| Keys-match rate                       | 99.9920 % (4 divergences)    |
| Clopper–Pearson upper 95 % CI failure | 2.05 × 10⁻⁴                  |
| NIST SP 800-22                        | 12 / 12 PASS over 1000 keys  |
| Passive adversary classes evaluated   | 5 (B.1, B.2, B.3, B.4, B.7)  |
| K* recoveries                         | 0 / 300 per class            |
| CP upper 95 % CI per attacker class   | 1.22 %                       |
| B.4 oracle Cohen's d                  | 0.009 (no measurable signal) |

H_AB = 6 ablation (`reference_h_AB6`, 2026-05-05): 0 / 50 000 KM
divergences, CP upper 95 % CI 7.38 × 10⁻⁵ — below the reference v2
baseline. Iter distribution identical to v2 (median 49). Bandwidth cost
+4 KB/round at N = 4096.

## Repository layout

```
Grata-Cascade/
├── GrataCascade.Core/                ← protocol library (C# / .NET 9)
│   ├── ClientA.cs, ClientB.cs        ← protocol parties
│   ├── Vector.cs, Stat.cs            ← state primitives
│   ├── AES.cs, SeedProvider.cs       ← key derivation + F8 fix
│   └── Configuration.cs, ConfigLoader.cs, ProtocolConfiguration.cs
├── EmpiricalEvaluation/
│   ├── HashFirstAttacker/            ← attacker battery + diagnostics CLI
│   │   ├── Attackers/                ← B.1 .. B.4, B.6, B.7
│   │   ├── Cli/, Commands/           ← post-R1 refactor (Program.cs split)
│   │   ├── BigEval.cs, BigEvalAttackerRunner.cs, BigEvalPreflight.cs
│   │   └── ProtocolRunner.cs, Transcript.cs
│   ├── NistTests/                    ← NIST SP 800-22 battery
│   ├── EvalSuite/                    ← release-distribution orchestrator
│   └── reports/                      ← empirical artefacts referenced by paper §6
│       ├── golden/                   ← byte-exact reference outputs
│       ├── convergence_post_f1.md    ← reference v2 convergence baseline
│       ├── probability_thresholds_calibration*.md ← F4 filter calibration
│       └── sha256_*, rng_migration_notes.md
├── Demo/
│   ├── Demo.Client/                  ← Blazor WASM client (Alice + Bob)
│   ├── Demo.Server/                  ← ASP.NET Core SignalR relay
│   └── Demo.Shared/                  ← DTOs (lobby + pair handshake)
├── configs/                          ← JSON profile configs (per-deployment)
├── docs/
│   ├── README.md                     ← release-distribution README
│   ├── USAGE.md                      ← per-command reference
│   ├── REPRODUCE_PAPER.md            ← reproduce empirical claims
│   ├── glossary.md                   ← parameters / F-tasks / profiles
│   └── config_format.md              ← JSON config schema
├── paper/
│   └── v9/                           ← active paper (main + popular companion, CZ + EN)
├── deploy/                           ← production deployment (Caddy + systemd)
└── scripts/                          ← build + run shell / pwsh scripts
```

## Quick start (developer)

```pwsh
# Build
dotnet build EmpiricalEvaluation/HashFirstAttacker/HashFirstAttacker.csproj

# Smoke-test the protocol (1 round, prints K*)
./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe `
    smoke --config configs/reference.json

# Convergence pilot (1000 runs, ~30 min on reference hardware)
./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe `
    convergence --config configs/reference.json --runs 1000 `
    --output EmpiricalEvaluation/reports/local_smoke_conv.md

# Full attacker battery (B.5-extended, 300 transcripts × 5 attackers, ~6 h)
./EmpiricalEvaluation/HashFirstAttacker/bin/Debug/net9.0/HashFirstAttacker.exe `
    b5-extended --config configs/reference.json --runs 300 `
    --output EmpiricalEvaluation/reports/local_b5_extended.md
```

End-user / reviewer entry point: see [`docs/REPRODUCE_PAPER.md`](docs/REPRODUCE_PAPER.md).

## Documentation index

- **End-user / reviewer:** [`docs/README.md`](docs/README.md),
  [`docs/USAGE.md`](docs/USAGE.md),
  [`docs/REPRODUCE_PAPER.md`](docs/REPRODUCE_PAPER.md),
  [`docs/config_format.md`](docs/config_format.md).
- **Source of truth for parameters / F-task index / profile catalogue:**
  [`docs/glossary.md`](docs/glossary.md).
- **Paper drafts:** [`paper/v9/files/`](paper/v9/files/) (active) +
  [`paper/v9/CHANGES.md`](paper/v9/CHANGES.md). A bilingual popular companion
  is alongside — [`paper_v9_pop_cz.tex`](paper/v9/files/paper_v9_pop_cz.tex) /
  [`paper_v9_pop_en.tex`](paper/v9/files/paper_v9_pop_en.tex) — same factual
  content as v9 main, written for an audience that knows hashes and AES
  but not formal cryptography (≈30 PDF pages, 7 TikZ diagrams).

## License

This repository is dual-licensed:

- **Code** (`GrataCascade.Core/`, `EmpiricalEvaluation/`, `Demo/`,
  `configs/`, `deploy/`, `scripts/`, `docs/`): [MIT License](LICENSE).
- **Papers** (`paper/v9/files/*.{tex,pdf}`, `paper/v9/CHANGES.md`):
  [Creative Commons Attribution 4.0 International (CC BY 4.0)](LICENSE-papers).

The MIT License is silent on patents — neither granting nor disclaiming
patent rights of the copyright holders.

### Citation

If you use Grata Cascade in academic work, please cite:

> Robert Jarušek. *Grata Cascade: Hash-based One-Shot Key Agreement via
> Statistical Drift, as a Candidate for Hybrid Post-Quantum Key
> Establishment.* Grata Luma s.r.o., 2026.
> <https://github.com/GrataLuma/Cascade>

## Author

Robert Jarušek (2026), Grata Luma s.r.o., Prague.
