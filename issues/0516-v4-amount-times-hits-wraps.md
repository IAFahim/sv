# 0516 v4: `amount * hits` wrapped in int64 before saturating

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
Large `amount` times `hits` overflowed int64 and could saturate to the wrong sign, in the engine and the reference alike, so the two agreed on a wrong answer.

## Fix
`RecordWriter.AmountTimesHits` clamps the amount to plus or minus (2^27 - 1) first; the product of that and an `int` cannot overflow and saturates exactly. The reference saturates the exact product with `BigInteger`. Test: `EdgeTests.AmountTimesHitsSaturatesInsteadOfWrapping` (4 cases).

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
