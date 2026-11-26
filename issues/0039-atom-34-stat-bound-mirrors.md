# Issue 0039 — Atom 34: stat-bound mirrors (`Board.MirrorBounds`, `Clamp.StatMirror*`)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #39 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 2 (clause 10,
additive) — vendored contract stays v1.0.3 until ack.

## Problem

`Clamp.Stat` converts each float bound to int (clause 16) per row per op, and it can
only read stride-1 bound columns (atom 33 now rejects strided ones). Bounds change
only at broadcast, so the conversion can run there, once, into an int column that
any binding layout can feed.

## Shape

- `Board.MirrorBounds(stat)` -> `int*`: sv-owned, capacity-sized, never moves.
  First call fills live rows from the current output column (external with its
  stride, or sv-owned); from then on every broadcast path (`BroadcastOne` and the
  full-set broadcast) writes `Clamp.BoundOf(value)` beside the float. `RenumberSlots`
  permutes mirrors like the sv-owned columns. Freed at dispose; counted in
  `MemoryBytes`.
- `Clamp.StatMirror(board, min, max)`, `StatMirrorMin`, `StatMirrorMax`: the
  `Columns` clamp over the mirrors — same vector path as host int columns.
- `Clamp.BoundOf` is public (the pinned clause-16 conversion, for hosts that build
  their own int bounds).
- Semantics: mirrors see broadcasts only; a host writing a bound float column
  directly is not reflected (documented on `MirrorBounds`).

## Receipts

- `BoundMirrorTests`: 2 seeds x 80 frames of churn with saturating modifiers
  (+-1e30, +-4e9), a reversal renumber at frame 30, a rebind to a stride-2 external
  AoS column at frame 50 — mirror clamps match the reference clamp row for row and
  applied for applied (reference: `Clamp.Stat` while stride 1, host-built
  `BoundOf` int columns after the strided rebind). Fill-from-current and pointer
  stability pinned.
- Bench (100k rows, L2-resident, 3 runs): statclamp 25.4-25.8 us, statmirror
  23.8-24.7 us (~5%). After atom 24's specialization the conversion is cheap; the
  two bound-column reads dominate. The bus 0036 evidence line (25.4 vs 14.9 us) is
  corrected on the bus: the win is stride-capable stat bounds plus ~5%, not ~40%.
- Tests default: 162 + 21 + 6 passed, 0 failed. SV_CHECKED: 164 + 21 + 6 passed.
- no-FMA grep clean.
