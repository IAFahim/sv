# 0405 fuzz: v4: a Send during a running frame overran the record log or was lost

Status: closed (2026-11-26, fixed: writers refuse sends while a frame runs)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api (InFrameMisuse), sv commit d929d03

## What was wrong
`BeginFrame` sizes the frame's record log from the writers' counts. A `Send`, `SendFrom` or `SendRecord` made after that, while the frame was still running, grew a writer past the log: the copy into history wrote past the buffer (a NullReferenceException inside Memmove, or silent heap corruption). On the fused path (`OpenFrame` to `CloseFrame`) the send went into a writer that `CloseFrame` then cleared, so the record vanished without being counted.

## Fix
`BeginFrame` (and so `OpenFrame`) locks every writer and `EndFrame` unlocks them; a send while locked throws InvalidOperationException naming the fix. The warm path is one compare (`Count == limit`), still 0 B allocated.

## Tests
`RedTeamWorldTests.Sends_inside_a_stepped_frame_are_refused_and_the_log_holds_exactly_what_was_applied`, `A_burst_of_sends_inside_a_frame_cannot_write_past_the_record_log`, `Sends_inside_an_open_fused_frame_are_refused_instead_of_lost`.

## Related
0519 made spawn, despawn and set throw during a frame; this closes the same gap for sends.
