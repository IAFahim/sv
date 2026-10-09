# Issue 0027 — Atom 22: bench rate label off by 1000x; stray probe allocations in `SetTable`

Filed as GitHub issue #27 (pending — see receipts). Labels: `ai`, `area:bench`,
`area:core`, `kind:fix`.

## Problem

1. `benchmarks/Sv.Bench/Program.cs` printed `rows / (us * 1000)` under the label
   `M rows/s`. That quantity is rows per nanosecond (G rows/s), so every published
   rate — the CI table and the README prose — understated throughput 1000x
   (intrinsics-uniform 100k rows in 52 us was printed as "1.92 M rows/s"; the true
   rate is 1.92 G rows/s).
2. `SetTable`'s constructor allocated, wrote and freed two `probe` blocks — debug
   residue with no function, paid on every board with stats.

## Shape

- Rate is now `rows / us` — rows per microsecond is exactly M rows/s.
- README prose receipts corrected to G rows/s; the CI table refreshes itself on the
  next push to main (the bench markers are not hand-edited).
- Probe allocations removed.

## Follow-up (after atom 27)

`samples/Sv.Samples` (MillionSample, FrameSample) carried the same rows/ns-as-M-rows/s
formula; fixed to rows/us. (`M broadcasts/s` in MillionSample was already correct.)

## Receipts

- Local bench (Ryzen, AVX2), post-fix labels: intrinsics-uniform 16.2 us = 6158.9 M
  rows/s; intrinsics-statclamp 31.4 us = 3180.7 M rows/s; enabled-runwalk 63.0 us =
  1587.8 M rows/s; events-emit 466 us; frame-run 951 us; tape-digest 1510 us. This is
  the baseline the following perf atoms (23+) are measured against.
- Tests default: 139 + 16 + 6 passed, 0 failed. SV_CHECKED: 141 + 16 + 6 passed.
- no-FMA grep clean.
