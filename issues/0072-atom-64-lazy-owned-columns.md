# Issue 0072 — Atom 64 (roadmap 2.5): lazy owned columns — sv-owned stat columns allocate on first read

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.5.
Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **`svColumns` becomes per-stat lazy.** Today `InitStats` allocates every
  stat's owned column (`statCount × capacity × 4 B`) upfront; a stat nobody
  reads or binds still pays it. Now each stat's column allocates on first
  `OutputPtr`/`Output` (mirrors are already lazy, atom 34), followed by a
  dense refill from the set table — the same write-only walk binds and loads
  use (atom 61), so the column holds `Computed(Set(slot, stat))` for every
  resolved live row exactly as broadcasts would have left it.
- **Broadcasts of unmaterialized stats mark change, not memory.** The owned
  column's invariant is "value == last broadcast computed (0 for
  never-resolved rows)", so for a stat whose column does not exist,
  `BroadcastOne` compares the new computed value against the old set's
  computed value (0 when the pair never resolved) and marks `ChangedWords`
  without writing any column — `ChangedWords` keeps its exact meaning
  ("differs from what the output column held") with zero column bytes.
  External bound columns still compare the column bits they always did.
- `ZeroSlotRows` (release) and `RenumberStats` skip unallocated columns;
  `FillMirrorFromColumns` fills from set values (works materialized or not);
  the tape/digest paths and `Game` readers materialize what they read.
- Board state format **v7**: per-stat column flags (like mirrors) replace the
  flat `statCount × capacity` block — never-read stats cost zero save bytes
  too. A column that exists only locally (snapshot predates the read) is kept
  and refilled, mirroring the mirror precedent.
- `MemoryBytes` counts only allocated columns.

## Tests

- New `LazyColumnTests`: a stat never read costs 0 column bytes (exact
  MemoryBytes delta on first read); `ChangedWords` tracks a stat that was
  never materialized (bits present before any `Output` call, value correct
  after); save→load round-trips mixed materialization byte-stably; renumber
  with one stat unmaterialized leaves the materialized one permuted and the
  other still lazy-and-correct.
- Existing suites carry broadcast/refill/renumber/rollback semantics.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **277/277 passed** (273 + 4 new); 3×
  repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 277/277.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27 (including the atom-48
  digest law: a bound column stays the effective one, the owned view stays
  stale — `Output` exposes that staleness exactly as before).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  **ff1m p50 2,664 µs / p99 4,164 µs (Δ=128), board 386 → 322 MiB, snapshot
  336 → 272 MiB, rss 447 → 383 MiB** (the 8 densely-bound FF-1M stats never
  allocate owned columns), alloc 0 B everywhere; no scenario regressed.
- FMA grep clean; no production comments.

## Provenance

`main@ddf39cb` + this atom. Next: 2.6 — bitmap allocator, lowest free slot
first; delete the free stack.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded here per
AGENTS.md discipline.
