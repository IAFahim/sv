# 0531 v4: an event index outside the pack read another event

Status: closed (2026-11-26, fixed: refused with ArgumentOutOfRangeException)

Found by: the 2026-11-26 triage and review pass.

## What was wrong
`Event(slot, e)`, `Entered(slot, e)` and `ChunkEntered(chunk, e)` shifted the 32-bit events lane by `e` unchecked. C# masks a shift count to 5 bits, so `e = 32` read event 0 and `e = -1` read event 31, and an index past the declared events read 0 silently. The same class of bug as 0515 (channels).

## Fix
All three throw `ArgumentOutOfRangeException` unless `0 <= e < EventCount`. Test: `SourceAndFrameTests.AnEventIndexOutsideThePackIsRefusedInsteadOfAliasingAnother`.
