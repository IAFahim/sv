# Issue 0083 — Atom 75 (roadmap 5.4): Incremental keyframes — cost tracks dirty pages

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 5, atom 5.4.
Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Dirty-page tracking** (`src/Sv/Board.Keyframe.cs`): a per-board bitmap over the
  digest pages (256 slots, the atom-72 `pageDigests` granularity) plus a count,
  lazily allocated on the first slot mutation and freed when a load re-bases the
  epoch. The bits are set as a side effect of the existing slot funnels —
  `MarkStatDirty`/`MarkSlotStatsDirty` (every modifier add/remove/expire, acquire),
  `TakeFreeSlot`/`Release` (live-bit changes) and `Spawn`'s template loop — so a
  frame that changes nothing marks nothing and there is no capacity scan anywhere
  on the tracking path. `KeyframeDirtyPages` exposes the count.
- **`SaveKeyframe(SvState)`**: writes only the dirty pages — page index, the
  page's live-word slice, and every live slot's modifier records (id, stat, kind,
  value, source, expiry — original insertion ids preserved) — plus
  `nextInsertionId` and the board digest for verification, through the atom-71
  `SaveCodec` into a warm `SvState` (managed-allocation-free). Taking the keyframe
  resets the epoch; an open undo window stays open.
- **`LoadKeyframe(SvState)`**: folds one keyframe onto a board whose state is the
  epoch base: per dirty page, slots whose live bit cleared are released through
  the board's own `ClearSlotStats`/`ZeroSlotRows` paths, slots whose live bit set
  acquire, and live slots whose shipped record chain differs (identical chains are
  compared field-wise and skipped) have their records replaced in order; expiry
  entries are re-pushed from the shipped records; the board then recalculates the
  touched slots and verifies its digest against the shipped one, throwing on
  mismatch (a keyframe that does not fold onto the base is a desync, loudly).
  Sets and blocks are re-derived through the board's own content-addressed
  tables, so the restored board genuinely continues simulation.
- **Epoch discipline**: `LoadState` and `LoadKeyframe` re-base the epoch (the
  loaded state is the new base, and the lazily-allocated bitmap is freed —
  `ShrinkTests`' exact memory bill holds); `RenumberSlots` and `RollbackTo`
  conservatively mark every page dirty (their slot remapping/content reversion
  is not page-trackable, and the next keyframe must re-ship).
- **Bench** (`benchmarks/Sv.Bench/KeyframeBench.cs`): the `keyframe` scenario —
  2²⁰ entities, 96 scattered modifier edits + `Recalculate` + `SaveKeyframe` +
  `LoadKeyframe` onto a second board loaded from the epoch base (the late-join
  loop), digest-verified at the end of every cycle, allocation-free.

The law is deliberately **not** byte equality of full saves: the receiver re-interns
content-addressed sets/blocks along its own allocation history, so table layouts
legitimately diverge while every observable — digest, output columns, `Stat()`
folds — matches and continued frames stay identical. Shipping the global tables
to force byte equality would make keyframe cost O(live sets), contradicting the
receipt.

## Tests (`KeyframeTests`, 3 facts; total 333)

- `Keyframe_AppliedToBase_MatchesFullState` — the law: a base `SaveState` plus a
  chain of six keyframes over churn (adds, `RemoveSource`, expiries, a release,
  an acquire, a `Spawn`) folds to digest equality after every keyframe, and at
  the end: live counts, digests, both output columns bit-exact, all `Stat()`
  folds equal; both boards then run three further identical frames and the
  digests stay equal (the restored board is a real participant, not a cosmetic
  copy).
- `Keyframe_DirtyTracking_CountsPagesNotSlots` — two edits in one page count
  once, a third page counts, reads (`SaveState`, `Stat`, `OutputPtr`) and
  `Recalculate` add nothing, `SaveKeyframe` resets to zero, `LoadState` re-bases.
- `Keyframe_IncrementalChain_EvictsCleanPages` — after an epoch reset, a
  4-page Δ ships exactly 4 pages, the next 2-page Δ exactly 2 (smaller buffer),
  the third 3-page Δ exactly 3 — pages clean since the previous keyframe are
  evicted — and folding the three-link chain from the single base matches the
  source digest and column.

All 330 pre-existing facts green and unmodified. Note: the `ComposeRollbackTests`
family (atom 74) flakes ~1 run in 8 on pristine `main@868b30e` as well
(`Rollback_RestoresEventsPendingDrain`/`Rollback_MatchesTheGameSnapshotAtTheMark`,
reproduced with this atom's sources fully stashed); it is unrelated to this atom
and clean full 333/333 runs were recorded in both build modes.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **333/333 passed** (330 + 3 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 333/333.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (both modes).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok; the
  new `keyframe` scenario (2²⁰ entities, Δ = 96 scattered edits; one iteration =
  edits + recalc + `SaveKeyframe` + `LoadKeyframe` + digest check) runs
  **~267–330 µs/cycle, allocation-free, digest-verified**. Ad-hoc scaling probes
  (same program shape, best-of-N): save alone **48 µs at 2²⁰ entities with Δ=96
  vs 53 µs at 2¹⁶ entities with the same Δ** — a 16× capacity increase moves
  nothing, there is no capacity scan — while Δ = 384 → 162 µs and Δ = 3072 →
  964 µs (fold/load side 96 → 316 → 1955 µs): **cost ∝ dirty pages**. For
  contrast, a full `SaveState` at 2²⁰ costs ~261–300 µs but ships 9.3 MB; the
  Δ=96 keyframe ships 109 KB.
- Guard scenarios unchanged within noise: ff1m p50 2881 µs (Δ=128, board 151 MiB),
  spawn1m 4.72 ms, tape-record 0.032 µs, rollback-resim 331 µs, fused-tick
  701.7 µs (695.7 before this atom, +0.9 % — the two-op `MarkKeyframeDirty`
  riding the existing mutation funnels).
- FMA grep clean; no production comments; no debug instrumentation.

## Provenance

`main@868b30e` + this atom. Next: 6.1 — `Sv.Core`: unmanaged state + static
kernels; classes become owners; unmanaged `Clamp` (S9).
