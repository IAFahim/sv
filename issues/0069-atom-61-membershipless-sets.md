# Issue 0069 — Atom 61 (roadmap 2.2): membershipless sets — member arrays, Pos and the dirty-set queue deleted; dense refill on bind and load

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.2 —
S1; laws L1, L4. Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Sets forget membership** (roadmap §9 deletion: "set values are immutable"). The
  per-set member arrays (+ doubling slack), `PairMeta.Pos` and the dirty-set queue
  are deleted; the refcount introduced by atom 60 is the whole membership. What
  remains per set: identity (stat, hash, content span), the fold cache
  (`Computed`/`Folded` — a set's value is immutable, so the cache never
  invalidates), and the refcount.
- **`Recalculate`**: joining a set is `AddRef`, leaving is `ReleaseRef` (frees at
  zero — no moved-member `Pos` fixup anywhere anymore); a not-yet-folded set is
  folded **inline** and the pair's row broadcast on the spot (the old `continue`
  into the dirty loop is gone). Per processed pair the work is now: gather,
  resolve, at most one refcount swing, at most one fold (first member only), one
  row broadcast.
- **`ClearSlotStats`** (release): `ReleaseRef` per resolved pair — no member-array
  surgery.
- **Dense refill replaces dirty marking**:
  - `BindOutput`/`BindExternal` walk the slot space once and write resolved rows
    into the column **immediately** (write-only: binding freshness and length-fit
    guards run per row; the changed mask is not touched — a bind is not a value
    change). Previously the fill waited for the next `Recalculate`.
  - `LoadState` refills every bound column from the restored rows immediately
    (mirrors were already restored/refilled by their own paths). Previously bound
    stats were dirty-marked and refilled at the next `Recalculate` — which also
    made save→load→resave byte-unstable whenever a column was bound; that
    instability is gone (pinned by a test).
- **`RenumberSlots`**: set ids are slot-independent, so member renumbering is
  deleted outright; metas ride along as `{ Set }`.
- `MemberTotal` sums refcounts; `SetTable.MemoryBytes` drops the member-array and
  dirty-queue terms. Set-table format **v4** (persisted layout changed again;
  v3/v2 saves rejected via the state shape).

## Tests

- `SetRefillTests` (4 facts): bind fills immediately (with and without prior
  state), load refills bound columns immediately **and** save→load→resave is
  byte-identical with a bound column, re-binding leaves `ChangedWords` untouched.
- `MirrorRollbackTests.LoadState_RebroadcastsBoundColumnsFromRestoredState`
  updated to the immediate-refill law (the column holds the restored value right
  after `LoadState`, not the host's interim scribble).
- `SetReclamationTests` moved to the `AddRef`/`ReleaseRef` API — the churn,
  LIFO-reuse and byte-stability facts carry over unchanged.
- `DeadSlotQueueTests` (`MemberTotal` == `LiveCount`), the eager-oracle random
  programs, guard-page fuzz table: all green, unmodified.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **265/265 passed** (261 + 4 new); 3×
  repeat runs stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 265/265.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok, three
  consecutive full-suite runs: ff1m p50 **2,967 / 3,140 / 3,319 µs** vs 3,126–3,134
  baseline (one outlier run at 11,759 µs was environmental — rss/page-cache
  pressure from diagnostic runs, not reproducible); **board 890 → 762 MiB,
  snapshot 560 → 432 MiB, statmirror 25.5 → 19.2 µs**, alloc 0 B everywhere.
- FMA grep clean; no production comments.

## Provenance

`main@847e3c8` + this atom. Next: 2.3 — dirty-stat masks replace the pair queue
and `Queued` (recompute exactly once per pair, topological order).
