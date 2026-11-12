# Issue 0124 - Event arena could shrink to 0 B and hang the next append (pre-existing)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Agent D, branch `proto/d-frame`. Found by the derive bench. Labels: `ai`, `area:core`, `kind:fix`.

`EventStreams.ShrinkArena` shrinks a writer arena above 4,096 B to `Round(peak + peak/8)`. A writer
that never wrote has peak 0, so one idle `Swap` shrank its arena to **0 B**; the next append's growth
loop (`capacity *= 2` until it covers the need) then never terminated (derive bench hung at its second
case after a 0-edge case). Fix: the shrink target is at least 4,096 B (the threshold below which the
arena is never shrunk). Arena bytes are not state and not in any digest.

Test (new): `tests/Sv.Tests/ArenaShrinkFloorTests.cs` - a 1 MiB arena after 4 idle swaps stays
>= 2 x 4,096 B and then takes 1,000 events. Fails on the old code (arena 0 B).

Receipts: Sv.Tests 493/493 in 3 of 4 runs; the 4th run failed `StructMakerTests.StructMaker_WarmEmitAllocatesZero`
once (per-thread 0 B check, passes alone and on rerun; same class as the GC-timing flake fixed at a0963c8).
