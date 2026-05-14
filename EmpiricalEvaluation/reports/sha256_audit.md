> **Historical (reference v1, pre 2026-05-02 pivot).** See current docs/glossary.md and reports/refv2/ for v2 baseline.

# F3.a — SHA-256 Call-Site Audit

**Date:** 2026-04-19
**Scope:** All SHA-256 invocations across `TreeParityMachine_HASH_V2`, `HashFirstAttacker`, `NistTests`.
**Goal:** Identify which call sites still use the managed-only `SHA256Managed.ComputeHash` API (which does not dispatch to SHA-NI hardware) vs. the hardware-accelerated `SHA256.HashData` static API. The migration target is to eliminate `SHA256Managed` from all hot paths; cold paths can remain since `SHA256.Create()` already dispatches to the hardware-accelerated implementation on .NET 9.

## Host context

- CPU: Intel Core i5-1335U (13th gen, Raptor Lake-U). CPUID reports SHA-NI bit 29 in EBX = **True**.
- .NET SDK: 10.0.201 (runtime: .NET 9 per project `TargetFramework`).
- Platform: Windows 11 Home 10.0.26200, x64.

## Call-site table

| # | Call site | File:Line | Per-run frequency | Current API | Post-F3 target | Status |
|---|-----------|-----------|-------------------|-------------|---------------|--------|
| 1 | `Vector.ComputeHash` | `TreeParityMachine_HASH_V2/Vector.cs:78` | ~1-2 M (dominant hot path) | `SecureRandom.Instance.Sha256.ComputeHash(Data)` → allocates 32-byte array per call | `SHA256.HashData(Data, stackalloc Span<byte>[32])` → allocation-free, directly hands the digest span to a new `HASH` span-aware ctor | **migrate** |
| 2 | `ClientA.GetPassword` | `ClientA.cs:170` | 1 per full protocol run | `SecureRandom.Instance.Sha256.ComputeHash(temp.ToArray())` on 512-byte input | `SHA256.HashData(temp.ToArray())` | **migrate** |
| 3 | `ClientB` password hash (per round) | `ClientB.cs:119` | ~100 per full protocol run | `SecureRandom.Instance.Sha256.ComputeHash(temp.ToArray())` | `SHA256.HashData(temp.ToArray())` | **migrate** |
| 4 | `ClientB.GetPassword` | `ClientB.cs:182` | 1 per full protocol run | `SecureRandom.Instance.Sha256.ComputeHash(temp.ToArray())` | `SHA256.HashData(temp.ToArray())` | **migrate** |
| 5 | `SeedProvider.recursive` | `SeedProvider.cs:45` | ≥1 per decoded collision-dict combo | `SecureRandom.Instance.Sha256.ComputeHash(temp.ToArray())` | `SHA256.HashData(temp.ToArray())` | **migrate** |
| 6 | `SecureRandom.Sha256` property / `sha256` field | `SecureRandom.cs:14,30,34` | — (owner of singleton) | Holds `SHA256Managed` instance | **delete** (no callers after 1-5) | **remove** |
| 7 | `KeyCollector.ComputeFileSha256Hex` | `NistTests/KeyCollector.cs:181-183` | 1 per NIST run (cold) | `using var sha = SHA256.Create(); sha.ComputeHash(fs)` | **unchanged** — `SHA256.Create()` returns hardware-accelerated impl on .NET 9 | keep |
| 8 | `TranscriptIO.ComputeFileHashHex` | `HashFirstAttacker/TranscriptIO.cs:118-120` | 1 per transcripts.bin (cold) | `using var sha = SHA256.Create(); sha.ComputeHash(fs)` | **unchanged** — same reasoning as #7 | keep |
| 9 | `BaselineRandomAttacker` | `Attackers/BaselineRandomAttacker.cs:56,82` | attacker inner loop | `SHA256.HashData(v, h)` and `SHA256.HashData(concat)` | — | **already migrated** |
| 10 | `HashFirstFilteredAttacker` | `Attackers/HashFirstFilteredAttacker.cs:107,152` | attacker inner loop | `SHA256.HashData(v, h32)` and `SHA256.HashData(concat)` | — | **already migrated** |
| 11 | `AESOracleAttacker` | `Attackers/AESOracleAttacker.cs:134` | attacker inner loop | `SHA256.HashData(v, h32)` | — | **already migrated** |
| 12 | `BigEval.MeasureSha256Throughput` | `BigEval.cs:482` | diagnostics | `SHA256.HashData(v, h)` | — | **already migrated** |

## Observations

- The protocol core (`TreeParityMachine_HASH_V2`) is the **only** remaining consumer of `SHA256Managed`. The attacker pipelines and the BigEval throughput diagnostic were previously migrated.
- The dominant hot path is `Vector.ComputeHash` (call site #1). With `Configuration.VectorCount = 4096` vectors and 2-3 hash reads per vector per round, a typical 30-100 round protocol run evaluates SHA-256 roughly 1-2 million times from this single call site alone — dwarfing all other call sites combined by two orders of magnitude.
- Cold-path file integrity hashes (#7, #8) are intentionally left on the `SHA256.Create()` factory API — they execute once per run, so overhead is irrelevant, and `SHA256.Create()` already returns the hardware-accelerated implementation.
- `SecureRandom.Sha256` public property is exposed by the V2 core but consumed nowhere outside the protocol code once call sites #1-5 are migrated. Safe to delete.

## Microbenchmark baseline (pre-migration, this host)

Captured with `HashFirstAttacker.exe bench-sha --seconds 5 --protocol-runs 20 --seed 42`:

| Variant | Ops/s (32 B input) | Ratio vs. baseline |
|---------|--------------------:|--------------------:|
| `SHA256Managed.ComputeHash(byte[])` | 7 382 569 | 1.00x (baseline) |
| `SHA256.HashData(byte[])` | 6 352 315 | 0.86x |
| `SHA256.HashData(ReadOnlySpan, Span)` | 5 377 364 | 0.73x |

At 32 B input, each SHA-256 call covers exactly one compression block. Per-call overhead dominates over the actual hash computation, so the three variants converge to 5-7 M ops/s regardless of which internal path is taken (SHA-NI vs. managed fallback). The headline "10× – 50× speedup" from raw hash throughput is **not observable at this input size**; the migration's gain on this host is expected from reduced GC pressure (one fewer 32-byte heap allocation per call) and from removing the `SecureRandom.Instance.Sha256` indirection, not from SHA-NI itself. The post-migration macrobenchmark (in `reports/sha256_perf_comparison.md`) is the authoritative end-to-end measurement.
