# Issue 0070 — Atom 62 (roadmap 2.3): dirty-stat masks — the pair queue and `Queued` deleted; recompute exactly once per pair in topological order

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.3 —
F12, S1; laws L1, L2. Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Per-slot dirty-stat masks replace the FIFO pair queue** (roadmap §4.3). The
  queue, `pairQueueCount`/`pairQueueCap` and `PairMeta.Queued` are deleted. In
  their place: `statMaskWords = ⌈stats/32⌉` `uint` words per slot (`dirty`,
  zero-initialized at create, 4 B/slot at ≤ 32 stats), a touched-slot list
  (`dirtyTouched`, growable high-water like the old queue), a per-stat
  **dependents mask** (`dependentsMask`, same word shape) and an `allStats`
  mask. A slot enters the touched list when its mask goes from all-zero —
  no `Queued` byte anywhere.
- **`Recalculate` resolves each (slot, stat) at most once, in topological
  order.** The touched list is heap-sorted ascending, and within a slot the
  lowest dirty stat is popped first; declaration order is already topological
  (`Derived` only accepts earlier-declared sources), and a changed set ORs its
  dependents mask into the slot's pending words — bits only move upward, so a
  derived pair is never resolved against a stale source value and never
  resolved twice. This deletes the integer division used to decode queue pairs.
  F12 closed: no transient sets (a stale-content set interned then abandoned
  within one pass), and no changed bit from an intermediate broadcast whose
  value the final resolution reverts — `ChangedWords` keeps its single meaning
  ("final value differs").
- `MarkStatDirty(slot, stat)` replaces `EnqueuePair` at every mutation site
  (add/remove/expire/source-removal); `MarkSlotStatsDirty` (acquire, renumber)
  sets the full `allStats` mask.
- `RenumberSlots` drops and rebuilds the dirty state exactly as it dropped the
  queue (all live slots fully dirty — the O(pending) renumber is atom 3.3's).
- **`PairMeta` becomes a bare `int` set id** per (slot, stat) — meta pages are
  `int` pages, `sizeof(PairMeta)` 8 → 4 B. Board state format **v5**: the
  persisted dirty state is the compact touched list (slot + its mask words,
  O(dirty) bytes like the old queue, never capacity-sized), and meta pages
  persist as `int`. v4 saves rejected via the state shape.
- `MemoryBytes`: queue term replaced by `dirty` (4 B/slot at ≤ 32 stats) +
  touched-list high-water. Set-creation order across a `Recalculate` is now
  sorted (slot, stat) instead of FIFO append — internal only, values stay
  bit-identical.

## Tests

- New `DirtyMaskTests`: the F12 fact — derived marked before its source in one
  frame resolves once against the settled source, so a row whose final value
  equals its previous value carries **no** changed bit (pre-fix the stale
  intermediate broadcast marked it); exactly-once (a source+dependent frame
  leaves no transient live set and no double broadcast — pinned via
  changed-mask emptiness and `SetCount` stability); L2 idempotence
  (`Recalculate; Recalculate` — second call broadcasts nothing); save→load
  mid-frame (dirty pairs pending across a save are restored and still resolve).
- Eager-oracle random programs (L1), save/load round-trips (L4), reclamation
  churn (L14), guard-page fuzz: green, unmodified.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **269/269 passed** (265 + 4 new); 3×
  repeat runs stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 269/269.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  **ff1m p50 2,566 µs / p99 3,542 µs** (Δ=128), **board 762 → 642 MiB,
  snapshot 432 → 368 MiB** (set-id pages halve to 4 B/pair at 16 stats), rss
  734 MiB, stat-recalc 33 ns/op, stat-churn 26 ns/op, alloc 0 B everywhere;
  no scenario regressed.
- FMA grep clean; no production comments.
- `MemoryBytesTests.MillionSlotBoard` bounds updated to the new bill: the
  empty 1M × 3-stat board carries the flat 4 MiB dirty-mask array (4 B/slot —
  roadmap §5.3's own line), and full-board meta pages are 4 B/pair
  (35.8 MiB, was > 40 with 8 B `PairMeta`).

## Provenance

`main@c1f77d0` + this atom. Next: 2.4 — modifier instance pool with 32 B
records; delete the `PairMods` pages.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded here per
AGENTS.md discipline.
