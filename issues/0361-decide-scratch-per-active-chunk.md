# 0361 Decide scratch is 18,944 B per active chunk (74 lanes, 56 of them channel totals): 296 MB at 1M, plan says 8 MB

Status: open (layout of the host/emitter scratch contract; not fixed in the benchmark phase)

Found by BM1 (branch v3/bench). Lego pack, 1,000,000 entities, 11 workers.

Receipts:
- `Kernels.ScratchLanes = 74` (18 delta lanes + `Totals` at `scratch + 1152`: 28 channels x 64 rows x 8 B). `FrameLoop` keeps one
  scratch block per active-chunk index (`context->Scratch + index * ScratchInts`) because Commit reads the deltas and Events /
  EdgeEmits receive `scratch` (and declare `totals`).
- `v3-lego-memory`: the first Step after spawning 1M entities makes every chunk active (spawn marks rows pending), so
  `activeCapacity` grows to 15,627 and stays: decide scratch 296.0 MB. With 32 epochs of history at 10 % activity the process
  holds 450-757 B per entity; scratch is the largest single item (plan 15.3 projects "frame scratch 8 MB", total 210 MB).
- Time: clearing 18.9 KB per chunk is 8.9 ms of 78 ms Decide at one worker and 18.6 of 180 ms CPU at 11 workers. Experiment
  (not committed): scratch per part instead of per chunk (wrong results, timing only) took B Decide 17.3 -> 14.9 ms.

Proposal: keep only the 18 delta lanes per active chunk (4.6 KB, 72 MB at 1M) and give `Totals` a per-part block that stays in
L1/L2, cleared per touched row. That needs Events/EdgeEmits to stop taking totals from the per-chunk scratch (they declare
`totals` but the lego and reference packs never read them), so it is a contract change between host and emitter.
