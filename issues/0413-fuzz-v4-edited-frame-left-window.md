# 0413 fuzz: v4: a frame edited and not resimulated before it leaves the window blocks Resimulate for good

Status: open (found by fuzzing, not fixed: needs a design decision)

Found by: Claude red-team pass, tests/Sv.Fuzz v4api, sv commit d929d03

## What happens
Edit `Inputs(f)` for a frame inside the history window, then keep stepping without calling `Resimulate` until `f` is older than `OldestFrame`. Every later `Resimulate` throws InvalidOperationException ("frame f was edited but has left the history window"), and nothing clears the mark short of a new world.

## Why it is open
Refusing is defensible: the edit can no longer be applied, and silently dropping it would hide lost input. But a host has no way to say "forget that edit". Options: drop the mark when the frame leaves the window and count it as lost; add a `DiscardEdits()`; or keep the refusal and document it in the spec.

## Not checked
How the Unity hosts react; they do not edit history today.
