# Issue 0100 — Atom 91 (roadmap 1.8): release-safe bindings — writes park while stale, length validated at bind

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 1.8, fixes F13.
Labels: `ai`, `area:core`, `kind:feat`. Contract: v1.2 batch item 4 (owner
0043 batch approval; relay 0052, correction 0053 — implemented ahead of the
fold; drafted wording in `issues/0097`).

## Changes

- **`InvalidateBindings` parks the writes in every build** (F13): the recorded
  pointers are dropped (`outputColumn[stat] = null` alongside the existing
  generation mark), so a release build can no longer broadcast into memory the
  board itself declared suspect — `BroadcastOne`'s existing null path routes
  the write to the sv-owned column or nowhere. `OutputColumn` now returns null
  for a parked stat (the pointer is gone, not just flagged). Checked builds
  keep the throw on stat-bounds use. Mirrors refresh from `SourceValue` while
  parked (the external column is suspect; the set table is sv's truth).
- **`BindOutput`/`BindExternal` validate the declared length at bind, in every
  build**: `columnLength >= 0` must satisfy
  `(long)columnLength >= (long)(Capacity - 1) * stride + 1` — the exact
  footprint a broadcast can touch. `-1` remains the documented opt-out; the
  SV_CHECKED per-write check stays as belt.
- **`PendingRebind`** (roadmap 1.8's named surface): how many stats have parked
  bindings awaiting a rebind. Found and fixed a latent init bug while adding
  it: `boundGeneration` was hand-initialized to −1 for *every* stat (never-bound
  and parked were indistinguishable); it is now a zeroed allocation — 0 means
  never-bound, −1 uniquely means parked by `InvalidateBindings`.

## Tests (total 403)

- `BindingParkTests` (4 new): parked writes leave the stale column untouched
  (10f pre-invalidate value survives, rebinding refills 15f into the fresh
  column); `PendingRebind` counts each parked stat and clears per rebind;
  short/stride-short columns rejected at bind in every build (`BindExternal`
  too); and the roadmap's F13 receipt — a Linux guard-page column is
  `mprotect(PROT_NONE)`d after invalidation and a full modifier+Recalculate
  cycle runs through the parked board without touching it (a write through the
  stale pointer would SIGSEGV), then the value reads correctly after re-arming.
- Updated to the decided semantics: `BindingGenerationTests` release branch now
  asserts the parked column keeps its old value while `board.Stat` serves 9f;
  `BoundColumnLengthTests` short-column and stride tests assert bind-time
  rejection (0-length included — the no-live-slots escape hatch is gone by the
  approved wording); two zero-length facts merged into one rejection fact.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **403/403 passed** (400 + 4 new −
  1 merged); 2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 403/403,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok,
  numbers within noise of atom 90's run (ff1m ~3.3 ms, wheel-expiry 126.7 µs
  Expire / 338.2 µs tick, renumber1m 5.4 ms + 9.8 µs).
- FMA grep clean; no production comments (XML docs only); `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@64032b1` + this atom. Bus checked before and after.
