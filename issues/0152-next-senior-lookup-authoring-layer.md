# Issue 0152 - Next senior lookup: the authoring layer the first plan did not cover

Status: open

Labels: `ai`, `area:plan`, `open`, `senior-lookup`.

## Why

The first senior question (SV-Q6 in ask-senior-sv.md) asked for "the smallest API" between tl and sv. The plan answered the
seam only: `ColumnDef`, `Link`, `StatOp`, the clip source id, "nothing new is written in a kind" (plan-sv.md lines 350-401).
It did not say what a designer or a tl toolbox programmer writes. Phase 1 and 2 built what the plan specified plus
hardening, and the designer layer is missing (issues 0145 to 0148). Ask again with the questions below.

## Context to attach

tl's real authoring flow: tl/README.md (roles table), tl/docs/json-format.md, tl.unity/docs/KINDS.md,
tl.unity `Tests/Kinds/Game/KindGame.cs` (`Hit`, `Damage`, `[Pulse]`, `[TimelineSystem]`), tl `src/Tl.Core/CoderApi.cs`
(`TlClip`, `TlKind`, `TlState`, `ITlTrack`). Designers use Unity's Timeline window and JSON, never code.

## Questions

1. **Sheet and bake:** what is the smallest data format and bake step for stats, intrinsics, bounds, conditions and
   reactions, in tl's style (`tl library`, `Timelines.g.cs`)? Where does it live (sv, tl, or glue), given libraries do not
   reference each other?
2. **Modifier clips from Burst:** the best Burst-safe way for a kind `Apply` to emit `StatOp`s deterministically (per-worker
   lists, per-row buffer, or tl's entry buffer), with reverse playback, interrupt, and rollback exactness.
3. **Events to timelines:** how sv events start tl timelines without a per-game system; ordering, the over-cap Dense case,
   and one-frame latency.
4. **Cross-row writes for designers:** damage to another entity as data (a clip with a target), instead of a
   hand-written send job.
5. **Hierarchy of ownership at 1M rows:** given tl's kind job costs 8.3 ms alone (issue 0149), where should the next
   budget be spent.

## Done when

A written answer with signatures and files, saved next to plan-sv.md, and issues 0145 to 0148 updated from it.
