# 0532 v4: frame phases ran out of order without complaint

Status: closed (2026-11-26, fixed: out-of-order phases throw InvalidOperationException)

Found by: the 2026-11-26 triage and review pass.

## What was wrong
`BeginFrame` could run twice (the frame counter moved twice for one step), and `Reduce`, `Resolve` and `EndFrame` ran with no frame open. With history on, a stray `EndFrame` wrote the current frame's checkpoint and cleared its action log, so a spawn made before it vanished from `Inputs(frame)` and from every later `Rewind` and `Resimulate`. A host driving the phases itself (`ColumnRunner`, Unity's `ColumnStepper`) is the one that can get this wrong.

## Fix
`BeginFrame` throws while a frame runs; `Reduce`, `Resolve` and `EndFrame` throw while none does. Spec section 5 says so. Tests: `SourceAndFrameTests.FramePhasesRunOnlyInOrder`, `AStrayEndFrameCannotWipeTheActionsOfTheCurrentFrame`.
