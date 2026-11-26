# 0528 sv and tl.unity in Entities: no ECS pairing, and the two step on different clocks

Status: closed (2026-11-26, the SubScene sample and v4 generated glue delivered in PR 130)

## Need
A game that uses sv and tl.unity together in Entities should be able to drop both packages in, bake, and have timeline ticks reach sv and sv events start timelines, with no managed code on the hot path. Today it can't:

1. The only tl leg in Unity is `packages/com.sv.unity.v4/Tests/Tl`. It runs tl's plain host with managed arrays inside an edit-mode test. Nothing bakes a `PlayableDirector`, reads tl.unity's `[TlState]` components or writes `DynamicBuffer<TimelineRequest>`.
2. `sv pack --glue tl` emits `TimelineRequests`, a managed class over `PlaybackTable`. tl.unity takes requests through `DynamicBuffer<TimelineRequest>` (`TlRequests`), so the glue can't be called from a Burst system.
3. `SvStepSystem` (v3) runs in `SimulationSystemGroup` with `OrderFirst`, and `ColumnsStepSystem` (v4, PR 114) with `OrderLast`. Both step once per rendered frame. tl.unity ticks in `TlSimulationGroup`, inside `FixedStepSimulationSystemGroup`. At 144 fps a 60 Hz timeline sends its outbox about every 2.4 sv frames; on a slow frame tl ticks twice before sv steps once. The plain-host test hides this because it steps both by hand.
4. Until this issue was filed, the Unity CI built the tl leg only when `TL_UNITY_ROOT` was set on the runner. Fixed with it: `eng/tl-plain.stamp` now records the tl.unity commit (`tl.unity=`) and `eng/unity-ci.sh` fetches it, so the plain-host test runs on every Unity CI run.

## Proposal
Glue stays in a host package, per the layer rule: sv and tl never reference each other.

1. `com.sv.tl.entities` (host glue, asmdef refs sv Unity + tl.unity, compiled only when both are present):
   - `SvTlOutboxSystem` (ISystem, Burst), in `TlSimulationGroup` after tl's tick: reads each generated `[TlState] XOut` component, turns non-zero cells into sends keyed by the entity's `SvSlot`, and zeroes the cell. v3 writes `Origin.Timeline` records as `Outbox.Send` does; v4 writes through PR 114's `ColumnsState` Sender.
   - `SvTlRequestSystem` (ISystem, Burst), right after the sv step: maps sv events to `TimelineRequest` entries on the entity's buffer. The generator emits the event to timeline table as a blob, not a managed class.
2. One clock: sv steps inside `FixedStepSimulationSystemGroup`, after `TlSimulationGroup`, when the tl glue is present (a define or an option on the sv step system). Then one fixed step is one tl tick plus one sv step, in that order, the same order `Outbox.Send` assumes in .NET.
3. A sample SubScene: one baked `PlayableDirector` with a poison track, one sv slot, the same NorthStar3 scenario the plain test runs.
4. Proof: the NorthStar3 digest through tl.unity in Entities equals the digest through the plain host and through hand-written sends, at 30, 60 and 144 fps render rates.
5. CI: the Entities sample and its digest test join the tl leg that the pinned tl.unity already runs.

## Done
- One clock, v3: `SvClock.FixedStep` (tag `SvFixedStep` on the handle entity) moves sv into `SvFixedStepGroup`, last in `FixedStepSimulationSystemGroup`: `SvOpenFrameSystem`, `SvSendGroup`, `SvFixedStepSystem`, `SvEventGroup`. `SvStepSystem` (render frame) stays the default and skips tagged handles, so exactly one path steps. `SvTimelineClockSystem` switches automatically when a `TimelineLibrary` exists. sv references no tl type.
- One clock, v4: `ColumnsFixedStep` on the config entity (authoring: `Fixed Step`) moves the frame to `ColumnsFixedStepSystem` in `ColumnsFixedStepGroup` after `ColumnsSendGroup`; `ColumnsStepSystem` keeps the untagged default.
- Glue as hand-written generic Burst systems, not per-pack code: `SvOutboxSystem<TCell : ISvOutboxCell>` (in `com.sv.unity.v4`, no tl reference) and `SvTimelineRequestSystem` (in `Sv.Unity.V4.Tl`, compiled only with `com.tl.unity` via `versionDefines` `SV_TL_UNITY`). The event to timeline table is a `SvTimelineRule` buffer on the handle entity, filled once on the main thread with `TlLibrary.Find`, so no tl.unity change was needed.
- `sv pack --glue tl --host unity` also emits `GlueEntities.g.cs`: the `ISvOutboxCell` partials, the generic system registrations and `TimelineRules.AddTo`. Byte-equality tests in `tests/Sv.NorthStar3/UnityLegTests.cs`.
- v4, cards first: `ColumnsState.Sets` (lane edits applied between frames), `ColumnsCardSystem<TCell : IColumnsCardCell>` (a cell sets a card lane; the card's timer channel ends it) and `ColumnsOutboxSystem<TCell : IColumnsOutboxCell>` (`SendFrom`, as v3's `Outbox.Send`).
- Proof: `Sv.Unity.V4.TlEntitiesTests` replays the five NorthStar3 journals through tl.unity's Entities systems and sv's fixed-step systems at 30, 60 and 144 rendered fps (1 and 11 workers): every per-frame digest equals the journal (pins `A566FAEC8837BA57`, `2D6A06776AE25DCB`), tl's cells equal the journal's timeline sends, and the glue reports Burst. `ColumnsClockTests` proves the v4 cards and outbox against a hand-stepped `ColumnWorld` at the same three rates.

- SubScene sample (proposal 3): `SvWorldAuthoring` and `SvSlotAuthoring` (assembly `Sv.Unity.V4.Authoring`) bake the world config, the fixed step clock tag and the slot; `SvEntities.Publish(manager, world, config)` puts the world on the baked entity. `TlPoisonScene` (menu `sv/Samples/Build Tl Poison Scene`) writes the poison timeline asset and a scene whose SubScene holds the world and a `PlayableDirector` on the status slot. `TlSceneTests` bakes the same objects with tl.unity's `TlDirectorBaker`, registers the timeline through `TlLibrarySystem` and `TlTimelineRegisterSystem`, and plays the two NorthStar3 poison journals at 30, 60 and 144 rendered fps: every frame digest equals the .NET host's. CI runs the bake of the same objects the menu writes, not the menu itself (creating and importing a SubScene inside an EditMode test run is left to the editor).
- v4 generated glue and the v4 tl request system: issue 0525 (closed), with a baked v4 proof in `ColumnsTlSceneTests`.

## Earlier blockers, now cleared
- 0525's default: the owner chose cards first, with the outbox kept for ported v3 code.
- PR 114 (Burst host, `ColumnsState` Sender) merged.
- No tl.unity change was needed: timeline names resolve once on the main thread into a `SvTimelineRule` buffer, which the Burst system reads.
