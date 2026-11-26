# Issue 0075 — Atom 67 (roadmap 2.8): tracked allocator, O(1) exact `MemoryBytes`, high-water shrink

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.8
(§4.10, S11). Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **`NativeArena` — the allocator abstraction.** One instance per board:
  `Alloc/AllocZeroed/Realloc/Free` wrappers over `NativeMemory` that keep
  an exact running `Live` byte total (callers pass counts, so frees and
  shrinks account exactly — no per-allocation headers). `Board`, `SetTable`
  and `BlockTable` route every native allocation through it; the static
  per-file `Alloc/Realloc` helpers are deleted.
- **`MemoryBytes` is O(1) and exact** — it returns `arena.Live` instead of
  re-deriving a formula (which already drifted: it omitted `statScratch`
  and padding). `SlotStateBytes`, `OwnedColumnBytes`, `MirrorBytes`,
  `SetTable.MemoryBytes` and `BlockTable.MemoryBytes` die with the formula.
  A fully released board reports zero; a materialized column moves the
  number by exactly `capacity × 4 B`.
- **High-water shrink at structural boundaries** (S11 — the pair queue
  half died with atom 62; this closes the rest):
  - `Recalculate` end: the modifier record pool's slack (`recordCap` beyond
    `recordCount` — after a mass-spawn burst the doubling leaves up to
    2× hanging) and the dirty-touched list shrink to live + 12.5%
    headroom, gated until live falls to 75% of capacity (hysteresis; the
    expiry heap already shrinks this way since atom 66).
  - `EventStreams.Swap`: per-buffer peak watermarks halve each swap and
    arenas shrink against the decayed peak only — a burst frame's arena
    is not reclaimed under the following quiet frames, so bursty
    workloads do not thrash realloc.
  - Persisted capacities (set/block table) are untouched — restore-exact
    saves keep their byte-stability law.
- Save format unchanged (v9): only in-memory capacities move.

## Tests

- New `ShrinkTests`: a 100k-modifier burst then removal shrinks the record
  pool at the next `Recalculate` (MemoryBytes drops to near baseline);
  1M acquires release the dirty-touched slack at the first `Recalculate`;
  an event wave + quiet swaps shrink the arena; `LoadState` of a small
  state frees excess slot pages and `MemoryBytes` returns to the fresh
  baseline exactly.
- Existing exact-delta suites (`LazyColumnTests`, `MemoryBytesTests`)
  carry the arena's exactness.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **294/294 passed** (290 + 4 new);
  3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 294/294.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok: ff1m board 154 → **150 MiB**, p50 2,835 µs, rss 257 MiB; spawn1m
  3.16 ms; alloc 0 B everywhere.
- FMA grep clean; no production comments.
- Receipt of the shrink exactness: a 32,769-modifier burst's record-pool
  slack is reclaimed at the next `Recalculate` to within bounded
  first-touch growth (assert 1.13–1.14 MiB of a 1.14 MiB max); 65,536
  acquires release exactly `(65536 − 64) × 4 B` of dirty-touched slack;
  `LoadState` of a page-free state returns `MemoryBytes` to the fresh
  baseline exactly.
- Peak-decay tuning receipt: a per-swap halving thrashed realloc on
  period-2 burst workloads (fused-tick +12%); decaying the watermark
  6.25%/swap keeps recurring bursts resident while quiet stretches still
  reclaim — fused-tick 790–807 µs (atom 66: 816–818).

## Provenance

`main@faec5e1` + this atom. Next: phase 3 (O(Δ) frame) — 3.1 onward.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
