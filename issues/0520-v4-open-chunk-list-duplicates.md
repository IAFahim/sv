# 0520 v4: a reclaimed chunk could take spawns of its old template, and churn grew memory

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
The list of chunks with room for a template could hold the same chunk twice. Once that chunk emptied and was handed to another template, the stale entry put new spawns of the old template into it: entities of the wrong template, found by the deep fuzz as lane differences (arena frame 18, random pack seed 157, wide pack seed 36). Under spawn and despawn churn the list also grew without bound: 1,040,552 B over 100,000 cycles.

## Fix
Each chunk is listed at most once, tracked by a flag per chunk; reclaim removes it from its old template's list. Tests: `EdgeTests.AFullWorldReusesAChunkOnlyOnceItIsEmpty`, `AReclaimedChunkNeverTakesASpawnOfItsOldTemplate`, `SpawnAndDespawnChurnOnAFullChunkAllocatesNothing` (0 B over 100,000 cycles). All three failed before the fix.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
