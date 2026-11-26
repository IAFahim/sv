# Issue 0104 — Atom 95 (roadmap 4.3): drift rides the frame — rebase on rule change, renumber, rollback

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 4.3, law L7 ("lazy
intrinsics are bit-equal to the frozen eager frame loop — random schedules:
hits, heals, bound changes, spawns, renumbers, rollbacks"). Labels: `ai`,
`area:core`, `kind:feat`.

## Changes

- **`DriftStore.AttachBoard(Board)`** — every later `Capture`/`Write`/
  `Rebase`/`RebaseRules` notes its 32 segment bytes into the board's undo
  window (`Board.ActiveUndo`), so **`Board.Rollback` restores drift segments
  bit-exactly with the rest of the state** (L7's rollback leg, L12). The
  notes are the same plain-entry kind the wheel already logs.
- **`DriftStore.RebaseRules(Window, tick, float* rate, float* floor,
  float* ceiling)`** — the frame's step-3 bulk rebase: each window row with a
  segment rebases to `ClampShift.Step(BoundOf(rate), BoundOf(floor),
  BoundOf(ceiling))` (clause-16 conversion; the stat-broadcast columns are
  indexed by absolute row). Range and masked windows; rows without segments
  are skipped — they are not drifting.
- **`DriftStore.Renumber(ReadOnlySpan<int> oldToNew)`** — the drift half of
  L3: segments remap through the same permutation as
  `Board.RenumberSlots`, so `read ∘ renumber = π ∘ read` at every tick;
  negative entries deactivate (renumbering closes the undo window, matching
  the board).
- `SegmentActive`/`CopySegment` internal accessors (the established
  test-seeding pattern).

## Tests (Sv.Tests total 414)

- `RebaseRules_RangeAndMasked_MatchPerRow` — bulk vs per-row `Rebase` over
  NaN/±∞/fractional stat columns, masked + range windows, skipped
  segmentless rows, 15 ticks of trajectory equality, null-column guard.
- `Renumber_ReadCommutesWithPermutation` — random permutation over 96 slots
  with ~25% dead: reads commute at two distant ticks, active ⟺ targeted,
  out-of-range and short maps rejected.
- `Rollback_RestoresDriftSegments_WithBoardUndo` — 8 marked frames of
  writes/rebases over a board undo window; every rollback depth restores the
  exact `(v₀, t₀, rule)` triple and 32 ticks of trajectory; frames re-run
  forward after each rollback (the UndoTests mark pattern).
- `RandomSchedule_HitsHealsBoundChangesSpawnsRenumbersRollbacks_MatchesEager`
  — the L7 receipt proper: 160 frames, 24 slots, eager model vs store with
  random writes (hits/heals), column-driven bound changes via `RebaseRules`
  (old rule applies within the rebased frame, then swaps — the §4.1
  convention), mid-schedule spawns, two random renumbers (model remapped,
  undo window reopened), and random rollbacks of depth 1–3 (model restored
  from its own mark stack). Every active slot compared at every frame.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (414 Sv.Tests, 7 Unity facade, 27 NorthStar).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (no drift path is hot yet — the store is consumer-ready for 4.4/4.5).
- Cost: one `ActiveUndo` null check per drift mutation; segments are 32 B,
  one log entry per write — O(Δ) with the window, same as a modifier write.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@e7af2e9` + this atom. Bus checked before and after (inbox empty).
