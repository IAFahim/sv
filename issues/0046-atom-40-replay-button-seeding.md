# Issue 0046 — Atom 40: `BeginReplay` seeds button state from the prior ring entry (review C-2)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #26 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-2 — verified
against source before fixing.

## Problem

`BeginReplay` zeroed both `previous` and `buttons`. The first `ReplayStep` then computed
`pressed = word & ~previous` with `previous = 0`, so every button held across the
rollback boundary replayed as freshly pressed. Rollback resimulation diverged from the
original run whenever a held button spanned the boundary.

## Shape

- `BeginReplay(fromTick)` seeds `buttons`/`previous` from the ring entry of tick
  `fromTick − 1` when that entry is still resident (`ringTickIds[prior % RingTicks] == prior`).
- Fallback (no prior tick, or it was evicted): zero both words, as before — the first
  replayed tick then reports held buttons as pressed, which is exact for tick 0 and the
  only defined behavior when the prior state is unknowable.
- No signature or state-format change.

## Receipts

- `ReplayEdgeTests` (4 facts): mid-stream hold replays as held not pressed; tick-0
  replay still reports the press; evicted prior falls back to the zero seed; a real
  release edge is reproduced exactly (and no spurious press).
- Existing `Replay_FromTick_IsBitIdenticalToLiveRun` (random buttons, replay from 0)
  unchanged and green — tick-0 path is behavior-identical.
- Suite: build green, 210 tests pass (23 NorthStar + 6 Unity + 181 Sv.Tests).
- Test-writing note for future atoms: `stackalloc` scratch in tests is uninitialized —
  seed it explicitly (an early version of the evicted-prior test captured garbage words
  and failed for that reason, not a library defect).
