# 0370 Decide after K1: 17 ns per single-record target, 46 ns per record when rules read random source cells; what is left needs more than the emitter

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

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

## P1 findings (branch v3/p1-perf, not merged: all reverted)

- Per-frame source table (mark distinct sources of canonical records in a slot bitmap, rank per word, gather the 6 source lanes
  the Lego gates read in slot order, gates read `row[column]` through a `SourceTable` with fallback to the world read when a
  slot is unmarked): exact (digests unchanged) but slower. C: Decide 13.0 -> 14.8 ms, total 30.1 -> 33.9 ms (build phase about
  2 ms, mostly contended bitmap marking). The table is about 17 MB for 630k distinct sources, so its one random row read is an
  L3/DRAM miss.
- Why: most source lanes the gates read (RageStacks, Execute, CritCard, CritRoll, RageBonus) are still template lanes
  (copy-on-write), so they hit cache; the real misses are the per-chunk `LaneOf` row and the meta word, about 2-3 L3 lines per
  record, not 5 lanes.
- A warm pass per chunk that touches each record's source meta and lanes before the gates: Decide C 15.9 ms (worse).
- Upper bound: with every source read returning its fallback (semantics changed, measurement only) Decide C is 11.0 ms, so
  all source reads together cost at most 2 ms of C's 13.
- What could still pay: a table smaller than L2 (for example a per-slot 16-bit id into deduplicated rows, ids rebuilt only for
  chunks whose source lanes changed), or carrying the read values in the record at send time (contract change).

## P2 (branch v3/p2-perf)

- Item 2 done in 15560e6 as a type-parameter specialization (`Target<Lone{c}>` / `Target<Mixed>`, dispatch on the lone channel
  in Decide): B Decide 4.04 -> 3.22 ms; decide-shape Tick 64x1 822 ns (12.8 ns per target), 1x64 468 ns.
- Mixed-channel targets (C, D: Tick plus a random record) still run the generic path; Decide C 13.2 ms, D 19.1 ms.
- Tried and reverted: initializing `a.Assign` only before its first use (B 3.42 vs 3.45 ms, C -0.26, inside noise).

## P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2; median of 5-9 processes x 50 frames, interleaved, digests unchanged)

- Profile first (perf + jitdump, scenario r = 1M random records): in `Target<Lone0>` about 18 % of samples sat on two loads per
  record, the source chunk's lane-table entry (`LaneOf[chunk * 46 + meta]`) and its meta word; in `Target<Mixed>` about 13 %. That
  confirms the P1 reading (lane-table row + meta word, not the five lanes).
- Software prefetch was measured first and dropped: two-stage prefetch of the next chunks' lane-table rows and meta lines from
  `Phases.Decide` cut Target samples by 1.8 points but the warm loops cost 3.4 (r decide 10.36 -> 10.66 ms); prefetching the next
  chunk's table entries once the table existed: r decide 9.76 -> 10.05 ms. Reverted both.
- Kept fd1c108: a per-frame source-chunk table (idea 4 at chunk granularity, 24 B per chunk, 375 KB for 15,625 chunks): Live|Dying,
  a bit per lane (first 64) that still points at the template lane, the template. Built by a new parallel phase right before Decide
  (0.17 ms, bandwidth of the lane-table rows and meta lines) and skipped when no record has a source (B). RuleMath.Present/Read in
  every kernel go through `SourceChunks`. r decide 10.13 -> 8.96, total 19.58 -> 18.58 ms; C 27.16 -> 26.29; D 38.86 -> 37.35.
- Kept (next commit): the table entry points at the template's value row (template lanes are uniform pattern lanes with epoch 0,
  never written in place; event lanes excluded), so an on-template source read is one load instead of three dependent ones
  (`EmitRule3007`, lifesteal's `source.Lifesteal`, was 4.9 % of samples). r decide 8.92 -> 8.39, total 18.14 -> 17.56 ms (9 rounds);
  C 25.43 -> 24.89; D 36.55 -> 35.47.
- Law: `SourceChunkTableReadsEqualWorldReadsForEverySlotAndLane` (Shared and Dense, every slot incl. absent and out of range,
  every lane incl. event and retired).
- Not done: carrying source values in the record at send time (contract change, and the table removes most of the miss);
  per-record CSE of the source entry across gate and emit methods (emitter, separate methods); compact entries (the table is an L3
  hit now: `bt` on the Present word is the remaining stall, 12 % of `Target<Lone0>` samples before the value rows).

## Decision (2026-11-14)

Closed by decision D20; the floor analysis and the measured follow-ups (source-chunk table kept, prefetch and huge pages refuted on this box) are in issue 0374.
