# sv in Unity 7

Unity 7000.0.0a16 (newest editor only), Entities 7.0.0, Collections 7.0.0, Burst 2.0.0. sv does not reference tl or Unity; the Unity adapter and the tl glue live in the host.

## Packages

| Package | What it is |
|---|---|
| `com.sv.engine` | `Sv.Core`, `Sv.Host` (runtime) and `Sv.Rules` (editor only) copied verbatim from `src` by `eng/engine-sync.sh sync` (`check` fails on drift) |
| `com.sv.unity.v4` | the adapter: generated Burst jobs per frame phase, `SvWorld`, `SvStepSystem`, `SvSlot`, reload hook, pack authoring window |

## Use

1. Add `com.sv.engine` and `com.sv.unity.v4` to `Packages/manifest.json` as `"file:<sv checkout>/packages/<name>"`.
2. Write the pack (`values`, `rules`, `cards`, `templates` `.sv.json`) with the authoring window (Window > sv > Pack Authoring) or by hand and generate with `sv pack <folder> --host unity --namespace <ns> --out <dir>` or the window's Regenerate. The output is `Values.g.cs`, `Kernels.g.cs`, `HostKernels.g.cs`, `PackData.g.cs`, `PackJobs.g.cs`, `Rules.map.txt`. Put it in a game assembly that references `Sv.Engine.Core`, `Sv.Engine.Host`, `Sv.Unity.V4`, `Unity.Burst`, `Unity.Collections`.
3. `PackWorld.Create(new HostOptions { Workers = ..., Parts = 64 })` returns an `SvWorld<HostKernels>`. `Spawn(template)`, `Send(...)`, `Modify(StatOp)`, `Step()`, `Read(slot, lane)`, `Digest()`.
4. In ECS: `SvEntities.Publish(entityManager, world)` creates the world handle entity, `SvEntities.Attach(entityManager, entity, world, template)` gives an entity a slot, and `SvStepSystem` (in `SimulationSystemGroup`, first) kills the slots of destroyed entities and steps the world. `SvWorlds.DisposeAll()` runs before every assembly reload.

## Proof

`COMPILE_ONLY=1 T=c1 bash m8-run/locked.sh m8-run/gate.sh` then the same without `COMPILE_ONLY` (scripts in `eng/m8-run`, set-up in `docs/M8-HANDOFF.md`). Expect 0 `error CS`, 0 `warning CS`, 0 `error BC` and every EditMode test green. Every generated job carries a probe: 1 means it ran as a Burst job.

## Player builds (Burst AOT; Linux CoreCLR proven, Windows and IL2CPP selectable)

The target follows the OS (`linux64`, or `win64` under Git Bash on Windows; override with `SMOKE_TARGET`) and the backend is `SMOKE_BACKEND=coreclr` (default) or `il2cpp`; the build fails if the backend did not take. The run line also prints `il2cpp=yes|no` (a `GameAssembly` file next to the player). On Windows set `U` to `Unity.exe`, for example `U="/c/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe" SMOKE_BACKEND=il2cpp bash eng/player-smoke.sh`.

`bash eng/player-smoke.sh` builds a Linux player from a throwaway project (the three test packs as a runtime assembly, the 37 twin journals under StreamingAssets), runs it headless and replays every journal at 1 and 11 job workers: every per-frame digest must equal the .NET host's and every phase must run as Burst (`SvProbe.ManagedRuns` 0). Set `KEEP=1` to keep the project, `U` for another editor. Result on 2026-11-14 (Unity 7000.0.0a16, `LinuxStandaloneSupport` CoreCLR player): build Succeeded, 0 error CS, 0 error BC, `lib_burst_generated.so` present; `SVSMOKE summary cases 74 frames 324 failures 0 maxJobWorkers 11 managedRuns 0`. This editor ships CoreCLR players only (no Mono, no IL2CPP variation installed), so those backends are not covered.

## tl in Unity (plain host, M9 Unity leg)

tl runs inside Unity on its plain host: `TimelineWorld`, `PlaybackTable`, `KindStore` and the generated system over managed column arrays, on the main thread or `TlWorkers`. No SubScene, Timeline asset or ECS system is involved. The worked example is `packages/com.sv.unity.v4/Tests/Tl` (NorthStar3).

