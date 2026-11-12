# Production test checklist (sv with tl)

For a first real-project test of sv 2.1.0 with tl on .NET 10 or Unity 7. Frame order and modes:
`docs/USING-WITH-TL.md`. Unity specifics and open Unity sharp edges: `docs/UNITY.md`.

## Enable

1. **One frame order** (USING-WITH-TL section 3). Deltas written by tl in frame t fold in t+1.
2. **Columns declared once**: `Links.Validate` returns `LinkFault.None` at load; pin `Links.Hash` and
   `Schema.ContentHash` in your own test next to the game's state hash.
3. **Stats to host columns**: `board.BindSink(stat, true)` once at setup, drain `TakeBroadcast` every
   frame. Rollback, `LoadKeyframe` and `LoadState` refill the sink; drain fully after each.
4. **Bounds from stats**: `Bounds.Stats(board, ...)` (sv columns) or `Bounds.StatRows(floor, ceiling)`
   (host float columns). Never `(int)` a stat; `Clamp.BoundOf` is the only conversion.
5. **Remote writes are integers**: tl kind -> row-local effect column -> send pass. Use one
   `InboxLanes` lane per worker (`Flush` at the end of the pass); it costs the same as `Inbox.Send`
   and less when many sends hit few slots.
6. **Events**: one `EventDerive` per channel, cap at host build (default 16,384, max 16,777,216). Every
   handler must accept the `DenseEvent` + edge-word form; above the cap no per-row event exists.
7. **Digest**: `SvHost.Install` defaults to `digestEvery: 8` in every build; pass `digestEvery: 1` in a development player (`DigestCadence.Default` is the core-level default: every tick in debug, every 8 in release). `digestEvery: 0` turns the digest off. Log
   `(tick, Board.Digest, CellDigest sums per intrinsic column)` on due ticks.
8. **Test builds**: run once with `-p:DefineConstants=SV_CHECKED` (Unity: add `SV_CHECKED` to Scripting Define Symbols) (range guards throw; after `InvalidateBindings` a bound column parks instead of throwing, see issue 0151), and run
   `sh eng/isa-legs.sh` on each machine you compare.
9. **Unity**: `eng/unity-sync.sh sync <project>/Packages/com.sv.core`, then `check` in CI (the sums need LF files: `.gitattributes` pins that); the repo copy
   lives in `packages/com.sv.core`.

## Expected numbers (measured, Ryzen 5 8500G 6C/12T)

| What | Value | Source |
|---|---|---|
| .NET P1 (1,048,576 rows), W 2.5, 0 % cross, 6 workers, stand-in kinds | ~6.1 ms/tick total, sv stages ~3.3 ms | issues 0125, 0130 |
| same, 10 % cross to one slot, `InboxLanes` | 7.3 ms (atomic 8.0 ms) | issue 0130 |
| .NET digest of 2 x 1M intrinsics | 1.2-1.4 ms when computed (hence K = 8) | issue 0125 |
| Unity Burst P1, W 2.5, 0 % cross, sv + stand-in kinds | ~4.7 ms/tick | issue 0127 |
| same with tl's real Damage kind job, 11 workers | sv 2.35 ms, tl kinds 8.45 ms, frame 10.83 ms (gate covers sv only) | verifier, issue 0143 |
| gate | 8.3 ms per tick at P1 (25 % of 30 Hz) | plan |
| warm managed allocation in sv calls | 0 B (soak: 50,000 ticks) | issue 0133 |
| board memory under churn | flat after warm-up | issue 0133 |

tl's own kind jobs are not in the .NET numbers; budget their time on top.

## Reading a digest mismatch

Compare the per-tick logs and find the **first** differing tick, then:

| Differs between | Likely cause |
|---|---|
| two runs, same host, same workers | uninitialized memory (zero every native column), wall-clock or `Random` without a seed, dictionary order in the glue |
| worker counts (1 vs N) | a float summed across rows, a non-integer remote write, a store column written during the tl step, a send pass that is not inside the frame |
| hosts or ISA legs | float math outside `StatMath`/`BoundOf`, FMA introduced by your code, a Mono/IL2CPP path not in the legs |
| `Board.Digest` only | modifier stream differs: StatOp order (always `Canonicalize`), expiry ticks, add+remove of one (slot, source) in one batch (the remove sorts first) |
| one intrinsic's `CellDigest` only | that column's deltas or inbox: bisect by partition, then by slot |
| live vs replay | something outside the journal changed state |

## Failure modes

| Signal | Meaning |
|---|---|
| `InvalidDataException: keyframe does not fold onto this base` | the receiver was not at the keyframe's base (missed a keyframe, or local edits not rolled back) |
| `InvalidOperationException: slot exhaustion` | board capacity too small for live entities |
| `stale binding — storage moved; rebind` (SV_CHECKED) | a bound column moved; rebind before the next fold |
| `ArgumentException: ... does not cover the window` | delta, inbox or changed column shorter than the fold window |
| `DenseEvent` on a channel | edges above the cap this frame; read the edge words, not per-row events |
| host stat column stale | sink not drained to zero, or bound without fill |
| frame over the gate | look at the stage split: fold and digest are DRAM-bound, cross-row sends live in the kinds stage |
