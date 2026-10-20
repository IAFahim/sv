# Issue 0049 — Atom 42: `LoadState` keeps mirrors alive and rebroadcasts bindings (review S-2, S-3)

Filed as GitHub issue #29 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), findings S-2/S-3 —
verified against source before fixing.

## Problem

- **S-2:** `ReadStats` freed `mirrors[stat]` when the snapshot lacked it. A host-cached
  `Clamp.StatMirror*` holds that raw pointer (`Clamp.cs` bakes it into a readonly
  struct), so rolling back to a snapshot older than the mirror's creation left every
  cached clamp reading freed memory — writes through the SIMD verbs could hit whatever
  the allocator placed there. The mirror doc promised "capacity-sized, never moves".
- **S-3:** externally bound columns were never marked dirty on load, so the restored
  `PairMeta`/sets said "already broadcast" and the bound columns kept the abandoned
  future's values until something else dirtied the stat.

## Shape

- `ReadStats` never frees a mirror. Snapshot-has-mirror + local-null still allocates
  (nothing cached points at a fresh block) and reads the content bit-exactly.
- Snapshot-lacks-mirror + local-mirror: the local block is kept; for unbound stats it
  is refilled from the restored output column via the same fill loop `MirrorBounds`
  uses (extracted to `FillMirrorFromColumns`); bound stats wait for the rebroadcast.
- After the set table is restored, every stat with a bound column or a mirror is
  `MarkStatDirty`-ed, so the next `Recalculate` rebroadcasts the restored values into
  bound columns and mirrors. Sets are restored bit-exactly, so recomputed values are
  identical — determinism unaffected.
- `Board.LoadState` doc updated to state the mirror/binding contract.

## Receipts

- `MirrorRollbackTests` (4 facts): mirror pointer stable across a rollback to a
  pre-mirror snapshot and refilled to the restored bound; snapshot mirror restores
  bit-exactly; bound column rebroadcast from restored state after load; the added
  dirty marks leave restored outputs unchanged.
- `StateRollbackTests` (bit-identical digests over 60 frames, same and fresh boards)
  unchanged and green — the dirty marks do not perturb determinism.
- Suite: build green; 215 tests pass (23 NorthStar + 6 Unity + 186 Sv.Tests).
