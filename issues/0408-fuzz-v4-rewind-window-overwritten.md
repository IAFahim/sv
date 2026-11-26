# 0408 fuzz: v4: after a rewind, frames already overwritten looked rewindable

Status: closed (2026-11-26, fixed: OldestFrame never moves back past a frame the ring overwrote)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api (lockstep against a fresh replay), sv commit d929d03

## What was wrong
`OldestFrame` was `frame - history`. After stepping past the ring and rewinding, it moved back to frames whose checkpoint slots had already been reused, so `Rewind(0)` restored a later frame's state and `Inputs(0)` returned another frame's log. The lockstep check saw the digest differ from a fresh replay.

## Fix
The world remembers the oldest frame still kept when each frame starts, and `OldestFrame` is the later of that and `frame - history`.

## Tests
`RedTeamWorldTests.After_a_rewind_frames_already_overwritten_stay_outside_the_window`.
