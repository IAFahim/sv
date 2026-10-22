# Issue 0068 — Atom 60 (roadmap 2.1): set reclamation — sets free when empty, slots and spans recycle

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.1 —
F11, law L14. Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Refcount = member count** (`F11`): every slot-pair referencing a set is a set
  member, so `SetTable.RemoveMemberAt` frees the set when the last member leaves
  (content change in `Recalculate`, `ClearSlotStats` on release). `Board.SetCount`
  now reports **live** sets (`LiveSetCount`), not sets-ever-created.
- **Deterministic LIFO slot reuse, gated by a generation horizon**: freeing pushes
  the slot on a free list stamped with the current generation; `Recalculate` calls
  `SetTable.Tick()` (generation++). A slot is reusable only when
  `generation − freedAt > ReclaimHorizon` (constant 0 today) — the hook the phase-5
  rollback window will raise so a rolled-back frame never resolves into a recycled
  slot. Between frees and the next `Tick`, contents re-resolve into fresh slots, so
  churn within one frame cannot alias a just-freed identity.
- **Backward-shift bucket deletion** (Knuth Algorithm R): the freed set's bucket is
  removed without tombstones and the probe chain repaired, so `Resolve` never walks
  a dead entry; `GrowBuckets` rehashes live sets only and the load-factor growth
  check counts live sets.
- **Content reuse by size class**: every set's content occupies a power-of-two span
  (`ClassSize(count)`); freed spans return to a per-class LIFO free list threaded
  **in-band** through the arena itself (the span's first 4 bytes hold the next
  offset), `CreateSet` pops the exact class before growing the arena. No
  per-set-span side arrays.
- **Totality of the reclaim path**: a freed set is marked by `setFreedAt != 0`
  (zeroed on reuse; every growth `Realloc` zeroes the new tail); the dirty queue
  may still hold stale entries — `TryDequeueDirty` skips dead sets (reprocessing a
  revived slot is idempotent, and `FreeSet` clears the dirty flag); freed member
  arrays are freed and re-allocated lazily by `AddMember`.
- **Persistence (set-table format v3)**: `Write`/`Read` carry the full reclamation
  state — live set count, free-list head/links, freed-at stamps, generation, span
  lengths, class-list heads — no compaction, byte-exact round trips. Uninitialized
  allocator bytes never enter a save: fresh `setFreeNext` slots are zeroed and every
  span's `[count, classSize)` tail is cleared on creation (the two-board determinism
  fact pins this). `SetTableFormatVersion` 2 → 3 rejects v2 saves loudly.
- `SetTable.MemoryBytes` counts the new arrays (and drops a stale phantom
  `ulong`/set from the old formula).

## Tests

- `SetReclamationTests` (6 facts): table-level free/generation-gating/LIFO order,
  neighbor resolvability across backward-shift deletions (8-set churn, odd sets
  freed and revived), board-level 40-round add/expire churn — `SetCount` 4 ↔ 1
  every round and `MemoryBytes` **byte-stable** after warm-up (law L14: memory
  bounded by live contents, not churn count), release frees the last-referenced
  set, save→mutate→load→resave byte-identical across churn, and two boards running
  the identical churn program serialize byte-identically (determinism of every
  reuse decision).
- NorthStar `RollbackTests`: the "must exercise timed modifiers" guard now tracks
  during the stepped frames — the old form read `SetCount > 1` after all modifiers
  had expired (total-ever-used to pass; live sets correctly reach 1 there).
- Existing coverage unchanged: `StatsTests.SetCount` facts (no frees in those
  scenarios), eager-oracle random programs now exercise free/reuse churn
  bit-for-bit, `SetTableSpreadTests` load law holds against live counting.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **261/261 passed** (255 + 6 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 261/261.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok;
  ff1m p50 3,126 µs (vs 3,134), board 890 MiB (+3 MiB = the new per-set arrays at
  ~½ M slots; churn-bounded memory is this atom's law, the absolute cut is the
  rest of phase 2), snapshot 560 MiB, alloc 0 B on every row.
- FMA grep clean; no production comments added.

## Provenance

`main@8dcfcbf` + this atom. Phase 2 continues with 2.2 (member arrays per set →
board-wide).
