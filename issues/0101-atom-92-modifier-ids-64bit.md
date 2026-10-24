# Issue 0101 — Atom 92 (roadmap 1.5): 64-bit generation-tagged modifier ids — the fold can no longer wrap-reorder

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 1.5, fixes F5.
Labels: `ai`, `area:core`, `kind:feat`. Contract: v1.2 batch item 3 (owner
decision 0045: *"Yes — 64-bit generation-tagged ModifierHandle, separate sort
key for derived entries, one SvState format bump."*; relay 0052, correction
0053 — implemented ahead of the fold; drafted wording in `issues/0097`).

## Changes

- **`ModifierHandle` widens to a 64-bit `Id`** — `(generation << 40) |
  counter`: a monotone 40-bit counter per generation (~1.1 T inserts), the
  generation bumping when the counter wraps and on lexicographic keyframe
  merges. Unique for the board's lifetime; sort order is insertion order
  (clause 3). The 32-bit id that wrapped in ~36 min at 1M adds/s — silently
  reordering the canonical fold (F5) — is gone.
- **`ModRecord.Id` is a long** (record 36 → 40 B; raw-bytes state persistence
  follows automatically); the fold's key arrays (`scratchIds`,
  `statScratchIds`) are `long*`; `Canonicalize` orders on longs.
- **Derived entries get their separate sort-key space** (0045): synthetic
  gather entries now key `long.MinValue + k` — always negative, below every
  host id at any id scale, where the old `int.MinValue + k` could collide with
  host traffic in ~36 min of churn.
- **Keyframes merge the id stream lexicographically**: the keyframe persists
  `(generation, counter)`, and the receiver takes the lexicographic max —
  post-fold additions numerically exceed every shipped id, so insertion order
  survives keyframe floods across generations (numerically larger = later,
  always). Full `LoadState` restores the pair verbatim.
- **One `SvState` format bump** (0045): `StateMagic 0x44425653 → 0x44425654`,
  `KeyframeMagic 0x4B46564B → 0x4B46564C`; the shape payload adds
  `insertionGeneration` next to the widened `nextInsertionId`. Pre-bump saves
  and keyframes fail the magic check loudly.
- **Wheel stale-detection ids widen** (`WheelEntry.Id`, `WheelInsert`,
  `RemoveDueRecord`, `LinkWheelEntry`) and the undo mark snapshots the widened
  counter — no narrowing anywhere.
- `nextInsertionId`/`insertionGeneration` are `internal` (test seeding; the
  established internals pattern).

## Tests (total 407)

- `ModifierId64Tests` (4 new): the roadmap's churn receipt — counter seeded at
  `2⁴⁰ − 3`, six adds cross the wrap; ids stay unique, strictly increasing,
  fold value exact, and a pre-wrap handle still removes its modifier.
  Keyframe merge takes the lexicographic max: a generation-2/counter-9
  keyframe onto a generation-0 receiver yields post-fold ids past
  `(2 << 40) | 9`. The bumped magics: fresh states start `'T'`, an `'S'`-magic
  byte is rejected loudly. Derived sources fold before host modifiers with the
  counter near `2⁴⁰` (the separate synthetic key space).
- Updated: `EagerOracle` model ids widen to long (same numeric stream for
  fresh boards — the oracle's `SyntheticIdBase` keeps synthetic-first
  ordering); the state-magic pin (`'S'` → `'T'`) and the record-pool shrink
  bound (36 → 40 B records: 1,142,360 → 1,365,424) move with the format.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **407/407 passed** (403 + 4 new);
  2× repeat stable.
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 407/407,
  0 warnings.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (keyframe 307.6 µs, rollback-resim 355.1 µs, wheel-expiry 115.6 µs Expire,
  renumber1m 5.0 ms + 9.1 µs — within run-to-run noise of atoms 90/91).
- Memory: +4 B per modifier record and +4 B per wheel entry — 1M modifiers
  cost +4 MiB, recorded honestly.
- FMA grep clean; no production comments (XML docs only); `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@1429fa0` + this atom. Bus checked before and after.
