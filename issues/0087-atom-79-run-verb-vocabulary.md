# Issue 0087 — Atom 79 (roadmap 7.1): `Column`/`Window`/`Delta`/`Bounds` — one `Run` verb

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 7, atom 7.1
(§5.1). Labels: `ai`, `area:core`, `kind:feat`. Contract: additive —
the clause-5 names survive as aliases (they already delegate through
`Sv.Kernel` since atom 78).

## Changes

The §5.1 axioms become value types, and the twelve-overload choice
product collapses into one verb:

- **`Column<T>`** — `(T*, length)`, validated at construction (negative
  length throws; a null pointer may only carry length 0). The column's
  length is what the window guards validate against — no separate
  capacity parameter.
- **`Window`** — `Range(start, rows)` or `Masked(start, words, wordCount)`
  (the board's multi-word semantics already accept any offset; the
  alignment rule was only the Frent seam's). `default` is an empty range.
- **`Delta`** — `Uniform(int)` or `PerRow(int*)`; `default` is uniform 0.
- **`Bounds`** — the clamp vocabulary as factories over the unmanaged
  `Clamp` (atom 77): `None()`, `Constant(min, max)`, `Rows(min?, max?)`,
  `Stats(board, min?, max?)`; `default` is none.
- **`Kernel.Run(value, window, delta, bounds, applied = default)`** —
  the one verb: validates the window against `value.Length` (reusing
  `Guard.ChunkRange`/`EnabledRange`), checks `applied` coverage, then
  dispatches to the atom-78 static kernels. `applied` empty means no
  applied column.

## Tests (L5/L9 shape)

- Dispatch equality: every `Window` × `Delta` × `Bounds` combination
  through `Run` equals both the scalar oracle and the old
  `Board.Run*` verb for the same inputs (aligned and unaligned masked
  offsets).
- Guards: windows beyond the column throw; short `applied` throws;
  `Column`/`Delta` construction validation.
- L9 partition invariance: a random enabled mask with per-row deltas
  run as one `Run` equals the same window run as disjoint sub-windows
  in shuffled order (values and `applied` identical).

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **342/342 passed** (337 + 5
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 342/342.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok, alloc 0 B (the verb is additive; every scenario drives the old
  alias names, unchanged).
- L5-shape receipt: every `Window` × `Delta` × `Bounds` combination
  through `Run` equals the scalar oracle and the old `Board.Run*` verb
  (aligned and unaligned masked offsets — the board's multi-word guard
  accepts any offset; the floor-division window math is pinned by the
  guard test). L9 receipt: a 4-word random mask with per-row deltas run
  whole equals itself run as four disjoint one-word sub-windows in
  shuffled order (values and `applied` identical).
- `Delta` uses constructors (`new Delta(7)` / `new Delta(int*)`) — a
  `Uniform`/`PerRow` factory name collides with its own property in C#.
- FMA grep clean; no production comments.

## Provenance

`main@d3499f1` + this atom.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
