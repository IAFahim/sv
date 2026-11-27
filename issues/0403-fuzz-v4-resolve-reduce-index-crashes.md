# 0403 fuzz: v4: an out-of-range Resolve part or Reduce writer crashed the process

Status: closed (2026-11-26, fixed: out-of-range indexes throw ArgumentOutOfRangeException)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api, sv commit d929d03

## What was wrong
`ColumnWorld.Resolve(part)` and `Reduce(writer)` took any int. `Resolve(2)` on a 2-part world read scratch memory past its block and `Reduce(-1)` indexed the writer array from the wrong end; under the fuzzer this ended in an access violation or a double free that killed the test host, not an exception.

## Fix
Both check the index first and throw ArgumentOutOfRangeException, leaving the frame as it was. The `ColumnFrame` pointer API stays unchecked on purpose (Burst).

## Tests
`RedTeamWorldTests.A_resolve_part_or_reduce_writer_out_of_range_is_refused_and_changes_nothing` (-1, parts, int.MaxValue, int.MinValue).
