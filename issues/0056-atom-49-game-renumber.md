# Issue 0056 — Atom 49: `Game.Renumber` permutes every game-owned column (review C-5 part 1)

Filed as GitHub issue #36 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:compose`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding C-5 part 1 —
verified against source before fixing. Closes the open item from
[issues/0054](0054-atom-47-permute-and-reconcile-atomicity.md).

## Problem

`Board.RenumberSlots` renumbers the board's slot-indexed storage, but `Game`'s own
slot-indexed columns — `TimelineIds`, `TimelineClocks`, `Applied`, and every timeline
effect column — were the host's to permute by hand through `IFrameWorld.Reconcile`.
Missing one meant entity N played entity M's clip after compaction.

## Shape

- `Game.Renumber(ReadOnlySpan<int> oldToNew)`: validates map length against capacity,
  then permutes timeline ids, clocks, the applied column and every effect column via
  `Columns.Permute` (capacity-length counts, so 64-alignment padding targets are
  legal; dead slots read zero).
- `Columns.Permute` doc now spells out the `count` contract: one past the highest new
  index; the column length is always safe, `board.LiveCount` is not.

## Receipts

- `GameRenumberTests` (2 facts): a swap permutation moves ids/clocks/applied/effect
  together and dead slots read zero; a short map throws `ArgumentException`.
- Suite: build green; 241 tests (27 NorthStar + 6 Unity + 208 Sv.Tests).
