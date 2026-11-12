# Issue 0130 - Hot-target inbox: per-worker lanes with an adaptive atomic bypass

Core agent, branch `proto2/core`. Plan mode table "D remote ... worse on one hot target". Labels: `ai`, `area:core`, `area:bench`.

## Change

New `src/Sv/InboxLanes.cs` (additive; `Inbox.Send`/`Take`/`InboxBuffer` unchanged):

- `InboxLanes(InboxBuffer, workers, cells = 1024)` owns one unmanaged `InboxLane` per worker
  (`Lane(worker)` returns `InboxLane*`, Burst-usable: pointers + ints only).
- `InboxLane.Send(target, amount)`: a direct-mapped table of (slot, long partial sum); a hit
  is a plain integer add, a collision evicts the old partial with one `Interlocked.Add`.
- Automatic selection: every 4,096 sends the lane checks its eviction count; above half it
  bypasses the table (plain `Interlocked.Add`) for the next 61,440 sends, then probes again.
  Hot targets stay in the table, spread targets cost what `Inbox.Send` costs.
- `Flush()` (per worker, at the end of its send pass) / `FlushAll()` (main thread, worker
  0..N-1) adds the partials into the inbox before the fold.
- Determinism: every path is a 64-bit integer add into the same cell, so any order gives the
  same bits as the serial path; the fold still saturates once in `Inbox.Take`.

## Tests (new)

- `tests/Sv.Tests/InboxLanesTests.cs` (4): 65,536 rows x 3 sends x 3 ticks, 10 % / 0 % / 100 %
  to 3 hot slots, saturating amounts: inbox cells and folded `CellDigest` equal the serial
  `Inbox.Send` path at 1, 2, 4, 11 workers (`Parallel.For`), cells 1,024 and 16, per-worker
  and main-thread flush, shuffled serial partition orders at 7 and 11; bad shapes throw; hot
  slot stays in the lane until flush; bypass engages on spread targets and resets on flush;
  warm sends allocate 0 B.
- `tests/Sv.NorthStar2/NorthStar2LanesTests.cs` (4): `FrameHost(useLanes: true)` reproduces
  the baked NorthStar2 pins `939FBDEE2E7DEE50 / E5CE5FA31824A22E / 75035C43BBDC14AF` at 1, 2,
  4, 11 workers, cells 1,024 and 16, and shuffled send order.

## Benchmark (`frame1m`, new env `SV_FRAME_HOT=1` sends every cross-row write to slot 0, `SV_FRAME_LANES=1`)

Tag: branch proto2/core at this commit. Settings: AMD Ryzen 5 8500G 6C/12T, Windows 11, .NET
10.0.401 Release, default ISA leg (V256), P1 (1,048,576 rows, 1 timeline/row), W 2.5, 6 workers
unless stated, stand-in kinds (no tl), 3 fresh processes per arm (median), bench.lock held,
no Burst (n/a on .NET).

| Cell | atomic total / kinds ms | lanes total / kinds ms |
|---|---|---|
| 0 % cross | 6.179 / 2.661 | 6.139 / 2.664 |
| 10 % cross, hashed targets | 8.570 / 5.372 | 8.597 / 5.412 |
| 10 % cross, all to slot 0 (hot) | 7.982 / 4.773 | 7.290 / 4.042 |
| 10 % hot, 11 workers (1 process) | 7.301 / 4.213 | 6.360 / 3.228 |

Kill rule (lanes cost the normal path > 3 % at 0 % cross -> opt-in): not tripped (+0.1 % kinds,
total within noise). Without the bypass (first draft) hashed 10 % cost +6.9 % kinds; the
bypass removed it. Lanes stay an explicit object (no Send API change); recommended default for
any send pass. On this CPU (one CCD) atomic contention on one slot is milder than feared:
hot atomic is faster than hashed atomic (cache hits), lanes still take 15-23 % off the kinds stage.

## Receipts

Sv.Tests 501/501, Sv.Portable.Tests 385/385, Sv.NorthStar2 13/13, Sv.NorthStar 27/27,
Sv.Unity.Tests 1/1. No production comments, no FMA.
