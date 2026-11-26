# Issue 0082 — Atom 74 (roadmap 5.3): Composed rollback — input ring and event stream windows, `GameHost.Rollback`

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 5, atom 5.3
(composed half). Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **InputState undo window** (`src/Sv/InputState.cs`): an `UndoLog` wired by
  `BeginUndoWindow(frames)`. `Capture`, `BeginReplay` and `ReplayStep` note every
  prior byte they mutate — axis bits, the ring slot (axes + button words), the tick
  id, ring head, tick counter, edge state (buttons/previous/pressed/released spans)
  and the replay cursor — so a replay restores the exact input state at the mark.
  All buffers are fixed-size; no quarantine is needed. Loading state closes the
  window.
- **EventStreams undo window** (`src/Sv/EventStreams.cs`): an `UndoLog` plus a
  growth journal. `AppendBatch` notes the destination bytes before writing (the
  double buffer reuses halves one frame later, so published events must be
  restorable) and `writeOffset`; `TryReadNext` notes the read cursor; `Swap` notes
  the offsets, peaks and active half. Arena growth while a window is open
  allocates a fresh block and journals the replacement instead of reallocating
  (a replay targets the old block); rollback frees the new block and restores the
  old pointer and capacity, eviction commits old blocks, and `ShrinkArena` is
  suppressed while a window is open. Loading state closes the window.
- **Composed window** (`src/Sv.Compose/Compose.cs`): `Game.BeginUndoWindow(frames)`
  opens all three windows with a ring of `frames + 1` marks (the topmost mark is
  "now"); `FrameRunner.Run` marks one boundary after `Swap`, so a mark is a
  completed frame with its events published; `Game.Rollback(frames)` (mirrored on
  `GameHost`) replays board/input/streams to `top − frames`, then re-opens the
  target ordinal so the next `Tick`'s input capture lands in a fresh buffer and
  re-driving the same inputs reproduces the frames bit-identically. `Game.Renumber`,
  `Game.LoadState` (via each component's reading branch) and `Dispose` close the
  window. `Board.RollbackTo(target)` added (target-driven core of `Board.Rollback`).
  Timeline columns and host columns stay the host's to snapshot, exactly as with
  `SaveState`.

## Tests (`ComposeRollbackTests`, 3 facts; total 330)

- `Rollback_MatchesTheGameSnapshotAtTheMark` — L12 at the composed level: per-frame
  `Game.SaveState` marks across input captures, event emits and drains; rollback
  depths 1..8 byte-compare against the mark, then re-driving the recorded inputs
  reproduces the top mark bit-identically.
- `Rollback_RestoresEventsPendingDrain` — events published at the mark survive the
  next frame's appends into the reused half and re-drain correctly after rollback.
- `RollbackBounds_AndWindowClosedByLoad` — window limits throw; `LoadState` closes
  the window; rollback after close throws.

All 327 pre-existing facts green unmodified.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **330/330 passed** (327 + 3 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 330/330.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (both modes).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok; the new
  `rollback-resim` scenario (2²⁰ entities, 8-frame window, Δ = 128: 96 edits + 32
  expiries per frame; one iteration = `Rollback(8)` + 8 re-driven frames) runs
  **~270–340 µs/cycle, allocation-free, digest-verified** at the end of every cycle.
  The cycle is O(Δ): scaling entity count 64K → 1M moves it 175 → ~310 µs and the
  residual growth tracks the random-access cache misses of Δ scattered edits, not
  any capacity scan. The roadmap's ≤ 50 µs figure assumes the end-state per-op
  costs (same envelope as the ≤ 5 µs frame, today 2.9–3.0 ms at ff1m); the
  dominant costs today are the per-edit undo notes (~6 entries per modifier edit)
  and the scattered small-copy replay (~11 ns/entry, memory-bound). A note
  coalescing + replay pass is queued behind the phase 6 performance atoms.
- Hot path with no window open: `fused-tick` 695.7 µs vs 687.0 before this atom
  (+1.2 %, the window checks); ff1m p50 ~2.9–3.0 ms unchanged; spawn1m 4.9 ms;
  tape-record 0.036 µs.
- FMA grep clean; no production comments.

## Provenance

`main@9cc592d` + this atom. Next: 5.4 — incremental keyframes (pages dirtied since
the last keyframe; cost ∝ dirty pages).
