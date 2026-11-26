# 0515 v4: a channel outside 0 .. 15 aliased a declared channel

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
The record keeps 4 bits of channel, so channel 17 became channel 1 and a negative channel became 15.

## Fix
Refused at send and counted in `Dropped` (`RecordWriter.ChannelLimit`). The reference drops an unknown channel name the same way. Test: `EdgeTests.AChannelOutsideTheRecordBitsIsDroppedAndDoesNotAliasAnother`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
