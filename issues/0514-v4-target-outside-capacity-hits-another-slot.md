# 0514 v4: a target outside the world wrapped onto a live slot

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
`Record.Pack` keeps 22 bits of target. A negative target or one at 2^22 or more landed on another slot; a target between `Capacity` and 2^22 read past the gates.

## Fix
`Send` and `SendFrom` refuse a target outside `0 .. Capacity - 1` and count it; the count joins `ColumnWorld.Dropped` at the next step. Tests: `EdgeTests.ATargetPastTheSlotBitsIsDroppedAndHitsNobody`, `ATargetPastTheCapacityIsDroppedEvenWhenItFitsTheSlotBits`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
