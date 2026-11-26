# 0521 v4 reference: with no kinds declared, the implicit kind was -1

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
`ReferenceWorld.Kind` returned -1 for the implicit kind when a pack declared none, so the oracle dropped records the engine kept (wide pack seed 6). The engine was right.

## Fix
`Kind` returns 0 for a null name or when the pack has no kinds. Found by `DeepFuzzTests.WidePacksMatchTheReferenceAtTheEdges`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
