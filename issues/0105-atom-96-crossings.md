# Issue 0105 — Atom 96 (roadmap 4.4): crossings → wheel → events

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 4.4, law L8 ("the
scheduled tick is the first eager tick that satisfies the predicate").
Labels: `ai`, `area:core`, `kind:feat`. `ClampShift.FirstTickAtLeast`
itself landed with atom 88 (`issues/0096`); this atom is the wheel side.

## Changes

- **The wheel holds drift crossings alongside modifier expiries.**
  `Board.ScheduleCrossing(long atTick, long id)` inserts a wheel entry with a
  host-chosen 64-bit id (generation-tagged per the 0045 discipline — a stale
  generation drains and the host drops it; there is deliberately no cancel
  API). Entries carry their own `Expiry` (the entry grows 32 → 40 B), which
  also removes the wheel's dependency on `records[].Expiry` for overdue-list
  ordering — crossings and expiries order by the same (tick, id) key.
- **`ExpireModifiers` collects due crossings** into a ring and
  **`TakeDueCrossings(Span<long>)` hands them to the host in (tick, id)
  order** — the host emits its clause-17 edge-triggered events (the fused
  frame drains the wheel at the clause-7 Expire step; crossing events land in
  the streams the host drains next frame). The ring is an event output, not
  state: it is never rolled back (rollback restores the wheel itself, and a
  re-drain re-collects — the resim discipline, receipt-tested).
  `PendingCrossings` counts scheduled-but-not-due.
- **The wheel is lazy**: statless boards no longer pay its 8,832 B fixed
  cost (`IntrinsicsOnly_ExactBill` stays 8 B) — it materializes on first
  `ScheduleCrossing`/`AddModifier`/`ExpireModifiers`, and `LoadState`'s
  rebuild allocates only when something is actually pending (the
  load-must-return-to-baseline memory pin keeps holding). Modifier ordering,
  cascade, bulk drain and rollback semantics are unchanged (WheelTests
  untouched and green).

## Tests (Sv.Tests total 418)

- `CrossingTests.ScheduledTick_IsTheFirstEagerTickThatReachesTheThreshold`
  — **law L8**: 200 random rising rules/values/thresholds; the bisection's
  tick equals the eager first-satisfied tick; the scheduled crossing fires at
  exactly that tick and never one earlier.
- `Crossings_DrainInTickIdOrder_AlongsideExpiries` — crossings interleave
  with modifier expiries in (tick, id) order; same-tick crossings sort by id.
- `Rollback_RestoresCrossings_AndTheRingIsAnOutput` — rollback restores
  pending crossings; the ring does not roll back; a re-drain re-collects.
- `TakeDueCrossings_TooSmallSpan_Throws`.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (418 / 7 / 27; 2× repeat stable).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok;
  `wheel-expiry` 140.2 µs Expire / 348.1 µs tick / 262,919 pending — within
  noise of atoms 94/95 despite the 32 → 40 B entry (recorded honestly: +8 B
  per pending entry, +2 MiB at the bench's 262 k pending, +8 MiB at FF-1M's
  1 M pending).
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@1efe84a` + this atom. Bus checked before and after (inbox empty).
