# 0524 v4: an object with many keys took minutes to parse

Status: closed (2026-11-26, fixed in the same change that found it)

Found by: probing `PackJson.Read` with hostile input after the v4 hardening pass.

## What was wrong
The duplicate-key check compared each key with every earlier key of its object. An object with 200,000 keys took 2 minutes 27 seconds to reject, so a large or hostile pack file could hang whatever reads it.

## Fix
Keys are checked with a hash set per object, linear in the number of keys. Test: `PackJsonTests.AnObjectWithManyKeysParsesInLinearTime` (200,000 keys and one duplicate, under 10 s; it failed at 2 m 27 s before the fix).

## Receipt
`dotnet test tests/Sv.Columns.Tests -c Release` green on default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`; canon digest still 77EC257ECB72B2CF.
