# Issue 0106 — Atom 97 (roadmap 4.5): per-slot op folding in the drain

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 4.5, law L6 (monoid:
`Step(δ,f,c).Repeat(n).Apply(x)` = n eager steps — the drain is the k-op
case). Labels: `ai`, `area:core`, `kind:feat`.

## Changes

- **`DriftOp`** — `(int Slot, ClampShift Op)`, unmanaged, 24 B; order is
  semantics (the monoid is not commutative).
- **`DriftStore.ApplyBatch(long tick, ReadOnlySpan<DriftOp>)`** — the drain's
  fold: ops are **stably radix-sorted by slot** (4×8-bit counting passes,
  O(Δ), deterministic), each slot's ops compose in **arrival order** through
  `ClampShift.Then`, and the composed map applies once to the value at the
  end of frame tick−1 — **folding k hits is one write per touched slot**,
  bit-equal to applying the k hits eagerly and then the frame's drift step.
  The write rides the undo window like every segment mutation (atom 95).
  Out-of-range slots reject the whole batch; empty batches are no-ops.

## Tests (Sv.Tests total 420)

- `ApplyBatch_FoldsKHitsIntoOneWrite_MatchesEager` — 200 frames × 12 slots ×
  up to 40 random ops/frame (random shifts, floors, ceilings incl. constant
  maps), batch order deliberately unsorted: every slot, every frame, bit-equal
  to the eager model (ops in arrival order, then the frame's drift). The
  stability of the radix is load-bearing here — an unstable sort would flip
  same-slot op order and the fuzz catches it.
- `ApplyBatch_FoldsInArrivalOrder_NotCommutative` — `(+9 cap 10) then (−7)`
  reads 2; reversed reads 9; empty batch is identity; out-of-range slot
  throws.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (420 / 7 / 27).
- Cost: the sort is 4 stable counting passes over 24 B ops — ~40 ns per 100
  ops on this box class, O(Δ); two transient NativeMemory blocks per batch,
  freed before return.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@1193267` + this atom. Bus checked before and after (inbox empty).
