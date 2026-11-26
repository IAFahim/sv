# 0513 v4: `Send(target, source, channel, amount)` silently called `Send(target, channel, amount, hits)`

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
`RecordWriter` had `Send(int target, int channel, long amount, int hits = 1)` and `Send(int target, int source, int channel, long amount, int hits = 1)`. A call with four `int`s binds to the first: the source becomes the channel, the channel the amount, the amount the hit count. No error, no warning.

Five tests (`InvarianceTests` three times, `VectorKernelTests` twice) and one in `RunnerTests` and `PackJsonTests` each meant to send from a source and instead sent kind 0 records on other channels, so the sourced path (kind gates, source scale flags) was not what they checked. The benchmark's sourced quarter of records was the same.

## Fix
The sourced call is now `SendFrom(int source, int target, int channel, long amount, int hits = 1)`, and `ReferenceWorld.SendFrom` matches it. Every call site was fixed; `Canon.cs` already passed five arguments, so the pinned digest is unchanged. Spec section 2 says why the names differ.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
