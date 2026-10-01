# Issue 0022 — Design: board memory scales with capacity x stats, not liveness (open — on the bus)

Filed as GitHub issue #22 (open). Labels: `ai`, `area:core`, `kind:perf`.
Contract touchpoint: clause 5 (dense slots) — allocation strategy is an implementation
freedom, but the tradeoff below deserves a deliberate pass, not a drive-by.

## Problem

`InitStats` preallocates the full cross product at `Create`: `statCount x capacity` of
PairMods (24 B each) plus pairSet (4 B) + pairQueued (1 B) + svColumns (4 B) per pair,
and `Board.Create` rounds capacity up to 64 silently. A 1M-capacity board with 3 stats
commits ~70+ MB of zeroed native before a single modifier or live slot exists.

## Candidate directions (for the bus)

- Chunked/lazy pair storage: allocate pair storage per 4096-slot span on first touch —
  keeps dense determinism inside a span, drops the empty-board cost to O(stats).
- Document + round capacity loudly: keep the preallocation philosophy, make the memory
  bill visible (a `Board.MemoryBytes` receipt) and stop rounding silently.
- svColumns are small by comparison but follow the same shape; they are also the
  clause-4 broadcast targets, so they likely stay eager.

Not implemented before ack; the million sample stays the receipt vehicle for whichever
direction wins.

## Receipt

- 2026-10-02: bus message 0033 sent to integrator (Reply-To 0029). Recommendation on
  top: loud eager now (`Board.MemoryBytes` receipt + documented 64-rounding), chunked/
  lazy rework gated on the million-sample bench. Ruling awaited.
