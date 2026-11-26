# Issue 0091 — Atom 83 (roadmap 3.2): one clock, hierarchical timing wheel, `Expire` as a declared step-3 sub-step

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 3, atom 3.2
(§4.6, F25, §9 deletes the expiry heap). Labels: `ai`, `area:core`, `kind:feat`.

## Contract note (bus-gated wording; owner-instructed implementation)

Roadmap §8 proposal 7 (batch v1.2) amends clause 7: `Expire` sub-step; one `Tick`.
The agent bus does not exist on this host, so the message cannot be posted and
`contract/` stays vendored at v1.0.3, untouched. Drafted bus message:

> sv proposes clause 7 amendment (v1.2 batch, proposal 7): the frozen frame
> order's step 3 becomes "expire modifiers whose tick has arrived (`Expire(tick)`,
> one clock: the frame index), then stat recalc + broadcast". Expiry stops being
> an implicit host choice (F25). No clause-7 wording for other steps changes.
> sv implements it now behind its own frame driver; tl/kernels see no surface
> change (`ExpireModifiers` is unchanged sv API).

## Changes

- **`src/Sv/Board.Wheel.cs`** (new partial) — the timing wheel replaces the
  expiry heap:
  - 4 levels × 256 buckets (`wheelHeads`/`wheelTails`, 8 KiB total, allocated
    once, never moved), holding intrusive chains of `WheelEntry { Record, Id,
    Next }` from an arena pool with a free list (the records-pool discipline:
    dense-index high-water shrink with the same 75% hysteresis at
    `Recalculate`).
  - Insert O(1): delay < 256 → L0, < 65 536 → L1, < 2²⁴ → L2, else L3
    (delays ≥ 2³² clamp to the L3 last bucket and re-clamp on every cascade —
    the classic different-lap-same-bucket hazard is handled by recomputing each
    entry's delay from its record's absolute `Expiry` at every cascade).
    Entries with `Expiry ≤ wheelTick` sort into an `overdue` chain by
    (tick, id).
  - Advance is O(due): per tick the due L0 bucket fires; at L0 wrap the crossed
    L1 bucket cascades (L2/L3 at their wraps). Bucket chains are id-ascending
    by construction (cascades at round boundaries precede direct inserts), so
    natural drain order **is** the heap's global (tick, id) order — no sort on
    the frame path.
  - Pathological jumps (≥ 2²⁴ ticks in one call, e.g.
      `ExpireModifiers(long.MaxValue)`) bulk-drain all buckets, re-bucket
    survivors, and merge-sort the due set by (tick, id) on transient native
    scratch — bounded work, deterministic, no managed allocation.
  - Lazy staleness like the heap: entries for already-removed modifiers stay
    chained and are skipped by the (record, id) check when they surface.
- **`src/Sv/Board.Stats.cs`** — `ExpiryEntry`, `expiries`, `PushExpiry`,
  `PopExpiry`, `Before`, `ShrinkExpiries` deleted; `AddModifier` inserts into
  the wheel; `ExpireModifiers` drives the advance; `PendingExpiries` counts
  live wheel entries; `VisitStats` drops the persisted-heap block (the wheel is
  rebuilt from the records at load — same (tick, id) semantics by sorted
  re-insertion), so saves shrink; the board state format bumps v10 → v11
  (`SetTableFormatVersion`, folded into `StateShape` as before — old saves
  shape-mismatch, same discipline as v9 → v10).
- **`src/Sv/Board.Keyframe.cs`** — keyframe restore no longer pushes expiries
  per record (reverse-id order would break the chain invariant); it rebuilds
  the wheel once from the restored records, sorted by (tick, id).
- **`src/Sv/Board.Undo.cs` / `UndoLog.cs`** — the undo frame carries the wheel
  scalars (`WheelTick`, entry count/cap/free/live, overdue head) and the entry
  pool pointer; `UndoArrayId.Expiries` becomes `WheelEntries`; heads/tails are
  fixed arrays, byte-noted per mutation and range-noted on rebuild.
- **`src/Sv.Compose/Compose.cs`** — `Game.Tick` (the one clock, the frame
  index, advanced at the input edge); `FrameRunner.Run` calls
  `Board.ExpireModifiers(Game.Tick)` immediately before `Recalculate` — the
  declared step-3 sub-step (F25 closed: expiry is no longer a host choice).

## Tests

- New `WheelTests`:
  - same-tick (tick, id) order — two boards whose modifier add order differs
    interleave but expire identically: save bytes after expiry are equal;
  - cascade correctness across every level (delays 1 … 2²⁵) against the
    sorted-oracle list, including same-bucket different-lap entries (the entry
    65 536 ticks later must not fire early);
  - overdue inserts fire on the next call, in (tick, id) order;
  - huge jumps (`long.MaxValue`) drain everything deterministically;
  - lazy staleness: `RemoveSource`/`RemoveModifier`/slot release between insert
    and due — skip, no ghost removals, `PendingExpiries` returns to zero;
  - undo: expire inside a window, rollback — pending count, values and save
    bytes restored;
  - save/load round trip: pending expiries survive as rebuilds — expiring
    after load equals expiring without load (counts, values, bytes);
  - keyframe restore: a keyframe taken with pending expiries restores them.
- Existing `ModifierLifetimeTests` (the random oracle program),
  `ModifierPoolTests`, `UndoTests`, `CodecTests`, digest pins — unchanged
  expectations, green as-is.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **377/377 passed** (368 + 9 new
  `WheelTests`); 2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 377/377,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (the composed
  frame now expires at step 3 and `Game.Tick` rewinds on rollback — resim
  digests unchanged).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  ff1m p50 2,909 µs (baseline 2,918), **keyframe 405 µs (baseline 423 —
  saves no longer persist the heap array; snapshot 120 MiB from 152)**,
  rollback-resim 569 µs (619), spawn1m 5.38 ms, bulkpass 1.30×, alloc 0 B
  everywhere. Board +9 MiB at FF-1M: the `Wheel` back-index on modifier
  records (+8 B × ~1M) and 20 B wheel entries — recorded honestly.
- O(due) receipt, interim: ff1m holds ~262k pending armor expiries with
  delays spanning L1/L2 and expires 8/frame at unchanged p50 — expiry cost
  does not scale with pending. The dedicated expiry microbench scenario is
  deferred to the next session (pickup point).
- Bugs caught during the atom (all now regression-covered): the round-boundary
  cascade off-by-one (an entry landing exactly on `wheelTick` missed its tick),
  the overdue insert not setting the new entry's `Prev`, the L3 re-clamp
  self-cycle in single-pass cascade, mid-drain scratch realloc discarding
  stored indices (AV under ff1m), and the 5× keyframe regression from
  full-board rebuild on incremental restores — fixed by per-record restore.
- An env-gated wheel validator (`SV_WHEEL_DEBUG=1`, checked before every
  `ExpireModifiers` when set) walks every chain invariant; it found the first
  corruption in seconds and stays as a debug facility.
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched; digest pin unchanged (the wheel is
  digest-invisible; values bit-identical).

## Provenance

`main@a310730` + this atom. Next: 3.3 (renumber without re-resolve; mask
windows at any offset).

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; the bus message
is drafted above per AGENTS.md discipline.
