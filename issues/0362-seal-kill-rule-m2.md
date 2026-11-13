# 0362 Seal above the M2 gate twice over: kill rule says "counting sort by row; still above -> stop and report"

Status: open (stop and report per plan section 14 M2; the counting sort by row already exists in `Seal.Sort`/`ByRow`)

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
