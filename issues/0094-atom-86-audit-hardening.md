# Issue 0094 — Atom 86 (hardening, atoms 77–83 audit): wheel sanitize on load, aligned fresh-free, bulk fault surfacing

Follow-up to the read-only audit of atoms 77–83 (commits `0835da0..bf4482f`)
that also root-caused issue 0092. Labels: `ai`, `area:core`, `kind:feat`.
Contract: none.

## Changes

- **`RebuildWheel` sanitizes the `Wheel` backlink on load**
  (`src/Sv/Board.Wheel.cs`): the walk sets `records[at].Wheel = -1` on every
  chained record before `WheelInsert` overwrites the expiring ones. The
  persisted record pool round-trips the field as raw bytes, so a save whose
  non-expiring records carry a stale index (only possible from a pre-0092
  build) previously loaded as a live-looking entry index into a
  differently-sized pool — the wild-unlink shape. Derived state no longer
  survives a load as state.
- **Rollback frees fresh aligned blocks through the aligned deallocator**
  (`src/Sv/UndoLog.cs`, `src/Sv/NativeArena.cs`, `src/Sv/Board.Stats.cs`):
  `UndoQuarantine` gains an `Aligned` flag, `QuarantineFreshAligned` marks
  materialized owned columns and mirrors (the two aligned
  fresh-quarantine sites), and `DropFreshFrom` branches to
  `NativeArena.FreeBytesAligned` (`NativeMemory.AlignedFree`).
  Formally the wrong deallocator before (C11 requires the aligned free);
  glibc tolerates the mismatch, which is why nothing ever failed.
- **A throwing bulk slice surfaces on the caller** (`src/Sv/Bulk.cs`):
  worker exceptions are captured (first wins, via `Interlocked`), every
  participant still completes the round, and the caller rethrows the
  captured failure with its original stack — a partially applied window can
  no longer return as success. The caller's own slice routes through the
  same capture. The pool is cleared for the next round.
- Deleted `src/SV_REVIEW.md` — an atom-41-era design review superseded by
  the roadmap findings (history preserved in git and issue 0048).
- **Not** deleted: `Guard.EnabledTail` — the audit flagged it dead, but it
  has three live callers in `src/Sv.Frent` (`Population.cs:271`,
  `ChunkSeam.cs:137,186`). Audit false alarm, recorded here.

## Tests (total 384)

- `WheelTests.SaveLoad_RoundTripsChainedAndUnchainedRecords` — a board
  with one expiring and one non-expiring modifier saves and loads;
  `PendingExpiries` is exact across load, removal of the non-expiring
  record (the 0092 path, post-load) and expiry.
- `UndoTests.Rollback_DropsFreshlyMaterializedColumns` — materializing an
  owned column inside an undo window bills exactly
  `capacity × sizeof(float)`; rollback drops the fresh aligned column (the
  `DropFreshFrom` aligned path), `MemoryBytes` returns to the mark, and the
  next read re-materializes with correct values.
- `BulkTests.FaultInASlice_ThrowsOnTheCaller_AndLeavesThePoolUsable` —
  under `SV_CHECKED`, a stale stat clamp makes worker slices throw and the
  caller rethrows `InvalidOperationException` (fault injection exists only
  in checked builds — Release dereferences stale columns raw); both modes
  then prove the pool stays usable with `Bulk.Run` == `Kernel.Run` over
  8,192 rows.

Honest receipts: the sanitize and the aligned free are defense-in-depth —
no red/green test exists by construction (the corruption vector needs a
pre-fix save; glibc tolerates the mismatched free). The new facts pin the
observable paths; the write-path invariant is pinned by 0092's regression
test.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **384/384 passed** (381 + 3);
  3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 384/384
  (the bulk fault fact exercises the rethrow here).
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (keyframe 315 µs, rollback-resim 374 µs, bulkpass 1.88×, renumber1m
  9.8 ms RenumberSlots + 24 µs Recalculate — within its ≤ 20 ms / ≤ 1 ms
  gates; run-to-run variance on this host spans ~5–10 ms depending on how
  many scenarios ran before it), alloc 0 B — hardening only.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@cd9d2b2` + this atom. Next: nothing executable without the owner
decisions (bus 0043–0049) or the v1.2 batch fold (bus 0036/0042/0051).
