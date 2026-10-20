# Issue 0054 — Atom 47: zero-filled Permute scratch + validate-before-rewrite Reconcile (review C-5, partial)

Filed as GitHub issue #34 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-5 (parts 2–3) —
verified against source before fixing. Part 1 (`Game`-owned columns never permuted)
needs a `Game.Renumber` on the compose side and is queued behind it; the
`Permute`-count footgun (`count` = new-index upper bound, not live count) is
unresolved — see open questions.

## Problem

- **C-5.2:** `Columns.Permute` took its scratch block from unzeroed
  `NativeMemory.Alloc`; only mapped indices were written, then all `count` entries were
  copied back — unmapped padding slots received heap garbage.
- **C-5.3:** `Population.Reconcile` rewrote `SvSlot` components chunk by chunk and only
  then called `Board.RenumberSlots`, which throws on count mismatch — an unbound live
  slot (or double-bind) left the world rewritten but the board unchanged, with no way
  back. Capacity overflow threw mid-rewrite the same way.

## Shape

- `Permute` scratch is `AllocZeroed`: padding slots deterministically read 0.
- `Reconcile` runs two passes: pass 1 computes the layout and validates capacity and
  `mapped == Board.LiveCount` (catches unbound slots and double-binds) **before any
  rewrite**; pass 2 assigns and rewrites. A failed reconcile now leaves world and board
  exactly as they were.

## Receipts

- `ReconcileAtomicityTests` (3 facts): permuted padding slots read 0, not garbage; an
  unbound live slot throws before any rewrite (entity slot intact, board live count
  intact); a normal unbind + reconcile renumbers the remaining entities into a dense
  64-aligned block.
- Suite: build green; 234 tests (23 + 6 + 205); existing `PopulationTests`
  reconcile/remap tests unchanged and green.

## Open (queued, not this atom)

- `Game.Renumber(perm)` permuting `TimelineIds`/`TimelineClocks`/`Applied`/effect
  columns (C-5.1) — compose-side API addition.
- `Permute`'s `count` parameter meaning (new-index upper bound vs live count) — the
  natural `Permute(col, perm, board.LiveCount)` call still throws when 64-padding
  pushes a target above `LiveCount`; fixing the ergonomics is an API decision.
