# 0413 fuzz: v4: a frame edited and not resimulated before it leaves the window blocks Resimulate for good

Status: closed (2026-11-27, decided and fixed: the refusal stays, `DiscardLostEdits()` recovers)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api, sv commit d929d03

## What happened
Edit `Inputs(f)` for a frame inside the history window, then keep stepping without calling `Resimulate` until `f` is older than `OldestFrame`. Every later `Resimulate` threw InvalidOperationException ("frame f was edited but has left the history window"), and nothing cleared the mark short of a new world.

## Decision
The owner was away, so the most conservative choice was taken: a lost edit is never dropped silently. Options considered: (1) drop the mark when the frame leaves the window and count it as lost, (2) keep the refusal and add an explicit way out, (3) keep the refusal and document it. Option 1 hides lost input unless the host reads a counter; option 3 leaves a host stuck. Chosen: option 2.

- `Resimulate` (and `ResimulateFused`) still throw while an edit has left the window; the message now names `DiscardLostEdits()`.
- `DiscardLostEdits()` gives up the lost edit, returns the earliest lost frame (-1 when nothing was lost), and moves the start of the next resimulate to `OldestFrame`, so edits still inside the window are applied. With nothing left in the window (after `Rewind(OldestFrame)`), it clears the marks.
- `EditedFrame` reports the earliest edited frame the next resimulate starts from, or -1.

To switch to option 1 later: call `DiscardLostEdits()` from `Step` when `EditedFrame < OldestFrame` and count the result.

## Tests
`LostEditTests` (5 tests, 6 cases): refusal until discard, a discard keeps in-window edits (stepped and fused, digest equal to a world that never made the lost edit), a discard with nothing lost changes nothing, a discard after a rewind to the oldest frame, a world without history. tests/Sv.Fuzz v4api checks `EditedFrame` against its own model before each resimulate and calls `DiscardLostEdits()` on half of the refused cases.

## Not checked
The Unity hosts (`ColumnStepper`, `ColumnsBurstHost`) reach the call through their `World`; no Unity test edits history this way.
