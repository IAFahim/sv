# 0374 Gate case r (1M random records) is 17.6 ms against 8.3: what the remaining cost is made of

Status: open (design: the gate needs fewer passes over the records and a cheaper Decide per record, not trims)

Found by P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2, final f8b53af). Lego pack, 1,000,000 entities, 11 workers,
digest every frame, Ryzen 5 8500G, Linux 7.2, .NET 10.0.403, Release; median of 5 processes x 50 frames, interleaved.

Scenario r: 1,000,000 random cross-entity records per frame (Physical 50 %, Healing 25 %, Fire, Poison; random target and source),
1,165,758 canonical with thorns, lifesteal and regen records, digest 9CFD52083A9DA3FB.

| ms | send | seal | log | decide | commit | events | digest | end | total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 6c219e2 | 0.76 | 4.64 | 1.19 | 10.18 | 1.23 | 0.15 | 2.32 | 0.26 | 20.88 |
| f8b53af | 0.77 | 4.28 | 1.22 | 8.42 | 1.29 | 0.15 | 1.13 | 0.27 | 17.64 |

Where it goes now:
- Memory passes over the 16-19 MB of records: Send writes them, Seal Count reads them, Scatter reads and writes them, Order reads
  them twice (by row into Scratch, merge back), the input log reads them and the previous frame's segments, Decide reads them once
  more. Roughly 120 MB per frame (estimate from the passes, not measured with counters); at the box's ~30 GB/s streaming that is
  about 4 ms before any compute, and Order's input
  lines were written by other cores (cross-core transfers, software prefetch did not help).
- Decide: about 40 ns of core time per record (8.42 ms x about 5.5 effective cores / 1.17M records), compute and branch bound (lone-channel dispatch, per-record channel switch in
  mixed targets, 10-argument rule-method calls, per-target settle and 18-lane delta fold). Source misses are gone (issue 0370).
- Commit and digest: per active chunk lane work for all 15,625 chunks every frame (r touches every chunk).

What could reach the gate (none tried here, each is a design change):
1. Seal and the input log examine in one pass per chunk while the chunk is in L1 (log 1.2 ms becomes mostly the old-segment read).
2. A Decide shape that runs a chunk's records per channel in batches (column-wise gates and totals) instead of per target, or
   plan M12a (delta scratch) for channels no rule tells apart by source, kind or hits; in the lego pack every channel is told apart
   by `immune_by_kind_mask`, so M12a needs a per-chunk proof (no KindMask in the chunk), not a pack-wide one.
3. An incremental digest that rehashes only lanes written this frame.
Estimate (not measured) of what more trims of the current shape could reach: about 15-16 ms.
