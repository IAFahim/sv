# 0400 M9 gate, Unity leg: tl running in Unity feeding the sv outbox

Status: open

## What is proven (v3/m8-unity, 2026-11-13)

- .NET: generated glue (`sv pack --glue tl`), `Sv.Host.Outbox.Send`, `TimelineRequests`, NorthStar3 17 tests (status as entity, sniper_ward drops sniper ticks, requests start and stop, workers 1/2/4/11, replay from causes, pinned digests `A566FAEC8837BA57` and `2D6A06776AE25DCB`, equal under DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0).
- Unity: the sv half. `tests/Sv.NorthStar3` records every call the glue makes on the host (including the `Origin.Timeline` sends) as twin journals in `packages/com.sv.unity.v4/Tests/Editor/Twin/northstar3`; the Unity EditMode tests replay them through the generated Burst jobs at 1 and 11 workers and every per-frame digest equals the .NET host (91 of 91).

## What is not proven

tl itself running inside Unity and writing the outbox columns (`[TlState] PoisonOut` as an ECS component or a plain `TlAuto` column), then Collect into the SvWorld, with the same digests as the .NET NorthStar3.

## Why it is open and what it needs (found, not guessed)

- The plain path in tl.unity (`com.tl.unity/Tests/Plain`) needs `Tl.Gen.Plain.dll` imported as a Roslyn analyzer (label `RoslynAnalyzer` on the plugin, set through Unity, not by hand) in an asmdef folder referenced by the game assembly, plus a baked `game.bytes` library (`tlb library` over the fixture against the game assembly) and a host class like `PlainHost`. Its asmdefs are gated by `UNITY_INCLUDE_TESTS`, so the package must be under `testables`, which also runs tl.unity's own test assemblies (known: a kind registered twice).
- The ECS path needs authored Timeline assets, a SubScene bake with a Playable Director and the Tl tracks for the glue clips (`PoisonClip` mirrors), made by Unity itself.
- Kill rule M9: it must not need a tl change beyond generated glue. The glue shapes already compile against tl main (.NET); nothing found so far needs a tl change.

## Done when

A Unity EditMode or PlayMode test with a tl timeline (the glue's `PoisonTrack`) on a status entity feeds `PoisonOut`, a Collect step turns it into `Origin.Timeline` records, and the per-frame digests equal the committed NorthStar3 journals at 1 and 11 workers. Needs a decision on which path (plain host or ECS) the owner wants shipped; the plain host is the smaller one.
