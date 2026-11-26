# 0517 v4: convert `gain * numerator` could leave int64, and the kernels then disagreed

Status: closed (2026-11-26, fixed in the v4 hardening change)

Found by: the v4 hardening pass (`tests/Sv.Columns.Tests/EdgeTests.cs`, `DeepFuzzTests.cs`).

## What was wrong
With scale 16 and 65,536 records of the largest amount, a channel's gain reaches about 2^47; times a numerator near 2^16 it passes 2^63. `Law` shifted arithmetically and `Kernels` logically, so the scalar and Vector256 paths gave different lanes.

## Fix
`Law.Fraction` splits the gain into a multiple of the denominator and a remainder: `q * numerator + ((r * numerator) >> s)`. It equals `floor(gain * numerator / denominator)` exactly and never overflows, because gains are never negative and `numerator <= denominator`. `Kernels` uses the same split; the reference computes the exact product with `BigInteger`. Normal loads give bit-identical results (canon digest unchanged). Tests: `EdgeTests.AConvertOfAHugeSumMovesTheExactFraction`, `AConvertOfAHugeSumAgreesOnEveryKernel`.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` 62 of 62 green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF; `eng/columns-fuzz.sh 1000 2500 all` clean on all three legs.
