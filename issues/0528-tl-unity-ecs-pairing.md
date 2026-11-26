# 0528 sv and tl.unity in Entities: no ECS pairing, and the two step on different clocks

Status: open

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

## Not done yet because
- v4's half waits on 0525: cards first or outbox first as the default changes what the glue emits.
- It builds on PR 114 (Burst host, `ColumnsState` Sender) and PR 115 (tl pin d87d1998), both still open.
- It needs tl.unity changes too (a request blob reader the glue can call from Burst), which belong in tl.unity.
