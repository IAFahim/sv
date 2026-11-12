# Issue 0117 - V2 receipt: `fold1m` bench and its kill rule (TRIPPED, decision needed)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Plan `_work/plan-sv.md` step V2: `fold1m` = 1M rows x 4 intrinsics, one thread,
<= 1.5 ms [target]; kill: fold not 10 % faster than `Kernel.Run` plus a masked clear.
Labels: `ai`, `area:bench`.

## Change

`benchmarks/Sv.Bench/Fold1mBench.cs` (+1 line in `Program.cs`): 2^20 rows x 4 int columns,
~25 % zero deltas, deltas refilled untimed before each rep; arms per bound shape
(constant [0, 150..300]; stat ceiling via `Bounds.Stats(board, null, max_*)`):
(1) `Kernel.Fold` with changed words, (2) `Kernel.Run` + `Span.Clear` of the delta,
(3) `Kernel.Run` with applied column + clear (the info the fold's changed bits replace).
First rep dropped, median of 40. Verifies fold == run bit for bit; 0 B over timed reps.

## Receipts [measured]

AMD Ryzen 5 8500G (6C/12T, AVX2), Windows 11, .NET 10.0.401 Release, 1 thread, no
cross-row (no inbox), bench.lock held, 3 processes:

| arm | constant ms | stat-ceiling ms |
|---|---:|---:|
| fold (+changed bits) | 1.28 / 1.29 / 1.50 | 1.70 / 1.74 / 1.67 |
| run + clear | 1.27 / 1.34 / 1.18 | 1.78 / 1.69 / 1.60 |
| run + applied + clear | 2.10 / 2.10 / 2.01 | 2.26 / 2.38 / 2.37 |

`DOTNET_EnableAVX2=0` (128-bit leg): fold 2.18 / 2.43, run+clear 1.85 / 1.81 ms.
An earlier non-generic draft (bounds read through an `in` struct, reloaded per group by
aliasing) ran 2.1-5.0 ms; struct-generic bound strategies fixed it (receipt kept here as a
dead end: never pass bound pointers by `in` reference into a vector loop that stores).

## Verdict

- Target <= 1.5 ms: constant pass (2 of 3), stat ceiling miss (1.67-1.74 ms). Single
  thread on a 6-core desktop; the 1M x 4 working set (48-64 MB) is DRAM-bound.
- Kill rule as written: **TRIPPED** - fold is 0.79-1.05x of run+clear (equal within
  noise), though 1.33-1.63x faster than run+applied+clear. The fold also folds the inbox
  and emits changed bits in the same pass; run+clear does neither.
- Plan says: ship the fold as those two calls. That would need an applied pass and an
  inbox pre-pass to keep the fold's outputs, i.e. slower. **Owner/manager decides**: keep
  `Kernel.Fold` (recommended: same speed, more outputs) or reimplement it as Run + clear.
