# Issue 0081 — Atom 73 (roadmap 5.3): Board undo window — rollback equals the snapshot at the mark

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 5, atom 5.3
(board half). Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Undo log** (`src/Sv/UndoLog.cs`, new): a ring of W frame entry buffers.
  `Note(address, bytes)` appends `[address][length][prior bytes]` before every
  state-memory write; `NoteRange` covers spans. Replay walks a frame's stream
  into a growable offset scratch (`order`) and restores newest-first; the
  scratch sizing counts both the entries already collected and the remaining
  bytes, so frames whose tail entries are long spans still grow before the
  write. `ReallocTracked` replaces in-place realloc for every growing array:
  allocate fresh in the arena, copy the shared prefix, quarantine the old
  block over the frame range `[0..top]` and the new block over
  `[top..∞)`; pointer-deduped by `NoteReplaced`. Fresh allocations (slot
  pages, owned columns, mirrors) quarantine as `Fresh` with their frame —
  dropped by `DropFreshFrom(target)` on rollback, committed (record dropped,
  memory kept) when eviction passes their frame. `UndoFrames(from, to)`
  replays frames newest→oldest inclusive and clears each consumed frame;
  `TruncateTo(ordinal)` rewinds the ring top.
- **Board window** (`src/Sv/Board.Undo.cs`, new): `BeginUndoWindow(frames)`
  wires the log into board, set table and block table and raises the set
  reclamation horizon to the window; `BeginUndoFrame` snapshots every
  mutating scalar (live count/hint, insertion id, record/expiry/touched
  counts, caps and pointers, digest + page-dirty bit, the full set-table and
  block-table scalar sets) into the ring slot, evicting (committing) the
  oldest mark when the window is full; `Rollback(frames)` replays the entry
  frames, restores the target mark's scalars, frees fresh allocations at or
  after the target, truncates and sweeps. `Rollback(k)` is bit-equal to
  `LoadState` of a snapshot saved at that mark (L12). Loading state,
  renumbering slots, (re)binding outputs/externals, invalidating bindings
  and `Dispose` close the window — bindings are not state, and a rolled-back
  write must never target a host column that moved.
- **Write hooks** (`src/Sv/Board.cs`, `src/Sv/Board.Stats.cs`,
  `src/Sv/SetTable.cs`, `src/Sv/BlockTable.cs`): every mutating write notes
  its prior bytes — live words, slot states (with fresh-page detection so
  first-touch initialization of a quarantined-fresh page is unlogged),
  modifier records and list heads, expiry entries, dirty/changed/acquired
  masks and touched lists, set-table and block-table fields, buckets, the
  content arena (in-band freelist included), owned/mirror/external column
  cells, and page-digest cells (suppressed while `RecomputeDigest` rebuilds
  them; a rollback over any frame that recomputed leaves
  `digestPagesDirty` set so page sums rebuild lazily). All growth paths go
  through `UndoGrow` → `ReallocTracked` while a window is open.
- **Reclamation horizon** (`src/Sv/SetTable.cs`): `ReclaimHorizon` moves
  from a constant 0 to the open window's frame count while a window is open
  — a freed set is reusable only once it is older than the horizon, so an
  undo never resurrects a freed set.
- `Board.UndoFrames` reports the open window's retained frame count.

## Tests (`UndoTests`, 5 facts; total 327)

- `Rollback_MatchesSnapshotTakenAtTheMark` — L12: per-frame `SaveState`
  marks, rollback depths 1..8, byte-compare against the mark plus digest
  equality.
- `Rollback_ResimReplaysFramesBitIdentically` — rollback then re-running
  the same per-frame seeds reproduces the marks bit-for-bit.
- `UndoWindow_EvictsOldestMarkAndLimitsRollback` — a window of 3 over 5
  frames commits the two oldest marks; `Rollback(4)` throws
  `ArgumentOutOfRangeException`; rollback after `EndUndoWindow` throws
  `InvalidOperationException`.
- `Rollback_AfterBulkSpawnRestoresDigest` — 40,000-slot spawn (deferred
  digest path) inside the window; rollback restores the digest to the
  mark's.
- `RebindAndLoad_CloseTheUndoWindow` — `BindOutput` and `LoadState` reset
  `UndoFrames` to 0.

All 322 pre-existing facts green unmodified. The rollback-vs-snapshot law
was additionally verified under `MALLOC_CHECK_=3` churn (acquire, release,
timed and untimed modifiers, expiry, spawn, recalculate; 8 frames, rollback
depths 1..8, resim after each) — which caught and fixed a real scratch
sizing bug: `GrowOrder` sized the replay offset scratch from remaining
bytes alone, so a frame whose tail entries were long spans stopped growing
the scratch and wrote past its end.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **327/327 passed** (322 + 5 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 327/327.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (both modes).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok;
  tape-record 0.033 µs/frame, spawn1m 4.8 ms, ff1m p50 2,852 µs (Δ=128) —
  unchanged: the hooks are inert with no window open.
- FMA grep clean; no production comments.

## Provenance

`main@e417806` + this atom. Next: 5.3 composed half — InputState and
EventStreams windows, `GameHost.Rollback`, the 8-frame rollback + resim
≤ 50 µs at Δ = 128 receipt.
