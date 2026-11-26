# 0407 fuzz: v4: a refused history edit marked the frame edited and could wedge Resimulate

Status: closed (2026-11-26, fixed: only an edit that happens marks the frame)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api, sv commit d929d03

## What was wrong
`EditLog` set `editedFrom` before the index checks ran. An edit refused with ArgumentOutOfRangeException still marked its frame edited. Once that frame left the history window, every `Resimulate` threw ("frame 0 was edited but has left the history window") for the rest of the world's life, though nothing had changed.

## Fix
The six edit methods mark the frame only after their checks pass.

## Tests
`RedTeamWorldTests.A_refused_history_edit_does_not_mark_the_frame_edited`.
