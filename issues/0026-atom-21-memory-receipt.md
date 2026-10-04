# Issue 0026 — Atom 21: `Board.MemoryBytes` + `Board.RequestedCapacity` (implements 0022's loud-memory direction)

Filed as GitHub issue #26 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`.

## Problem

Implements the recommended-now direction of [issues/0022](0022-design-capacity-memory.md):
the board's eager `capacity x stats` preallocation was invisible (no receipt), and
`Board.Create` rounded capacity up to a 64 boundary silently. The chunked/lazy
rework stays gated on the million-sample bench per the original recommendation.

## Shape

- `Board.MemoryBytes` — the native bill this board currently owns: slot
  bookkeeping (free list, live words), the stats cross product (pairs, set table
  including member arrays, the atom-20 member-position index, dirty queue,
  scratch), sv-owned output columns, and live per-pair modifier storage at
  current capacities. O(capacity x stats) walk — tooling receipt, not a
  frame-path call.
- `Board.RequestedCapacity` — the pre-rounding value `Create` received, next to
  the (rounded) `Capacity`, so the 64-alignment padding is visible instead of
  silent.
- `SetTable.MemoryBytes` (internal) backs the set-table share.

## Receipts

- 1M x 3-stat board: `MemoryBytes` = 115,127,264 (109.8 MiB), requested
  1,000,000, capacity 1,000,000 (no rounding at a 64 multiple; a 1M+1 request
  reports both). Intrinsics-only 10-slot board: exactly 264 B (free list 64x4 +
  one live word) — the test pins it.
- Tests default: 139 + 16 + 6 passed, 0 failed. SV_CHECKED: 141 + 16 + 6
  passed, 0 failed.
- no-FMA grep clean.
