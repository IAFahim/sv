# Issue 0079 — Atom 71 (roadmap 5.1): one serialization codec — `Visit<TCodec>` for save, load, size and hash

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 5, atom 5.1 —
F17. Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **`IStateCodec`** (`src/Sv/StateCodec.cs`): `Header(magic, shape, what)`,
  `Value<T>(ref T)`, `Bytes(void*, long)`, and the static `Reading` flag. Four
  struct codecs implement it — `SaveCodec` (writes), `LoadCodec` (reads, header
  mismatch throws `InvalidDataException` before any field is adopted),
  `SizeCodec` (byte total), `HashCodec` (streaming FNV-1a digest). Struct codecs
  monomorphize per `Visit<TCodec>` instantiation — no boxing, no virtual calls.
- **Every mirrored `Write*`/`Read*` pair is replaced by one `VisitState<TCodec>`**
  describing the layout once: `Board.VisitState` (live words, then
  `VisitStats`: dirty masks, changed/acquired, lazy owned columns and mirrors,
  expiry heap, modifier records, slot pages, `BlockTable.VisitState`,
  `SetTable.VisitState`), `EventStreams.VisitState`, `InputState.VisitState`,
  and the composed `Game.VisitState` (board + input + streams + timelines +
  applied + effects). Load-only reconciliation (realloc-to-streamed-capacity,
  lazy page/column/mirror allocation, stale tracking, restored-binding refill)
  sits inside the same traversal guarded by `TCodec.Reading`; field adoption
  goes through locals so the pre-load value survives for the realloc
  comparisons.
- Byte layout unchanged — every pre-existing save/load/round-trip/truncation
  test passes without modification. Public `SaveState`/`LoadState` signatures
  unchanged.
- Dead code out: `SvState.Expect` deleted (the load codec owns the header
  check); `SvState.Write<T>`/`Read<T>` remain only as codec/`Header`
  primitives. Net diff **−83 lines** (401 deleted, 318 added, of which 134 are
  the new codec file — the per-component write/read duplication roughly
  halves).
- `Sv.Compose` now grants `InternalsVisibleTo` to `Sv.Tests` (the game-level
  codec facts need `Game.VisitState`).

## Tests (`CodecTests`, 4 facts)

- `Board_SaveLoadSave_IsByteIdentical` — the roadmap law
  `Save(Load(Save(s))) == Save(s)` over churned state (dirty masks, expiries,
  records, sets).
- `Board_SizeCodec_MatchesSavedLength` — `SizeCodec.Total` equals the saved
  byte length exactly (the size walk and the save walk are the same code).
- `Board_HashCodec_TracksStateNotIdentity` — two boards running the identical
  program digest equal; one modifier change digests different.
- `Game_SizeCodec_MatchesSavedLength` + game-level `Save(Load(Save))` byte
  identity — the composed visit (board + input + streams + timelines + effects)
  walks one layout.

All 314 pre-existing facts green unmodified (they pin the byte layout, the
truncation behavior, and the rollback semantics across the refactor).

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — **318/318 passed** (314 + 4 new).
- `dotnet build sv.slnx -c Release -p:SV_CHECKED=true` + retest — 318/318.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok,
  ff1m p50 2,917 µs (Δ=128, board 150 MiB, snapshot 152 MiB, spawn1m 3.9 ms),
  alloc 0 B on every row — no serialization-path regression.
- FMA grep clean; no production comments.
- One crash during development, root-caused and fixed before commit: the first
  SetTable conversion dropped the `setCapacity = capacity` adoption, so a later
  growth pass re-used the stale capacity and shrank the arrays (glibc
  "corrupted double-linked list"). The full suite caught it; adoption now
  mirrors the original read path field-for-field.

## Provenance

`main@dc74dde` + this atom. Next: 5.2 — Zobrist digest + page sums, tape v4
(inputs + digest; bodies on demand).
