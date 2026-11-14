# 0362 Seal above the M2 gate twice over: kill rule says "counting sort by row; still above -> stop and report"

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

Found by BM1 (branch v3/bench). Lego pack, 1,000,000 entities, 11 workers, 64 parts, records sent by 11 writers.

Gate M2: Seal <= 0.4 ms per 1M pre-bucketed records and <= 3 ms per 1M random records, 11 workers; above 2x -> counting sort by
row inside the chunk; still above -> stop and report.

Receipts (median of 5 runs x 50 frames, after commit d45b099 moved Seal.Plan onto the workers):
- B, 1,000,000 pre-bucketed Tick records: Seal 3.01 ms (gate 0.4, 2x = 0.8). Was 3.69 ms before d45b099.
- r, 1,000,000 random records (1,165,758 canonical with derived): Seal 5.0 ms (gate 3, 2x = 6): above the gate, under 2x.
- C (1M pre-bucketed + 1M random): 9.15 ms; D (3M, half random): 14.35 ms.
- Split of B before d45b099 (temporary probes): count 0.61, plan 0.99, scatter 0.74, order 0.57, compact 0.76 ms. C: count 1.40,
  plan 0.78, scatter 2.28, order 3.54, compact 1.63 ms.
- Parts 16 / 32 / 64 / 128 (`SV_BENCH_PARTS`): B Seal 2.94 / 3.01 / 3.69 / 4.48 ms before d45b099: the parts x chunks count
  matrix (4 MB at 64 parts) is cleared, filled and walked every frame whatever the record count.

What a fix needs (design): pre-bucketed blocks still pay route + count + scatter copy + ascending check + merge copy + compact
copy (four passes over 16 MB); a pre-bucketed fast path that seals a writer's ordered blocks in place, and a count matrix sized by
the chunks a part touched, are layout/algorithm changes.

## Progress (P1, branch v3/p1-perf)

- 74b8239: scatter writes the canonical buffer directly; Order sorts with Scratch as temp and merges back; a strictly ascending
  chunk needs no copy; Compact runs only when a merge moved a chunk start (then Records -> Scratch and the buffers swap).
  `Seal.Settle` is the new last step for every driver. Seal B 3.15 -> 2.06 ms, C 9.17 -> 7.95 ms (median of 5).
- 915e3cb: Count writes a record back only when it moved or an earlier record was dropped. Seal B 2.13 -> 2.03, C 8.14 -> 7.85 ms.
- Split now (B / C, ms): count 0.68 / 1.45, plan 0.41 / 0.32, scatter 0.72 / 2.32, order 0.39 / 4.0.
- Not done: count matrix sized by touched chunks. In B a part's blocks come from all 11 writers, so a part's chunk span is
  the whole world; a touched-chunk list needs a per-chunk part order for the plan (deterministic order rule), measured upper
  bound of the gain 0.15-0.4 ms in B (16 parts vs 64). Pre-bucketed blocks still pay route + one copy + ascending check;
  skipping the check needs per-(part, chunk) "single ordered run" flags.
- Gate: still above (B Seal 2.0 ms vs 0.4; 1M random 4.6 ms vs 3).

## P2 (branch v3/p2-perf, no Seal change kept)

- Split now (temporary probes, means), B / C: count 0.79 / 1.55, plan 0.43 / 0.31, scatter 0.72 / 2.26, order 0.47 / 4.67 ms.
- Tried the per-chunk "single ordered run" flag (set in Scatter when a chunk got anything but one repeat-free run of an ordered
  block starting at its base; Order skips Shape when clear): B Seal 2.13 -> 2.01 but Decide 3.83 -> 3.94, total unchanged
  (8.77 -> 8.78); C 29.38 -> 29.86, D 41.67 -> 42.15, A 3.21 -> 3.34. Reverted. In B the record passes are bandwidth-bound: the
  canonical records are read once by someone, and skipping Shape moves the misses into Log and Decide.

## P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2)

- Split for r (1M random, temporary probes, means incl. warm frames): count 0.98, plan+scatter 1.46, order 2.96 ms.
- Kept 77b0129: Order returns at the first descent instead of scanning the whole chunk, and merges each sorted row straight into
  the segment (no separate Merge pass). Order step r 2.96 -> 2.48 (-16 %), C 4.62 -> 3.81 (-18 %); Seal phase r 4.66 -> 4.32,
  C 7.94 -> 7.13, D 12.15 -> 11.14 ms; A, B unchanged.
- Tried and reverted: prefetching the segments of the chunk two ahead in Order (r seal 4.31 -> 4.36, total 18.26 -> 18.35);
  Count/Scatter on 2 x workers parts instead of 64 (A -9.5 %, B -2.7 %, r +1.2 %, C +1.3 %: issue 0373).
- Gate: still above (1M random: Seal 4.3 ms vs 3; B 2.1 ms vs 0.4). What is left is memory traffic: the scattered lines are
  written by one core and read by another in Order (cross-core transfers), and Count and Scatter both read the raw blocks.

## Decision (2026-11-14)

Closed by decision D20: the per-phase M2 Seal sub-gate is superseded by the whole-frame gate, which A and B meet. Seal ideas and the single-writer range partition are recorded in issue 0374.
