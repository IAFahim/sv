# 0360 generated Decide costs 27-87 ns per record, the plan's cost model says 4-8 ns; it is 53-68 % of every 1M frame

Status: open (K1 cut Decide 3-7x, see below; the remaining floor is issue 0370)

Found by BM1 (branch v3/bench). Lego pack (55 rules, 22 mechanics), 1,000,000 entities, 11 workers, Ryzen 5 8500G, Release.

Receipts (`dotnet benchmarks/Sv.Bench/bin/Release/net10.0/Sv.Bench.dll <entry> --ms 1000`, median of 5 runs x 50 frames):
- Decide phase: A 2.64 ms (projected 0.2), B 16.5 ms (1.7), C 32.0 ms (2.6), D 42.7 ms (5.0).
- One worker, B: Decide 74.7 ms = 75 ns per record, 4.8 us per chunk. Scaling 1 -> 11 workers is 4.5x.
- `v3-lego-decide-shape` (one chunk, L1-hot, kernel only, scratch clear timed apart at 135 ns):
  Tick 64 targets x 1 record 3,983 ns; 8 x 8 1,943 ns; 1 x 64 1,814 ns.
  Physical 64 x 1 5,558 ns; 8 x 8 3,491 ns; 1 x 64 3,299 ns.
  So about 34 ns fixed per target and 28 ns (Tick) / 51 ns (Physical) per record, even for Tick records that no rule acts on.
  In the frame at one worker: 4.4 us kernel + 0.57 us scratch clear per chunk (probes below), close to the 3.9 us L1-hot
  microbench: the cost is instructions, not memory.
- Stopwatch probes around `kernels.Decide` and `Memory.Clear` in `Phases.Decide` (temporary, not committed): one worker 69.0 ms
  kernel + 8.9 ms clear per frame; 11 workers 161.7 ms + 18.6 ms CPU per frame.
- Note for the next agent: `dotnet-trace --profile dotnet-sampled-thread-time` put 97 % of Decide in `Buffer.ZeroMemoryInternal`;
  removing the clear did not make Decide faster (94.6 ms vs 74.7 ms at one worker, totals then leak). The sampler is wrong here;
  use the probes above.

Where it goes (`tests/Sv.Kernels.Lego/obj/SvPack/Kernels.g.cs`, `Kernels.Target`, Tier1 frame 648 B, 41 call sites):
- rule-major passes: per target, 10 rule methods are called unconditionally (`if (true) SelfRule3001(...)` etc., 10 arguments each),
  each loops over the target's records testing the channel; plus 3 absorb loops, the survive loop, the totals loop, the assign loop,
  the 28-channel `totalled` scan and 18 delta folds. About 17 passes over the records of each target.
- `Gate` is one non-inlined call per record with a `switch` to `AdmitChannelN`.

Proposal (needs C1 / owner, changes the emitter, not semantics): per target compute the channel set present (one OR over its
records) and guard each rule call with its channel mask (most rules name one channel); fuse the per-record passes into one
channel-dispatched pass; emit the 28-channel `totalled` scan only for channels that have totals. Target: the cost model's 4-8 ns.
Kill rule M4 (generated method stops being optimised) is not what happens: the method is optimised, it does too much.

Progress (K1, branch v3/k1-decide, 6a6597c..d390be0; median of 3 x 50 frames, 11 workers, digests unchanged):
- Decide A 3.49 -> 1.02, B 20.82 -> 3.81, C 35.67 -> 12.91, D 47.93 -> 18.17 ms (same machine, baseline re-measured before the
  first change). Decide-shape Tick 64x1 5,142 -> 1,044 ns, 1x64 2,194 -> 549 ns; Physical 64x1 6,769 -> 2,699 ns, 1x64 3,974 -> 1,463 ns.
- Atoms: rule calls guarded by channels present and row masks; one settle pass per record; totalled scan over written channels;
  gate inlined; totals per part (0361); delta section skipped when no source moved plus inlined saturating helpers; hoisted
  repeated source reads; written-lane marks instead of a scratch clear.
