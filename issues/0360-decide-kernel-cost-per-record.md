# 0360 generated Decide costs 27-87 ns per record, the plan's cost model says 4-8 ns; it is 53-68 % of every 1M frame

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

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

Progress (P2, branch v3/p2-perf, 15560e6; median of 5 x 50 frames, interleaved with the previous commit, digests unchanged):
- `Target<Lone{c}>`: a target whose records share one channel runs a generic copy with constant present/channel, so the gate
  switch, ChannelIndex, the assign set test and every presence guard fold. Decide B 4.04 -> 3.22, C 13.87 -> 13.39, D 19.47 ->
  19.00 ms. decide-shape Tick 64x1 1,115 -> 822 ns, Physical 64x1 2,864 -> 2,360 ns (3ddf93e -> 152bee6).

Progress (P3, 2026-11-14, see issue 0370 for the receipts): cross-entity reads through a per-frame source-chunk table with template
value rows. Decide r 10.18 -> 8.42 ms (1M random records, 1,165,758 canonical), C 13.11 -> 11.41, D 18.73 -> 16.36 (6c219e2 ->
f8b53af, one session). Still about 7.2 ns per record wall at 11 workers, roughly 40 ns of core time (11 threads on 6 SMT cores); the rest is per-target scalar work (dispatch on the lone channel
and, in Mixed targets, per record; 10-argument rule-method calls; settle and delta fold per target). The plan's 4-8 ns per record
needs a different execution shape, not more emitter trims.

## Decision (2026-11-14)

Closed by decision D20: the whole-frame gate is met for chunk-bucketed load (B 7.7-7.8 ms); the remaining Decide cost and the redesign that would cut it are recorded in issue 0374.
