# 0400 M9 gate, Unity leg: tl running in Unity feeding the sv outbox

Status: closed (45f0e5c, branch worktree-agent-a206d5f18338d4190 on v3/m8-unity 29c530f, 2026-11-13)

## What is proven (v3/m8-unity, 2026-11-13)

- .NET: generated glue (`sv pack --glue tl`), `Sv.Host.Outbox.Send`, `TimelineRequests`, NorthStar3 17 tests (status as entity, sniper_ward drops sniper ticks, requests start and stop, workers 1/2/4/11, replay from causes, pinned digests `A566FAEC8837BA57` and `2D6A06776AE25DCB`, equal under DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0).
- Unity: the sv half. `tests/Sv.NorthStar3` records every call the glue makes on the host (including the `Origin.Timeline` sends) as twin journals in `packages/com.sv.unity.v4/Tests/Editor/Twin/northstar3`; the Unity EditMode tests replay them through the generated Burst jobs at 1 and 11 workers and every per-frame digest equals the .NET host (91 of 91).
- Unity, tl half (45f0e5c): tl itself runs in Unity 7000.0.0a16 batch mode on the plain host. `Sv.Unity.V4.TlTests` starts the glue's `poison` timeline (`PoisonTrack`, `PoisonClip` amount 3, 4 ticks) on the status entity, or lets `TimelineRequests` start it from LowHealth. `TlAuto_Sv_NorthStar3` writes the `[TlState] PoisonOut` column, and `Outbox.Send` (Collect) turns the cells into `Origin.Timeline` records in the `SvWorld`. Then the generated Burst jobs step. The test replays the five NorthStar3 journals at 1 and 11 job workers (tl on `TlWorkers(11)` in the 11 case). Each frame, the cells tl wrote equal the timeline sends the .NET host collected (source, channel, amount), Collect accepts every one, every per-frame digest and the final digest equal the journal, including `A566FAEC8837BA57` and `2D6A06776AE25DCB`, and no phase ran as managed code.

## Decision (owner, rule 15)

Plain host path shipped; the ECS/SubScene path (Timeline assets, baked Playable Director, Tl tracks for the glue clips) stays a follow-up and is not built.

## How it is wired (docs/UNITY.md, "tl in Unity")

- `com.tl.unity` is a normal manifest dependency, not under `testables`, so its own test assemblies (kind registered twice) never compile. The plain path uses only its `Tl.Core` assembly. Its sources are byte-identical to tl `src/Tl.Core` at TL_ROOT 3ba5575 (`diff -r`, no file differs).
- `packages/com.sv.unity.v4/Tests/Tl/Generator`: `Tl.Gen.Plain.dll` copied from tl.unity `com.tl.unity/Tests/Plain/Generator` (built from tl-kinds a15ea149, blob 1c5834fa, sha256 34f7c312) by `eng/tl-plain-sync.sh sync`. `check` compares it with `eng/tl-plain.stamp`. Label `RoslynAnalyzer` and no platforms were set through Unity (`PluginImporter` + `AssetDatabase.SetLabels` from a scratch `-executeMethod`); the committed `.meta` is Unity's output.
- `Tests/Tl/Glue` is assembly `Sv.NorthStar3`: `Glue.g.cs` (emitter output), `NorthStar3Timelines` (plain host) and `game.bytes`. Every tl assembly carries `versionDefines` com.tl.unity -> `SV_TL_UNITY`, so a project without tl skips them.
- `Tests/Tl/Editor` is assembly `Sv.Unity.V4.TlTests`, holding `TlTimelineTwinTests`.

## Findings (no tl change needed; kill rule M9 holds)

- `Tl.Gen.Plain` emits nothing for an assembly without a `[TimelineSystem]` struct, and the zero-config glue declares none. One empty `[TimelineSystem] public ref partial struct` in the host makes it emit `Tl.Generated.TlAuto_<Assembly>`. Probe (.NET, netstandard2.1, the tl.unity dll as analyzer): no trigger gives 0 generated files; with the trigger, the generated `TlAuto` system (both its `.Auto.g.cs` declaration and its `Run`/kind code) is byte-identical to tl main's .NET `Tl.Gen.CSharp` output for the same assembly. The only extra is the trigger's empty system. Worth a tl.unity issue (zero-config assemblies in the plain generator) after talking with the owner; it is not filed.
- The baked library records the clip type's assembly name in its TLB metadata, so `tlb library --assembly X.dll` gives different bytes for a differently named assembly with the same types: Sv.Unity.V4.TlGlue vs Sv.NorthStar3 differed in 150 bytes. This corrects skill note "against any dll with the same types gives the same bytes": the pair keys are equal, the bytes are not. The Unity glue assembly therefore has the .NET name `Sv.NorthStar3`.

## Receipts

- `tlb library Tests/Tl/Glue/Timelines~ --assembly <tl-harness>/Library/ScriptAssemblies/Sv.NorthStar3.dll --no-sources` gives 1160 bytes, digest b8b8b0957bd9b0d1, `cmp`-equal to the committed `game.bytes`, to `Rig.Library()` and to the netstandard probe bake.
- `TL_ROOT=/home/os/GitHub/tl dotnet test tests/Sv.NorthStar3 -c Release`: 21 of 21. That is 18 existing plus 3 in `UnityLegTests`: Unity glue == emitter, `game.bytes` == `Rig.Library()`, fixture bake == `Rig.Library()`.
- Unity 7000.0.0a16, scratch project with com.tl.unity (`T=tl4full`, `eng/m8-run/gate.sh`, R = this branch): compile 0 error CS, 0 warning CS, 0 error BC; EditMode 101 of 101 (Sv.Core.PinTests 4, Sv.Unity.V4.Tests 87, Sv.Unity.V4.TlTests 10), NUnit clr-version 10.0.10. A first targeted run (`T=tl1`, FILTER Sv.Unity.TlTests) also gave 10 of 10.
- Same project without com.tl.unity (`T=tl3notl`): 0 error CS, 0 warning CS, 0 error BC; EditMode 91 of 91; the tl assemblies were not built.
- Negative control (`T=tl2neg`): the same tests against a library baked with clip amount 4 failed 10 of 10 with "frame 1: tl in Unity wrote other outbox cells than the .NET host collected".

## Not verified

- A fresh `git clone` plus a fresh project (this ran on a scratch copy of m8-harness with the worktree packages).
- PlayMode, and a player build (IL2CPP/CoreCLR). tl's plain host runs as managed C# on the Editor's CoreCLR, not Burst; only the sv phases are Burst jobs.
- The ECS/SubScene path.
