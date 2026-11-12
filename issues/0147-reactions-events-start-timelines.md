# Issue 0147 - Reactions: an sv event starts a tl timeline, declared as data

Status: open

Labels: `ai`, `area:unity`, `area:authoring`, `open`.

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
