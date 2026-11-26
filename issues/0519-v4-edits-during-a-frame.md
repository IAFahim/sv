# 0519 v4: spawn, despawn and set were allowed while a frame ran

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
Between `BeginFrame` and `EndFrame` the world reads lanes and gates from many workers. A spawn, despawn or set from game code at that moment raced with them.

## Fix
Those three calls throw `InvalidOperationException` while a frame runs. It is a guard against the common mistake, not a lock: under Unity jobs a call from another thread at the exact moment can still slip past. Test: `EdgeTests.NothingChangesTheWorldBetweenBeginFrameAndEndFrame`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
