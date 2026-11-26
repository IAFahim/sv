# 0406 fuzz: v4: editing history inside a running frame corrupted the log

Status: closed (2026-11-26, fixed: history edits throw while a frame runs)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api (InFrameMisuse), sv commit d929d03

## What was wrong
`Inputs(f).AddRecord`, `RemoveRecord`, `AddAction` and the rest ran between `BeginFrame` and `EndFrame`. The running frame's log was half written at that point, so an edit to `Inputs(Frame - 1)` was overwritten or shifted records the frame was still copying, and a later Resimulate replayed something no one sent.

## Fix
`EditLog` calls `Editable()` first, which throws InvalidOperationException while a frame runs.

## Tests
`RedTeamWorldTests.History_cannot_be_edited_while_a_frame_runs`.

## Related
0519 made spawn, despawn and set throw during a frame; this closes the same gap for history edits.
