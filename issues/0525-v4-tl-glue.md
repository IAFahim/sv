# 0525 v4 and tl: generated glue for column packs

Status: closed (2026-11-26, delivered in PR 130)

## Need
v3 has `sv pack <folder> --glue tl`: an outbox column per timeline channel, clips, tracks and `TimelineRequests`, so a ticking status is a tl timeline whose ticks reach sv as records (README chapter 7). v4 column packs have no equivalent. Today a game connects them by hand (`docs/v4-guide.md` section 6): a timeline sets a card lane, or game code turns an outbox column into `SendFrom` calls.

## Proposal
1. Prefer cards: most v3 timeline statuses (poison, burning, regen) become v4 cards whose lane a timeline sets on start and a timer channel ends, so no records are sent at all. Glue would emit, per card, the tl request that sets its lane.
2. For timelines that must emit amounts, emit an outbox column per channel and one `Send` pass before the step, matching v3's `Outbox.Send` (source = the status slot, target from an aim lane).
3. Prove it as v3 did with NorthStar3: one scenario run through tl and through hand-written sends must give equal digests, in .NET and in Unity.

## Progress
- The owner chose cards first as the default, with the outbox kept for ported v3 code.
- Issue 0528 added the Entities runtime both paths need: `ColumnsState.Sets` (lane edits between frames), `ColumnsCardSystem<T : IColumnsCardCell>` and `ColumnsOutboxSystem<T : IColumnsOutboxCell>`, proved against a hand-stepped `ColumnWorld`. What is left here is the generator (cells and tl request rules per column pack) and the .NET proof.

## Done
- Pack format: an optional `timelines` section in the v4 pack JSON (`cards`, `outbox` with `channel` and optional `target` lane, `starts` with `event` and `timeline`), checked by name in `PackCompiler.CheckTimelines`. It changes nothing in the compiled world (`TlGlueTests` proves the digest is the same with and without it).
- Generator: `sv pack <pack.json> --glue tl [--host unity]` (`src/Sv.Pack/ColumnsGlue.cs`). `Glue.g.cs`: per card a `[TlState]` cell, a kind (max), a `Level` clip and a track that writes the level on the clip's first forward tick (proposal 1); per outbox channel a cell, a summing kind, an `Amount` clip and a track as in v3 (proposal 2); for the plain host a `TimelineRequests` class (entered event starts, left event stops). `GlueEntities.g.cs` (unity host): the cells as `IComponentData` with `IColumnsCardCell` or `IColumnsOutboxCell`, their `RegisterGenericSystemType` lines and `TimelineRules.AddTo`. With the unity host the cell fields live in `GlueEntities.g.cs`, so the cells compile without tl.
- Unity request system: `ColumnsTimelineRequestSystem` (Burst, `Sv.Columns.Entities.Tl`, compiled only with `com.tl.unity`), in `ColumnsEventGroup` after `ColumnsFixedStepSystem`: rules from a `ColumnsTimelineRule` buffer on the config entity; per slot it tracks the events it saw in `ColumnsTimelineHeld`.
- Proof (proposal 3). .NET: `tests/Sv.Columns.Timelines` runs the generated plain glue through tl's plain host (burn and mend outbox timelines, a poison card timeline started by `LowHealth`) and checks the cells tl wrote, the start and stop, the health the rules give, and every frame digest against the same cells written by hand, at 1 and 4 tl workers. Unity: `ColumnsClockTests` uses the generated cells; `ColumnsTimelineRequestTests` checks start and stop; `ColumnsTlSceneTests` bakes a `ColumnsWorldAuthoring` (fixed step), a `ColumnsEntityAuthoring` and a `PlayableDirector` with the generated tracks, and checks every tick digest against a hand-stepped `ColumnWorld` at 30, 60 and 144 rendered fps. Golden output tests for the committed glue in `tests/Sv.Columns.Tests/TlGlueTests.cs` (`SV_UPDATE_GLUE=1` rewrites).
