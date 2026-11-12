# Issue 0129 - Keyframe/state load refills a bound sink; Bounds.StatRows over a host float column

Core agent, branch `proto2/core`. Closes the "not done" line of issue 0120. Labels: `ai`, `area:core`.

## Changes

- `Board.LoadKeyframe` and `Board.LoadState` end with `RefillSink()` (the same call
  `RollbackTo` makes since 0120): every bound sink stat is refilled from the restored board
  and the cursor rewinds. Why: `LoadKeyframe` runs `Recalculate`, which clears the changed
  masks, so rows changed before the load but not yet drained by the host, and untouched by
  the keyframe (identical pages skip), were lost and the host column stayed stale.
- `Clamp.StatColumns(float* min, float* max)` and `Bounds.StatRows(float* min, float* max)`:
  stat-strategy bounds over any float column indexed by slot (a host stat column filled by
  the sink, a chunk component), same `BoundOf` conversion and fold loops as
  `Bounds.Stats(board, ...)`. At least one bound, else `ArgumentException`. No generation
  check (the host owns the storage). Additive API; Inbox/Board/StatOp signatures unchanged.

## Tests (new): `tests/Sv.Tests/SinkKeyframeTests.cs` (2, also in the portable leg)

- 10 rounds: receiver (sink bound with fill on 4 stats) runs 3 predicted undo frames with
  partial drains, rolls back, drains, applies the authority's frame itself, takes 4 records,
  then loads the authority's keyframe: digest equal, pending = live x stats, host columns =
  authority `Board.Stat` bits for every live cell. **Fails on the old code** (verified by
  stashing the fix), passes with it.
- `Bounds.StatRows(null, hostMaxHp)` vs `Bounds.Stats(board, null, max_hp)` on 1,024 rows x
  8 ticks with saturating deltas: values, changed words and `CellDigest.Sum` equal, and both
  equal `IntrinsicLaw.Step` with `Clamp.BoundOf`; `StatRows(floorHost, ceilingHost)` equals
  the reference too.

## Receipts

`dotnet test sv.slnx -c Release`: Sv.Tests 497/497, Sv.Portable.Tests 381/381, Sv.NorthStar
27/27, Sv.NorthStar2 9/9, Sv.Unity.Tests 1/1. No production comments, no FMA.
