# Issue 0120 - Sink: fill existing rows on bind, rewind on undo rollback

Agent D (sv-frame), branch `proto/d-frame` from proto/int 71e30f7. Closes the two "not done"
lines of issue 0118. Labels: `ai`, `area:core`, `kind:fix`.

## Baseline (proto/int 71e30f7, this Windows host, before any change)

`dotnet test sv.slnx -c Release -p:NuGetAudit=false`: Sv.Tests 476/476, Sv.Portable.Tests
360/360, Sv.NorthStar 27/27, Sv.Unity.Tests 13/13.

## Changes

- `src/Sv/Board.Sink.cs`: new `BindSink(StatId stat, bool fillExisting)`. With `true` the next
  broadcast carries every live row of that stat (current canonical bits, ascending
  (slot, stat)), merged with the changed rows; the fill survives Recalculate calls until the
  broadcast is fully drained. `BindSink(stat)` keeps its old meaning (no fill), because the
  existing test `Sink_CarriesCanonicalNaNBits_AndNothingWithoutSinks` pins "binding after a
  row exists broadcasts only changed rows" and existing tests are never edited.
- `src/Sv/Board.Undo.cs`: `RollbackTo` ends with `RefillSink()`: the cursor rewinds to slot 0
  and every bound sink stat is refilled from the restored board, so a chunked host that took
  records of rolled-back frames converges to the restored bits.

## Tests (new): `tests/Sv.Tests/SinkFillTests.cs` (3, also run in the portable leg)

- bind-with-fill on 160 live rows of 200 acquired (40 released), drained 13 at a time:
  exactly live x 2 records, ascending, bits = canonical `Board.Stat`; nothing after; a later
  plain `BindSink` adds no fill.
- fill survives a Recalculate between bind and first take (70 rows, the modified row carries
  the new value).
- 12 rounds of 5 undo frames (window 6), partial drains before rollback on odd rounds,
  rollback 1-4: pending = live x stats, first record = lowest live slot, host columns equal
  `Board.Stat` for every live cell.

## Receipts

Sv.Tests 479/479, Sv.Portable.Tests 363/363. No production comments, no FMA.

## Not done

Keyframe load still resets the cursor without a refill (host calls `BindSink(stat, true)`).
