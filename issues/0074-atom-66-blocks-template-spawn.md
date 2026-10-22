# Issue 0074 — Atom 66 (roadmap 2.7): blocks + template spawn — O(N + blocks·S) stat identity

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.7
(§4.4). Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **Stat identity becomes a `uint` block id per slot.** Today every pair
  (slot × stat) stores its own set id in a paged `PairState {Set, Head}`
  (8 B/pair — 128 MiB at FF-1M). Now each slot's whole tuple of set ids is
  interned as a **block** (`BlockTable`: open-addressed like the set table,
  refcounted, freelist reuse, backward-shift bucket deletion) and the pair
  pages become per-slot `SlotState {Block, Head}` (8 B/**slot**, −120 MiB
  at FF-1M). Most of a million entities share a few thousand tuples; the
  worst case (all unique) costs per-pair set ids + 4 B/slot, never meaningfully
  worse than today. Blocks store set ids only — values stay
  `Computed(set)`, immutable per set.
- **Changing one stat re-interns the tuple** (O(S), usually warm): the
  resolve loop builds the new tuple in scratch, interns it (refs first),
  then drops the old block's ref — shared sets never hit refcount 0
  transiently. Set refcounts move from pairs to blocks; the reclamation
  law is unchanged (a set frees when its last block leaves, L14).
- **Modifier records become entity-chained.** `ModRecord` gains `Slot`
  and `Stat` (32 → 40 B): one head per slot (in `SlotState`) instead of
  one per pair; walks filter by `Stat`. Renumber remaps record slots by
  walking live chains.
- **Expiry heap entries pack to 16 B** (`{Tick, Record, Id}` — slot/stat
  come from the record; a stale entry is one whose record id no longer
  matches) **and the heap exact-fit shrinks** (to count + 12.5% headroom,
  gated until the live count falls to 75% of capacity — regrowth stays ×2,
  so the hysteresis band keeps realloc churn off the frame path). FF-1M's
  heap stops hoarding 96 MiB after a doubling. `(tick, insertion id)`
  processing order and `PendingExpiries` semantics are unchanged.
- **Template spawn.** `Spawn(templateSlot, count, int* slots)` spawns n
  entities from a resolved slot: n block-id writes (one refcount add per
  block, `AddRefMany`), materialized columns and mirrors filled directly
  from the template's block values, rows marked in `AcquiredWords` (the
  clause-7 spawn window) — no dirty pairs, no resolution, values final.
  Recycled rows keep their changed-mask doctrine (spawn visibility is
  acquired-words, atom 56). An unresolved template falls back to plain
  `Acquire` semantics. Exhaustion is checked before any mutation.
- Board state format **v9** (slot pages, block table, records with
  slot/stat, 16 B heap entries).
- `MemoryBytes` follows the new layout.

## Tests

- New `BlockTests`: spawn fills every materialized surface with the
  template's values and marks acquired words only; spawned entities share
  sets (`SetCount` flat across a 1k spawn); a modifier on one spawned
  entity re-interns its block alone (siblings unchanged); releasing the
  last user of a block frees its sets; save→load round-trips blocks
  byte-stably; renumber with blocks and records keeps values bit-exact;
  block churn (spawn → modify → release → spawn) stays deterministic.
- Existing suites (eager oracle, digests, reclamation, rollback) carry
  the value laws — the atom-65 cross-host digest pin must not move.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **290/290 passed** (282 + 8
  new); 3× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 290/290.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios
  ok: new **spawn1m 3.33 ms/spawn** (1,048,576 entities from one
  template, 16 stats, bound column + mirror — budget ≤ 10 ms);
  **ff1m board 318 → 154 MiB** (≤ 160 receipt), snapshot 272 → 152 MiB,
  rss 383 → 260 MiB; ff1m p50 2,966 µs (block indirection on the Δ path,
  +3%); stat-churn 21 ns/op unchanged; alloc 0 B everywhere.
- FMA grep clean; no production comments.
- Spawn semantics note: the template's modifiers are folded into the
  shared set, not copied as records — a spawned entity's first re-resolve
  recomputes from its own modifiers alone (template-instanced buffs are
  the roadmap's §4.5 follow-up, bus-gated).

## Provenance

`main@c1d080c` + this atom. Next: 2.8 — allocator abstraction; O(1)
exact `MemoryBytes`; shrink high-water capacity at structural boundaries.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded
here per AGENTS.md discipline.
