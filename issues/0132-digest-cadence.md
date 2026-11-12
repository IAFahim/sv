# Issue 0132 - DigestCadence: every tick in debug builds, every 8 in release, core API

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Core agent, branch `proto2/core`. Owner default (phase 1): digest every tick in debug, every
K = 8 ticks in release. Labels: `ai`, `area:core`.

## Change

New `src/Sv/DigestCadence.cs` (struct, C# 9, no allocation):
`DigestCadence(int every)` (>= 1, else throws), `EveryTick`, `Release` (`ReleaseEvery = 8`),
`IsDebugBuild` (true when the sv core assembly is compiled with `DEBUG`: Unity Editor and
development players, .NET Debug), `Default` = `EveryTick` in debug else `Release`,
`Due(tick)` (tick % every == 0), `LastDue(tick)` (the tick whose digest a host holds at
`tick`, floor semantics for negative ticks). `default(DigestCadence)` behaves as every tick.
Host wiring (`SvHost.DigestEvery` -> `DigestCadence`) is the Unity agent's; the .NET host
pattern is `if (cadence.Due(tick)) digest = ...`.

## Tests (new): `tests/Sv.Tests/DigestCadenceTests.cs` (3)

Default per build (Release run: 8; Debug run `dotnet test -c Debug`: 1, both green), Due/LastDue
sequences incl. negative ticks, sparse digests at cadence 4 equal the every-tick digests on
their ticks.

## Receipts (0131 + 0132)

`dotnet test sv.slnx -c Release`: Sv.Tests 506/506, Sv.Portable.Tests 390/390, Sv.NorthStar 27/27, Sv.NorthStar2 13/13, Sv.Unity.Tests 1/1. FMA grep clean, no production comments.
