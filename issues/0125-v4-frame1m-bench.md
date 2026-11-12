# Issue 0125 - V4: frame1m bench (P1, P2, P3 x W x cross-row, stage split)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Agent D, branch `proto/d-frame`. Plan step V4 measurement. Labels: `ai`, `area:bench`.

`benchmarks/Sv.Bench/Frame1mBench.cs` (`dotnet Sv.Bench.dll frame1m`, `SV_FRAME_WORKERS`, `SV_FRAME_SHAPE`).
One frame per tick, the order of `docs/USING-WITH-TL.md` section 3: StatOps (0.05 x W x T x 2/300 per
tick, add at t / remove at t+1) + Canonicalize + Apply + Recalculate (main), sink scatter, fold of 2
intrinsics (hp bounded by the sink stat through an int bound column, energy constant 0..100) with inbox,
changed words and 30/40 % condition bits per worker partition, **kinds = an sv stand-in, not tl** (per
timeline 5-W reads of the stat column and W writes: local delta `+=` or, with the cross-row share, an
`Inbox.Send` to a hashed row; W = 2.5 alternates 2 and 3), `EventDerive` + Swap, slot-keyed digest of both
intrinsics.

Settings [measured]: AMD Ryzen 5 8500G (6C/12T, AVX2), Windows 11, .NET 10.0.401 Release, default ISA
leg (Vector256 on), 6 workers (`Parallel.For`, one partition per worker), Iw = 2, 32 warm ticks, 6 rounds
x 32 ticks, round 0 dropped, median of round means, **1 process per cell** (not the plan's 5 ABAB),
bench.lock held, no Burst (not applicable on .NET). alloc B/tick is the harness's `Parallel.For`
(~5.2 KB), not sv.

```
shape  W    cross  total ms | main  scatter  fold  kinds  derive  digest | statops/tick  edges/tick  alloc B/tick
P1    1.0    0%     5.553 | 0.218   0.060  1.869  2.224  0.007  1.238 |      350          0       5213
P1    1.0   10%     5.854 | 0.084   0.029  1.488  2.870  0.006  1.379 |      350          0       5302
P1    1.0   50%     6.965 | 0.089   0.031  1.521  3.912  0.007  1.403 |      350          0       5287
P1    2.5    0%     5.762 | 0.184   0.040  1.489  2.572  0.006  1.448 |      874          0       5292
P1    2.5   10%     8.113 | 0.190   0.041  1.522  4.985  0.007  1.372 |      874          0       5305
P1    2.5   50%    11.061 | 0.193   0.041  1.526  7.882  0.007  1.394 |      874          0       5341
P1    4.0    0%     5.982 | 0.291   0.046  1.521  2.745  0.006  1.342 |     1398          0       5351
P1    4.0   10%     9.041 | 0.294   0.048  1.547  5.773  0.007  1.365 |     1398          0       5337
P1    4.0   50%    12.635 | 0.304   0.048  1.547  9.392  0.007  1.351 |     1398          0       5348
P2    1.0    0%     2.549 | 0.072   0.011  0.284  1.844  0.002  0.346 |      350          0       5307
P2    1.0   10%     3.090 | 0.081   0.012  0.294  2.357  0.002  0.340 |      350          0       5294
P2    1.0   50%     4.218 | 0.087   0.013  0.291  3.474  0.003  0.338 |      350          0       5298
P2    2.5    0%     3.140 | 0.179   0.025  0.295  2.275  0.002  0.350 |      874          2       5288
P2    2.5   10%     4.413 | 0.189   0.026  0.288  3.554  0.002  0.359 |      874          1       5288
P2    2.5   50%     6.550 | 0.214   0.028  0.316  5.607  0.003  0.375 |      874          2       5282
P2    4.0    0%     3.562 | 0.287   0.041  0.298  2.584  0.002  0.347 |     1398          4       5272
P2    4.0   10%     5.724 | 0.316   0.042  0.306  4.692  0.003  0.362 |     1398          4       5279
P2    4.0   50%     8.740 | 0.265   0.043  0.288  7.747  0.002  0.363 |     1398          4       5286
P3    1.0    0%     2.688 | 0.064   0.013  0.029  2.557  0.001  0.025 |      500         34       5205
P3    1.0   10%     3.461 | 0.071   0.013  0.030  3.319  0.001  0.027 |      500         34       5187
P3    1.0   50%     4.996 | 0.068   0.013  0.030  4.858  0.001  0.026 |      500         34       5197
P3    2.5    0%     3.403 | 0.156   0.028  0.030  3.166  0.001  0.026 |     1250         52       5211
P3    2.5   10%     5.228 | 0.157   0.027  0.030  4.984  0.001  0.026 |     1250         52       5193
P3    2.5   50%     8.161 | 0.165   0.028  0.033  7.905  0.002  0.026 |     1250         51       5186
P3    4.0    0%     4.057 | 0.251   0.043  0.031  3.696  0.002  0.026 |     2000         69       5196
P3    4.0   10%     6.880 | 0.247   0.041  0.030  6.535  0.002  0.026 |     2000         69       5201
P3    4.0   50%    11.216 | 0.258   0.040  0.031 10.853  0.002  0.025 |     2000         69       5195
```

## Reading

- sv's own stages at P1 (main + scatter + fold + derive + digest): 3.3-3.4 ms at 6 workers; the stand-in
  kinds are the rest. P2 ~0.7 ms, P3 ~0.15 ms of sv work. 8.3 ms gate: sv alone passes every cell;
  sv + stand-in passes at cross-row <= 10 % except P1 W=4 10 % (9.0) and is over at 50 % (reported, not gated).
- Fold P1: 1.49-1.87 ms for 2 x 1M rows at 6 workers (the 4M-row one-thread `fold1m` was 1.3-1.7 ms
  for 4 columns): DRAM-bound, workers barely help.
- **Digest P1: 1.24-1.45 ms > the plan's 0.7 ms kill rule** -> digest every K ticks in release (owner
  default K = 8, already approved); every tick in debug.
- Cross-row cost lives in the kinds stage (random atomic adds): P1 W=4 2.7 -> 9.4 ms at 50 %.
- Edges/tick are near 0 (hp rarely crosses 30 %): derive here is the empty walk; see issue 0121 for loaded derive.
