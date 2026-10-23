# Issue 0092 — Atom 84 (fix, roadmap 3.2): keyframe restore initializes `Wheel` — one wild unlink corrupted the heap

Fixes atom 83 ([issues/0091](0091-atom-83-timing-wheel.md)). Roadmap
[issues/0058](0058-roadmap-1m-entities-5us.md) phase 3. Labels: `ai`,
`area:core`, `kind:feat`. Contract: none.

## The defect

`Board.Keyframe.cs` restored records with `new ModRecord { … }` **without
`Wheel`** — the only construction site besides `AddModifier` (which sets
`Wheel = -1`) and the only one that forgot. Unlisted fields of a fresh struct
default to 0, so every record restored by a `LoadKeyframe` fold carried
`Wheel = 0` instead of −1. On the next fold of the same slot,
`WheelUnlink` trusted the field, read `wheelEntries[0]` — an uninitialized
entry (garbage `Prev`/`Cell`) or a freed free-list entry chained to a live
owner — and performed a wild unlink: `wheelEntries[entry.Prev].Next = …`
is an arbitrary 4-byte heap write, `FreeWheelEntry(0)` double-frees the
entry pool slot and drives `wheelEntryLive` negative.

Manifestation is heap-layout dependent, which is why it slipped through: all
377 tests green and the office host's bench green, but the full bench suite on
the bus host crashed **differently on each run** — an AccessViolationException
in `WheelUnlink ← LoadKeyframe` (receiver board) in one run, `sv: slot 178555
is not live` from the **source** board's corrupted live bitmap in the next.
The wild write lands wherever `wheelEntries`' neighbors sit: `keyframe`
standalone passed because the same deterministic writes fell in harmless
memory there.

## Changes

- `src/Sv/Board.Keyframe.cs` — the restore initializer gains `Wheel = -1`,
  mirroring `AddModifier`. One field; `WheelInsert` overwrites it for
  expiring records exactly as before.

## Tests (total 378)

- New `WheelTests.KeyframeRefolds_RecycledRecordsStayOffTheWheel`: a
  receiver folded three times over a slot holding one expiring and two
  stacked non-expiring modifiers must keep `PendingExpiries` at exactly 1,
  the digest equal to the source, and the folded value correct (10 + 1 + 4
  + 8 = 23). **Fails without the fix** (the third fold's unlink of the
  `Wheel = 0` record double-frees entry 0; `PendingExpiries` goes negative
  and the chain corrupts) — verified by running the test against the
  unfixed tree before restoring the fix.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **378/378 passed** (377 + 1).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 378/378.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — the full suite,
  twice back-to-back: all scenarios ok including `keyframe` (275 / 312 µs),
  which crashed on both pre-fix runs on this host; ff1m p50 3,032 µs,
  spawn1m 4.5–4.7 ms, rollback-resim ~364 µs, bulkpass 1.20× / 1.37× on 32
  cores, alloc 0 B everywhere.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@bf4482f` + this fix.
