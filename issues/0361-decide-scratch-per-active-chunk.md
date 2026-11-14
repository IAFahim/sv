# 0361 Decide scratch is 18,944 B per active chunk (74 lanes, 56 of them channel totals): 296 MB at 1M, plan says 8 MB

Status: closed (K1, branch v3/k1-decide, commits 8a72ecc and d390be0)

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

Fix (K1): channel totals live in one per-part block of Channels x 64 longs that the kernel keeps zero (`IPackKernels.TotalLanes`,
Decide argument `totals`; a pack whose events or edge emits read a total keeps them in the per-chunk scratch). The per-chunk
scratch is the 18 delta lanes (74 -> 18 lanes, 18,944 -> 4,608 B per active chunk, 296 -> 72 MB at 15,627 chunks; 14,336 B
per part). The kernel marks each delta lane it writes (`ulong* written`) and clears a lane on its first store, so the host
no longer clears scratch and Commit scans only marked lanes.
Receipts (median of 3 x 50 frames, 11 workers): Decide B 10.40 -> 7.45 -> 3.81 ms, A 2.57 -> 1.55 -> 1.02 ms; Commit B 2.21 ->
1.71 -> 1.20 ms. Digests A-D unchanged; Laws 516, Scenario 80, Diff 18, Host 27, Lego 119 green, also on no-AVX2 and scalar.
