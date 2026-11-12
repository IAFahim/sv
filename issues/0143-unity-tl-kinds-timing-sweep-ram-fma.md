# Issue 0143 - U3 timing + U4: tl's generated kind jobs at 1M rows, worker sweep, P2/P3, RAM, FMA

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Harness: `Tests/Tl/SvTlBench.cs` (`-executeMethod Sv.Unity.TlTests.SvTlBench.Run`), script
`tests/Sv.Editor/bench/run-bench-p2.sh`, raw lines in `tests/Sv.Editor/bench/receipt-p2/`.
Settings: Unity 7000.0.0a16 batch, CoreCLR 10.0.10, Burst on, safety checks off, job debugger off, leak
detection off (logged per process), `-job-worker-count` N, 3 fresh processes per N, 32 warm ticks,
6 x 32 timed (round 0 dropped), median. P1 = 1,048,576 rows, tl timelines `w2` (2 Poison feeders) and
`w3` (3) on alternate rows = W 2.5 feeders per row into tl's generated Damage kind job (writes `HpDelta`),
cross-row 0 %, net delta 0 per row, Iw = 1 (hp: stat ceiling + inbox + 30/40 % condition), send pass on
(`Outgoing` -> inbox, all zero), digest every 8, 874 StatOps/tick. Probes: `SvProbe` and tl
`TimelineKindTick.ManagedRuns` read 0 in every process; main-thread GC 0 B over 192 timed ticks.
sv commit = this commit (send scan: OR-reduce a chunk before the send loop, 0.70 -> 0.61 ms at 11 workers).

| workers | svMain | scatter | fold | send | derive | digest | **sv total** | tl kinds (sv+tl window) | tl only (null window) | frame total [range] |
|---|---|---|---|---|---|---|---|---|---|---|
| 11 | 0.218 | 0.143 | 1.258 | 0.610 | 0.013 | 0.068 | **2.31** | 8.28 | 8.28 | 10.56 [10.25-10.91] |
| 7 | 0.174 | 0.119 | 1.459 | 0.694 | 0.013 | 0.066 | **2.53** | 9.47 | 9.51 | 11.99 [11.97-12.13] |
| 4 | 0.175 | 0.119 | 1.894 | 0.992 | 0.012 | 0.067 | **3.26** | 12.41 | 12.44 | 15.71 [15.68-15.90] |
| 2 | 0.182 | 0.122 | 2.717 | 1.526 | 0.012 | 0.087 | **4.65** | 18.30 | 18.08 | 22.91 [22.84-23.10] |

- Gate (sv <= 8.3 ms per tick, P1): met at every worker count; 60 Hz line (4.2 ms) met at 4+ workers.
- Honest finding: tl's own generated kind work at 1M rows is 8.3 ms per tick at 11 workers (18.1 ms at 2),
  more than the whole 25 % budget by itself; frame total 10.6 ms. sv is 22 % of the frame. Not an sv
  change; tl owners decide (no tl change made, none needed for sv).
- Setup at 1M (world, 1M instantiate, Bind, 699k timeline starts, first frame): 0.9-2.2 s.
- The send pass costs 0.6 ms at 1M rows with zero sends because tl's job takes write access to `Outgoing`
  in every chunk every tick, so every chunk passes the change filter and is scanned.

P2/P3 stand-in gate cells (phase-1 harness `SvBench`, archetype now carries `SvSlotCleanup`, 3 processes,
11 workers): every cell under the gate; worst sv total P2 1.14 ms (W 4, 50 %), P3 2.18 ms (W 4, 10 %);
W 2.5 cells P2 0.81/0.92/0.85 ms, P3 1.24/1.24/0.99 ms at 0/10/50 %. Table: `receipt-p2/standin-*.txt`
through `summarize.py`.

RAM (V11, 1M rows, measured): board 18.58 B per row after setup (19.5 MB with 437 live modifiers), host
native 33.13 B per row (34.7 MB: slot->entity 8, pending list 8+4+4, inbox 8, condition + edge bits,
64k-write scatter buffer), sv ECS columns 32 B per row + 1 enable bit (SvSlot, SvSlotCleanup, hp, delta,
max_hp, armor, target, outgoing); board 58.76 B per live modifier (100,000 added).

FMA: all 146 Burst JIT dlls in the harness cache disassembled with `objdump -d`: `vfmadd/vfmsub/vfnmadd`
appear only in 4 dlls, all `SvV0.V0StatFoldFastJob` (the phase-0 FloatMode.Fast probe, the positive
control). The dlls that contain `SvFoldJob`, `SvScatterJob`, `SvSendJob`, `SvDigestJob` have 0 FMA.

Locks: 16:19:15-16:20:xx (11-worker pre-run, `p2-out/bench-pre`), 16:22:34-16:37:42 (sweep).
