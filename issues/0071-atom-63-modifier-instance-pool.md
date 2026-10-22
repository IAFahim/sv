# Issue 0071 — Atom 63 (roadmap 2.4): modifier instance pool — 32 B records, the `PairMods` pages deleted

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 2, atom 2.4 —
S1. Labels: `ai`, `area:core`, `kind:feat`. Contract: none (ids stay 32-bit;
the 64-bit id bump is roadmap 1.5, bus-first).

## Changes

- **A global record pool replaces per-pair modifier storage** (roadmap §4.2's
  sparse instance pool). Deleted: the `PairMods` struct, the per-pair
  `{Entries, Count, Cap}` 16 B headers and the per-list doubling blocks
  (cap 4 start, `(StatEntry, Id, Source)` triples). In their place:
  - `ModRecord` — 32 B: `Id` (int, unchanged semantics), `Kind`, `Value`,
    `Source`, `Expiry` (long), `Next` (int link). Instances are pool indices;
    the pool grows by doubling and never moves logically (all links are
    indices, so realloc is invisible).
  - A deterministic LIFO free list; freed records keep their `Next` link as
    the free-list link, so an identical operation sequence reuses identical
    indices and saves byte-identically.
  - Per-pair state becomes one `PairState { int Set; int Head; }` (8 B/pair)
    on the existing page system — the set id (atom 62) plus the record-list
    head. Net per pair: 20 B (set 4 + `PairMods` header 16) → 8 B, before any
    modifier exists; each modifier costs a flat 32 B instead of a 64 B
    minimum block.
- `AddModifier` prepends a record (canonical fold order is (kind, id) —
  gather sorts, so list order is irrelevant); `RemoveModifier`,
  `RemoveSource` and `ExpireModifiers` walk and unlink; `ClearSlotStats`
  (release) frees the slot's whole lists. The expiry heap keeps its
  `{Tick, Id, Slot, Stat}` shape (the timing wheel is roadmap 3.2).
- `RenumberSlots`: record instances are pool-global and slot-independent —
  only `PairState` rides along (already does); no modifier storage surgery.
- Board state format **v6**: the pool (`recordCount`, records, `freeHead`)
  and `PairState` pages persist; the per-pair `{Count, Cap, Entries...}`
  blocks are gone from the stream.
- `MemoryBytes`: pair pages 8 B/pair + `recordCap × 32` pool bytes.

## Tests

- Existing coverage carries the semantics: modifier lifetime/oracle tests,
  source removal, expiry order, renumber, save/load round-trips, the eager
  oracle's random programs (canonical order unchanged).
- New pool facts in `ModifierPoolTests`: memory grows ≈ 32 B per modifier
  (not 64 B blocks), remove→add reuses the freed record (byte-identical
  saves across two identical op sequences), release frees the slot's
  records (`PendingExpiries` skip + memory drop), mid-frame save→load
  restores the pool exactly.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **273/273 passed** (269 + 4 new); 2×
  repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 273/273.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok:
  **ff1m p50 2,784 µs / p99 3,876 µs (Δ=128), board 642 → 386 MiB** (the
  16 B/pair `PairMods` header pages die; modifier bytes are now the pool,
  2M records × 32 B = 64 MiB + 8 B/pair `PairState`), **snapshot 368 → 336
  MiB, rss 734 → 447 MiB**, stat-churn 26 → 21 ns/op, stat-recalc 29 ns/op,
  alloc 0 B everywhere; no scenario regressed.
- FMA grep clean; no production comments.

## Provenance

`main@f4ccc8d` + this atom. Next: 2.5 — owned columns and mirrors allocated
only on first read or bind.

Bus note: `/home/i/GitHub/agent-bus` does not exist on this host
(`/home/os`), so the pre/post-atom bus checks could not run; recorded here per
AGENTS.md discipline.
