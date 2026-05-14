# Empirical Evaluation Suite — Hash-Drift CKA Protocol

Self-contained Windows distribution of the empirical evaluation framework for the
Hash-Drift Continuous Key Agreement (CKA) protocol. Companion artifact for the paper
*Grata Cascade: A Hash-Drift CKA Construction*.

No .NET runtime is required on the target machine — each executable embeds its own
runtime.

## Quick start

1. Unzip the distribution to any folder.
2. Open PowerShell or `cmd` in the unzipped folder.
3. List available commands:
   ```
   .\bin\EvalSuite.exe list
   ```
4. Run the standard reproduction sequence (~10 minutes on reference hardware):
   ```
   .\bin\EvalSuite.exe paper-reproduce
   ```
   Results land in `reports/repro/`.

## Layout

```
EmpiricalEvaluation-vX.Y.Z-win-x64/
├── bin/
│   ├── EvalSuite.exe           ← orchestrator (recommended entry point)
│   ├── HashFirstAttacker.exe   ← protocol runner + attackers (B.1 .. B.7)
│   └── NistTests.exe           ← NIST test battery
├── configs/                    ← JSON configuration profiles
│   ├── reference.json          ← reference v2 parameters used in paper
│   ├── reference_h_AB6.json    ← v3 reference candidate (h_AB=6 ablation)
│   ├── low_resource_iot.json   ← IoT profile
│   ├── high_security_pq128.json ← post-quantum high-security profile
│   ├── break_demo.json         ← research-only break-demo (paper §sec:adv-b7)
│   └── f12_s*.json             ← SeedMinMax sweep configs (F12, historical)
├── reports/                    ← output directory (initially empty)
├── README.md                   ← this file
├── USAGE.md                    ← detailed command reference
├── REPRODUCE_PAPER.md          ← reproducing the paper's empirical claims
└── config_format.md            ← JSON config schema reference
```

## Requirements

- Windows 10/11 x64
- ~500 MB RAM for typical convergence/NIST runs
- ~200 MB free disk for outputs (more for big-eval campaigns)

## Typical runtimes

Reference hardware: 11-core CPU with SHA-NI extensions enabled.

| Command                                         | Runtime          |
|------------------------------------------------|------------------|
| `EvalSuite.exe paper-reproduce`                | ~10 minutes      |
| `EvalSuite.exe convergence --runs 1000`        | ~1 minute        |
| `EvalSuite.exe nist-run --runs 1000`           | ~5 minutes       |
| `EvalSuite.exe b1 --budget-seconds 60`         | ~1 minute        |
| `EvalSuite.exe b5-extended --runs 300`         | ~30 minutes      |

CPUs without SHA-NI hardware extensions are an order of magnitude slower; check
`bench-sha` to confirm.

## Two equivalent invocation styles

EvalSuite is a thin orchestrator over the underlying tools. Both forms work:

```
.\bin\EvalSuite.exe convergence --runs 100
.\bin\HashFirstAttacker.exe convergence --runs 100
```

Use `EvalSuite.exe` when you want the curated command catalog and the
`paper-reproduce` pipeline. Call the underlying tools directly when you need
options not yet surfaced through the orchestrator.

## Documentation

- **USAGE.md** — full command reference for every subcommand
- **REPRODUCE_PAPER.md** — step-by-step reproduction of the paper's empirical claims
- **config_format.md** — JSON configuration schema reference

## Source code

The source code, full reports, and historical task list (`Zadani-followup.md`) live
in the project repository. This distribution contains only the compiled binaries
and minimal documentation needed to run the evaluations.
