# Task S1 (for a smaller agent): make Seal fast without changing a single output bit

Branch: create `v3/s1-seal` from `v3/int` (after K1 is merged, or from d68abde) in a new worktree under D:\GitHub\_work\sv-proto\. Only touch `src/Sv.Core/Seal.cs`
and the Seal phases in `src/Sv.Host/Frame.cs` (SealCount, SealScatter, SealOrder, SealCompact, Plan). Issue range 0380-0389. No comments in code, C# 9 in Sv.Core, warnings are errors.

Read first: `docs/BENCH-REPORT.md` (Seal rows), issue 0362 (receipts and split by pass), `src/Sv.Core/Seal.cs`, tests/Sv.Laws.Tests InputLaws and SealForwardLaws (they define the law: Seal output depends only on the multiset of records).

Problem: Seal costs 3.0 ms per 1M pre-bucketed records (gate 0.4) and 5.0 ms per 1M random records (gate 3.0). Measured split for B before the last fix: count 0.61, plan 0.99, scatter 0.74, order 0.57, compact 0.76 ms. The parts x chunks count matrix (4 MB at 64 parts) is cleared, filled and walked every frame whatever the record count. Pre-bucketed blocks still pay route + count + scatter copy + ascending check + merge copy + compact copy (four passes over 16 MB).

Do, one atom each, keep an atom only if the benchmark median improves:
1. Size the count matrix by the chunks a part actually touched (clear and walk only those).
2. Pre-bucketed fast path: when a writer's block is already ordered by (chunk, key) and canonical (the ascending check passes), seal it in place or by one copy; never run route, count, scatter and order on it.
3. Fuse the compact pass with the order pass where the output is the same bytes.
Never change the order, the merge rule or the overflow behaviour. Stop and write an issue if a change needs it.

Verify after every atom (all must hold): `dotnet test` of tests/Sv.Laws.Tests, Sv.Scenario.Tests, Sv.Diff.Tests, Sv.Host.Tests, Sv.Lego.Tests all pass (counts at least 516, 80, 16, 27, 119); the bench digests are byte-identical (A DAF3A1D9BD73BA10, B 6E7618BF0900D3F8, C D3C714D46555B7CE, D 7413F619472566C4); also run the Laws suite with `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`. Benchmark: `dotnet benchmarks/Sv.Bench/bin/Release/net10.0/Sv.Bench.dll <entry> --ms 1000` (entries in benchmarks/Sv.Bench/V3Bench.cs), Release, nothing else on the CPU, median of 5 runs. Report before and after per atom. Commit atoms, do not push. Final report under 200 words.

STATUS: mostly done by agent P1 (handoff section 16). Not the next task. Next task is BRIEF-small-2-stat-layers.md.
