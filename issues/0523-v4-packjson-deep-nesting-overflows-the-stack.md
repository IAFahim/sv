# 0523 v4: deeply nested JSON crashed the process with a stack overflow

Status: closed (2026-11-26, fixed in the same change that found it)

Found by: probing `PackJson.Read` with hostile input after the v4 hardening pass.

## What was wrong
`PackJson.Read` parsed arrays and objects by recursion with no depth limit. 100,000 nested `[` overflowed the stack, which .NET cannot catch: the process died (the test host reported "Test host process crashed : Stack overflow"). A pack file is designer or web input, so one bad file could kill an editor, a server or a game. The v3 reader (`Sv.Rules/Pack/Json.cs`) already refuses nesting past 64.

## Fix
`PackJson` refuses nesting deeper than 64 with a `PackException` naming the line and column, the same limit as v3. Real packs nest about 5 deep. Test: `PackJsonTests.DeepNestingIsRefusedInsteadOfOverflowingTheStack`, which crashed the test host before the fix.

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` green on default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF.
