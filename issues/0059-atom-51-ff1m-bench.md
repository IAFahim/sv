# Issue 0059 — Atom 51 (roadmap 0.1): the FF-1M benchmark — the real gap, measured

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 0, atom 0.1. Scenario
`ff1m` in `benchmarks/Sv.Bench/Ff1mBench.cs`, wired into the suite (`all` and the CI
README refresh). Labels: `ai`, `area:core`, `kind:perf`. Contract: none.

## Workload (Roadmap.md §2.2, verbatim)

- 2²⁰ live entities (capacity 1,048,576 — exactly 64-aligned, no padding).
- 16 stats: 12 base + 4 derived (`atk`/`defense`/`power`/`crit_cap` off `strength`/`armor`/`crit`),
  8 bound to host-read columns.
- 4 intrinsics (hp/mana/stamina/shield), each `Clamp.StatMirrorMax` against its `max_*` stat,
  driven by dense regen passes every frame (hp +2, mana +3, stamina +1, shield +1).
- 2 timed modifiers per entity (armor Multi 1.25, haste Multi 1.1), expiries spread over
  262,144 ticks → ~8 due per frame.
- 1 timeline (`FfTimeline`: Apply walk of ids/clocks, dense Transform against the bound atk
  column, Advance of clocks) and 4 event channels (Hit/Heal/Buff/Expired).
- Busy frame Δ = 128: 64 hits (full-word `RunEnabledChunk` on hp), 32 heals (half-mask word
  on mana), 8 buffs, ~8 expiries (`ExpireModifiers`), 8 spawns + 8 despawns (balanced, in
  the Reconcile step).
- Clause-7 stages all present: drain, reconcile, intrinsics, recalc, timeline (with the
  4 MiB effect clear), conditions (dense `ChangedWords` walk ×4 stats, applied zeroing), swap.

The timed frame is managed-allocation-free (asserted per batch); the bench verifies itself
against a scalar model (probe rows, a hit row, the exact armor fold 10×2.25×2.5 = 56.25,
drained-event count, live count) before timing.

## Receipts (AMD EPYC 7763, 2 cores, this box, 2026-10-22)

- `dotnet run -c Release --project benchmarks/Sv.Bench -- ff1m --ms 1500`:
  **p50 3,134 µs/frame, p99 4,129 µs/frame** (1,048,576 entities, Δ = 128), `alloc=0B`.
- Memory: `board.MemoryBytes` **887 MiB** (Roadmap §2.2's byte model predicted
  ≈ 890–955 MiB — confirmed within ~1%), game-owned 12 MiB, one `SaveState` snapshot
  **558 MiB** (an 8-frame rollback window of today's snapshots would be **4.4 GiB**;
  the phase-5 undo-log target is ~0.1 MiB), RSS after setup 976 MiB.
- `perf stat -e LLC-load-misses,LLC-store-misses` over a 213-frame run (whole process,
  setup included — an upper bound): 11.1 M load misses + 4.5 M store misses ≈
  15.6 M / 213 frames ≈ **≤ 73 K LLC misses per frame ≈ ≤ 570 per mutation**.
- Full suite with ff1m folded in: `all --md` green in 21 s wall, every scenario `0 B`;
  `dotnet test sv.slnx -c Release` 220/27/6 passed.
- The 5 µs budget against the measured frame: **3,134 µs / 5 µs ≈ 627×** — the gap the
  roadmap closes. Dominant terms are the four dense regen passes (≥ 334 µs each per the
  README receipt), the 4 MiB effect-column clear, dense mask walks and the per-frame mask
  clearing in `Recalculate`.

## Notes

- The CI bench refresh picks the `ff1m` row up automatically (`grep '^|'`); p99 rides in
  the rate column, memory details print in non-markdown mode only.
- F14 (world copied per frame) forced the clamps and the atk column pointer into statics —
  the bench is single-instance, so this is safe here; the finding stands for phase 1.6.
- Rollback snapshots are measured (one `SaveState`), not taken per timed frame — today's
  per-frame save would add ~hundreds of ms and is a phase-5 deletion, not part of the
  steady frame.

## Provenance

`main@bef9fd6` + this atom. Numbers are measured on this box; the roadmap's estimates
stay labelled as estimates.
