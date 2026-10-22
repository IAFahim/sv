# Issue 0073 — Atom 65 (roadmap 2.6): bitmap allocator — lowest free slot first, free stack deleted

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.6.
Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **The free-slot stack is deleted.** Today `Board` carries an
  `int`-per-slot stack (`capacity × 4 B`, −4 MiB at 1M slots) recording free
  slots in LIFO order. But the `live` bitmap already knows which slots are
  free: a slot is free iff its live bit is clear. The stack is redundant
  bookkeeping — `Acquire` now scans `live` for the first word with a zero
  bit (the complement word's `TrailingZeroCount`) and hands out the
  **lowest free slot**.
- **Search hint keeps it amortized O(1).** `freeHint` holds the lowest
  word index that may contain a free bit (invariant: every word below it
  is full). `Acquire` scans from the hint and advances it past words it
  fills; `Release` lowers it to the released slot's word. A spawn burst
  from empty walks the bitmap once total, not once per acquire — the
  atom-2.7 receipt (1M template spawn ≤ 10 ms) depends on this.
- **Denser live set.** LIFO reuse fragmentation — release a high slot,
  reacquire it while lower holes sit empty — is gone: holes are always
  filled lowest-first, so renumber compaction has less to do and the live
  prefix stays packed.
- Board state format **v8**: the `freeTop` int and the `freeTop × int`
  stack block leave the save entirely (the free set is derived from
  `live`, which is already saved); the load path recomputes `freeHint`
  from the restored `live` words. Saves shrink by
  `(1 + liveHoles) × 4 B`.
- `RenumberSlots` rebuilds the hint instead of the stack (the `seen`
  scratch walk that refilled the stack is deleted).
- `MemoryBytes` drops the `capacity × 4` term.

## Tests

- New `BitmapAllocatorTests`: released holes refill lowest-first (release
  {5, 2} of 0..7 → next acquires are 2 then 5 then 8); the live prefix
  stays dense across churn (release scattered slots, reacquire, assert
  every slot below the high-water mark is live); exhaustion still throws
  at exactly `Capacity`; save→load keeps byte-stability with holes; a
  full-board release→reacquire storm reuses slot 0 first.
- `SlotTests.Release_IsImmediateLifoReuse` keeps its meaning — releasing
  the only hole and reacquiring returns that slot under both policies.
- Existing suites carry the rest.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **282/282 passed** (277 + 5
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest —
  282/282.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok: ff1m p50 2,868 µs / p99 3,976 µs (Δ=128), board 322 → **318 MiB**
  (−4 MiB stack), snapshot 272 MiB, rss 381 MiB, alloc 0 B everywhere; no
  scenario regressed.
- FMA grep clean; no production comments.
- Re-pinned receipts of allocator-order dependence: the cross-host digest
  constant (the seeded churn script assigns different slots — lowest-first
  is a pure function of the live bitmap, so still host/ISA-stable) and
  `FrentAdapterTests` rebound slot (299 → 1, the lowest hole).

## Provenance

`main@0cea0d7` + this atom. Next: 2.7 — blocks + template spawn.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