1. Import `com.tl.unity` (`"com.tl.unity": "file:<tl.unity checkout>/com.tl.unity"` or its git URL). The plain path uses only its `Tl.Core` assembly, whose sources equal tl's `src/Tl.Core`. Do not list `com.tl.unity` under `testables`: its own test assemblies would register kinds twice. The sv tl leg (`Sv.Unity.V4.TlGenerator`, `Sv.NorthStar3`, `Sv.Unity.V4.TlTests`) compiles only when `com.tl.unity` is in the project (`versionDefines` set `SV_TL_UNITY`) and tests are included, so a project without tl still compiles the rest of `com.sv.unity.v4`.
2. The generator is `Tl.Gen.Plain.dll`, copied from tl.unity's `com.tl.unity/Tests/Plain/Generator` by `eng/tl-plain-sync.sh sync` (`check` compares it with `eng/tl-plain.stamp` and with the tl.unity copy). `sync` also writes the tl.unity commit it copied from (`tl.unity=` in the stamp), and refuses a checkout with uncommitted changes under `com.tl.unity`. `eng/unity-ci.sh` fetches that commit into `~/.cache/sv-tl-unity` and adds `com.tl.unity` to the probe, so the tl leg runs on every Unity CI run; `TL_UNITY_ROOT` points it at another checkout instead. It sits in a folder with its own asmdef (`Tests/Tl/Generator`), and the game assembly references that asmdef, so the analyzer runs on the game assembly only. Its plugin settings are label `RoslynAnalyzer` and no platforms. Set them through Unity, never in the `.meta` by hand: an `-executeMethod` in the scratch project that calls `PluginImporter.SetCompatibleWithAnyPlatform(false)`, `SetCompatibleWithEditor(false)`, `SaveAndReimport()` and `AssetDatabase.SetLabels(dll, new[] { "RoslynAnalyzer" })`. The committed `.meta` carries the result. Unity cannot run that method while a script fails to compile, so do it before adding code that uses the generated system.
3. `Tl.Gen.Plain` runs only on an assembly that declares at least one `[TimelineSystem]` struct. The glue from `sv pack --glue tl` is zero-config and declares none, so the game assembly adds one empty `[TimelineSystem] public ref partial struct`. The generator then emits `Tl.Generated.TlAuto_<AssemblyName>`, the same code tl's .NET generator emits. Since tl `d87d1998` there is no module to install: the host builds the generated struct and calls `Run`.
4. Bake the library with tl's `tlb library` over the timeline JSON, against the compiled game assembly: `dotnet Tl.Bake.dll library Tests/Tl/Glue/Timelines~ --assembly <project>/Library/ScriptAssemblies/Sv.NorthStar3.dll --out Tests/Tl/Glue/game.bytes --no-sources`. The library metadata records the clip type's assembly name, so the Unity glue assembly has the same name as the .NET one (`Sv.NorthStar3`). Then the bytes equal the library the .NET host bakes in code. `tests/Sv.NorthStar3/UnityLegTests.cs` checks three things: the Unity glue equals the emitter output, `game.bytes` equals `Rig.Library()`, and the fixture bakes to the same bytes. `SV_UPDATE_TWIN=1` rewrites the glue and `game.bytes`.
5. Each frame: `NorthStar3Timelines.TickAndFillOutboxes()` (tl `Tick`, then `TlAuto_Sv_NorthStar3.Run` adds the clip amounts to the `[TlState]` outbox columns), then `Outbox.Send(world.Loop, cells, channel, kindLane, targetLane)` per timeline channel (Collect: the cells become `Origin.Timeline` records), then `SvWorld.Step()` (the generated Burst jobs), then `TimelineRequests.Apply` with the frame's events.

Proof: `Sv.Unity.V4.TlTests` replays the five NorthStar3 journals at 1 and 11 job workers. tl writes the outbox, and Collect feeds the SvWorld. Each frame, the cells tl wrote equal the timeline sends the .NET host collected, and every per-frame digest equals the journal's, including the pins `A566FAEC8837BA57` and `2D6A06776AE25DCB`. The ECS/SubScene path (Timeline assets, a baked Playable Director, Tl tracks for the glue clips) is not built; it stays a follow-up.

tl glue: `sv pack <folder> --glue tl` and `docs/V3-HANDOFF.md` (M9). The old adapter v3 (`com.sv.unity`: `SvHost`, `SvLinkRegistry`, `SvJobs`) was deleted in M11; its history is in git and its issues are `issues/0127`, `0140`, `0142`.
