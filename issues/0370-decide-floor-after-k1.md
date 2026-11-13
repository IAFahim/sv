# 0370 Decide after K1: 17 ns per single-record target, 46 ns per record when rules read random source cells; what is left needs more than the emitter

Status: open (design: cross-entity reads; per-target dispatch)

Found by K1 (branch v3/k1-decide, after d390be0). Lego pack, Ryzen 5 8500G, Release, median of 3 processes x 50 frames.

Receipts:
- `v3-lego-decide-shape` (one chunk, L1-hot, dummy template): Tick 64x1 1,044 ns, 8x8 603 ns, 1x64 549 ns; Physical 64x1 2,699 ns,
  8x8 1,602 ns, 1x64 1,463 ns. So about 8 ns per extra target and 8.6 ns per Tick record (chunk set-up included), 23 ns per
  Physical record with sources in cache.
- One worker, whole frame: B Decide 17.4 ms = 17.4 ns per record (one Tick per target); C Decide 99.0 ms = 46 ns per record
  (2,165,758 records, half of them Physical/Healing/Fire/Poison from random sources).
- 11 workers: A 1.02, B 3.81, C 12.91, D 18.17 ms (plan 15.2: 0.2, 1.7, 2.6, 5.0). B scales 4.6x from 1 to 11 workers on 6 cores.

What limits it:
1. Cross-entity reads. A Physical record's gates read five lanes of its source (RageStacks, Execute, CritCard, CritRoll,
   RageBonus) plus the source's meta word; with random sources these are cache misses, about twice the cost of the same
   record with sources in cache (46 vs 23 ns). The cost model's 4-8 ns per record assumes record-local work. Options, all
   outside the emitter: Seal or Collect attaches the source attributes rules read to the record, or a per-frame source-side
   table (one row per source with the read lanes) built once and read sequentially.
2. Per-target fixed cost, about 8 ns: Target call (11 arguments), 18 rule call guards, assign set-up, settle. A per-channel
   dispatch for single-channel targets (switch on the one present channel, call only rules that list it) would cut the guard
   chain; not done.
3. Tried and reverted: gating the per-chunk card and row masks by the channels present in the chunk (B Decide 4.74 vs
   4.34/4.70 ms, A 1.25 vs 1.27 ms: no measurable gain).
