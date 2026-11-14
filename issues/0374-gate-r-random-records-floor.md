# 0374 Gate case r (1M random records) is 17.6 ms against 8.3: what the remaining cost is made of

Status: closed (decision D11, 2026-11-14: r is a stress benchmark with a no-regression ceiling of 19.0 ms; the 8.3 ms gate covers chunk-bucketed load, A and B pass). The redesign notes below stay as the plan if a real workload looks like r.

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

## Senior consult applied (2026-11-14, measured on v3/m8-unity 208c5d3, box load ~5, baseline r 18.5-19.9 ms)

- Huge pages: THP is `always` here with `defrag=madvise`; a running r bench had 306 MB of 1 GB in huge pages. `madvise(MADV_HUGEPAGE)` on every allocation of 1 MB or more (in `Memory`) raised that only to 332 MB and r did not move (5 interleaved pairs each, r/b/a). The large malloc regions (181, 122, 62, 22, 16 MB) stay on 4K pages; making them huge needs 2 MB-aligned mmap at allocation, not advice. Reverted. `GLIBC_TUNABLES=glibc.malloc.hugetlb=1`: no change either.
- Fewer workers for bandwidth phases: refuted. r with 6 workers: seal 6.0, decide 11.2, total 23.8 ms; 8 workers 24.4-25.8; 11 workers 18.5-19.9; 12 workers 19.0-20.4. Seal itself gets slower with fewer workers, so it is not purely bandwidth-bound on this box.
- 16 parts instead of 64: r 20.7-21.9 ms (worse), as 0373 found.
- `a` passed by value: not the case, Target takes `in DecideArgs`.
- Source gather (A5): already largely done by P3's per-frame source-chunk table.
- Whole-process counters (perf stat, r, user mode): IPC 1.8, branch misses 2.1 % of branches, about 8 % of cycles at 15 cycles per miss. Bad speculation is not the main cost; path length and memory stalls are.
- Order-insensitive channel contract (A9): exact by construction, but the lego pack's `immune_by_kind_mask` reads `kind` on every channel and Physical/Healing rules read `record.source`, so no lego channel qualifies statically; it needs a per-chunk proof and does nothing for r as authored.

Verdict: the remaining trims (Order kernel, per-channel dispatch, hash at commit) are each estimated under 1 ms on r. The senior's own floor for engineering alone is 9-11 ms after week-scale work (single-writer range partition, fused Seal/Log/Decide per range, shadow-lane commit). 8.3 ms for r needs that redesign or a change of the gate (r as a stress test rather than a gate): owner decision.
