# sv in Unity 7

Unity 7000.0.0a16 (newest editor only), Entities 7.0.0, Collections 7.0.0, Burst 2.0.0. sv does not reference tl or Unity; the Unity adapter and the tl glue live in the host.

## Packages

| Package | What it is |
|---|---|
| `com.sv.engine` | `Sv.Core`, `Sv.Host` (runtime) and `Sv.Rules` (editor only) copied verbatim from `src` by `eng/engine-sync.sh sync` (`check` fails on drift) |
| `com.sv.unity.v4` | the adapter: generated Burst jobs per frame phase, `SvWorld`, `SvStepSystem`, `SvSlot`, reload hook, pack authoring window |
| `com.sv.core` | the legacy v2 core (`src/Sv`), synced by `eng/unity-sync.sh`; kept for the frozen legacy pins |
| `com.sv.core.tests` | the Unity legs of the legacy pins: fold scenario (plain and as a Burst job) = `0D945D8FD131DA2A`, NorthStar2 link table hash `A47434070C42E4AC`, twin scenario `DA47AD9B5CD74335` |

## Use

1. Add `com.sv.engine` and `com.sv.unity.v4` to `Packages/manifest.json` as `"file:<sv checkout>/packages/<name>"`.
2. Write the pack (`values`, `rules`, `cards`, `templates` `.sv.json`) with the authoring window (Window > sv > Pack Authoring) or by hand and generate with `sv pack <folder> --host unity --namespace <ns> --out <dir>` or the window's Regenerate. The output is `Values.g.cs`, `Kernels.g.cs`, `HostKernels.g.cs`, `PackData.g.cs`, `PackJobs.g.cs`, `Rules.map.txt`. Put it in a game assembly that references `Sv.Engine.Core`, `Sv.Engine.Host`, `Sv.Unity.V4`, `Unity.Burst`, `Unity.Collections`.
3. `PackWorld.Create(new HostOptions { Workers = ..., Parts = 64 })` returns an `SvWorld<HostKernels>`. `Spawn(template)`, `Send(...)`, `Modify(StatOp)`, `Step()`, `Read(slot, lane)`, `Digest()`.
4. In ECS: `SvEntities.Publish(entityManager, world)` creates the world handle entity, `SvEntities.Attach(entityManager, entity, world, template)` gives an entity a slot, and `SvStepSystem` (in `SimulationSystemGroup`, first) kills the slots of destroyed entities and steps the world. `SvWorlds.DisposeAll()` runs before every assembly reload.

## Proof

`COMPILE_ONLY=1 T=c1 bash m8-run/locked.sh m8-run/gate.sh` then the same without `COMPILE_ONLY` (scripts in `eng/m8-run`, set-up in `docs/M8-HANDOFF.md`). Expect 0 `error CS`, 0 `warning CS`, 0 `error BC` and every EditMode test green. Every generated job carries a probe: 1 means it ran as a Burst job.

tl glue: `sv pack <folder> --glue tl` and `docs/V3-HANDOFF.md` (M9). The old adapter v3 (`com.sv.unity`: `SvHost`, `SvLinkRegistry`, `SvJobs`) was deleted in M11; its history is in git and its issues are `issues/0127`, `0140`, `0142`.
