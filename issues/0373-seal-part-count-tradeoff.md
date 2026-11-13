# 0373 Seal part count: fewer Count/Scatter parts help A and B, hurt random-heavy frames (r, C, D)

Status: open (measured and reverted in P3; a per-frame choice is not designed)

Found by P3 (2026-11-14, worktree branch from v3/m8-unity 6c219e2, after the source-chunk table and the Seal order fusion).

Experiment: Count and Scatter ran with `min(Parts, 2 x Workers)` = 22 parts instead of 64 (the count matrix shrinks from
64 x 15,625 to 22 x 15,625 ints, 4.0 -> 1.4 MB); Order, Plan and Compact kept 64 chunk-range parts. Canonical output is sorted,
so digests are unchanged (all five scenario digests equal).

Receipts (median of 5 processes x 50 frames, 11 workers, interleaved in one session, seal / total ms):
- A 0.69 -> 0.53 / 2.42 -> 2.19 (-9.5 %)
- B 2.12 -> 1.87 / 7.70 -> 7.49 (-2.7 %)
- r 4.32 -> 4.55 / 18.25 -> 18.47 (+1.2 %)
- C 7.13 -> 7.45 / 25.52 -> 25.86 (+1.3 %)
- D 11.14 -> 11.57 / 36.48 -> 36.81 (+0.9 %)

Reading: the matrix clear, plan walk and per-part cursor rows cost in every frame (A, B win); with random records each part's
Scatter covers more blocks and the slowest of two parts per worker sets the end (r, C, D lose). The gate case is r, so it was
reverted. Open: choose the Count/Scatter part count per frame from the raw arena (ordered blocks vs unordered), or size the matrix
by the chunks a part touched (issue 0362).
