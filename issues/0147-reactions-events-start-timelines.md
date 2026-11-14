# Issue 0147 - Reactions: an sv event starts a tl timeline, declared as data

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

Labels: `ai`, `area:unity`, `area:authoring`, `closed`.

## Problem

sv derives events (`host.Events(hp)`, `EdgeBit`, `Channel.Dense`) but nothing connects them to tl. A designer cannot say
"when LowHp is entered, start the `panic` timeline" without a programmer writing a system that reads `SvEvent` and adds a
`TimelineRequest.Start`.

## Wanted

- `reactions` in the stat sheet (issue 0145): `{ "on": "LowHp.enter", "start": "panic" }`, `{ "on": "Hp.zero", "start": "die" }`.
- One generic system (shipped, not per game) that turns events into `TimelineRequest.Start` for the row, in ascending slot
  order, deterministic. Timelines named in reactions are added to the row's reachable set (tl's `May Start`) at bake.
- Over the channel cap (one `Dense` record): defined behaviour, documented (start for all rows with the edge bit, or none).
- Event reads see the tick's fold, one frame later than the delta (docs/UNITY.md section 4): state this in the sheet docs.

## Done when

- A scene: damage clip drops Hp below 30 percent, `panic` starts the next frame, `LowHp` leaves at 40 percent with hysteresis.
- No dropped starts (`DroppedStarts` stays 0) when the sheet and bake are consistent.
- Same result at 1 and 11 workers; digest pinned.

## Progress 2026-11-13 (v3, branch v3/m8-unity)

Status stays open: the Unity scene leg is not verified. Done on .NET with the v3 engine: an event row carries `"starts":"<timeline>"` (values.sv.json, events only, no PackHash moved for existing packs); the generated `TimelineRequests` (`sv pack --glue tl`) starts the timeline on enter and stops it on leave, in the host's event order, timelines resolved once by name. `tests/Sv.NorthStar3` (17 tests): LowHealth starts `poison` on the entity that went low (4 ticks), a heal above 40 percent leaves the event and stops it early, same digests at 1, 2 and 11 workers, replay from the causes alone equal, pinned digests `A566FAEC8837BA57` (hurt) and `2D6A06776AE25DCB` (hurt then healed), equal under DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0. Open: the same in a Unity scene with tl (needs tl in the Unity project), the over-cap `Dense` behaviour of requests, `May Start` reachable sets at bake.

## Decision (2026-11-14)

Done in v3 (M9): an event value declares `"starts": "<timeline>"`, `sv pack --glue tl` emits `TimelineRequests`, which starts the timeline on the event's enter edge and stops it on leave. Proven on .NET (`tests/Sv.NorthStar3`, scenarios low_health_requests_poison and low_health_then_healed, pins A566FAEC8837BA57 and 2D6A06776AE25DCB) and in Unity (`Sv.Unity.V4.TlTests`, 1 and 11 workers, issue 0400).
