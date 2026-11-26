# 0518 v4: calls after `Dispose` read freed memory and crashed the process

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
Any call on a disposed `ColumnWorld`, or on a world whose `CompiledPack` was disposed, dereferenced freed native memory. The test host died with an access violation.

## Fix
Every public call checks that the world and its pack are alive and throws `ObjectDisposedException`; `Dispose` nulls every pointer and is safe twice; writers throw too. Tests: `EdgeTests.AWorldRefusesEveryCallAfterDispose`, `AWorldRefusesToStepOnceItsPackIsDisposed`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
