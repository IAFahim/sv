# Issue 0065 — Atom 57 (roadmap 1.7): state integrity — schema-content shape, game header, validate-before-commit

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 1, atom 1.7 — F17, F18.
Labels: `ai`, `area:core`, `area:compose`, `kind:fix`. Contract: none (state format
evolves; states saved before this atom do not load — atom-43 precedent).

## Changes

- **F18** — `Schema.ContentHash` (public, computed once at authoring): XXH64 over a
  canonical little-endian byte stream of the whole schema — every stat's name, base
  bits, and derived sources (stat index + coefficient bits), every intrinsic's name
  and declared bounds. `Board.StateShape` (now internal, computed once in the
  constructor) is XXH64 over `{SetTableFormatVersion, Capacity, statCount,
  intrinsicCount, ContentHash}` — the XOR-counts collision (`(1 stat, 0 intrinsics)`
  vs `(0 stats, 256 intrinsics)`, and same-count/same-capacity schemas with different
  names, bases, or source coefficients) is dead.
- **F17** — `Sv.Compose.Game` writes a header (`{GameMagic, GameShape}`) **first** in
  `SaveState` and validates it **first** in `LoadState`. `GameShape` is XXH64 over
  `{board StateShape, timelineCount, capacity}` — a state saved from a game with a
  different schema or a different timeline count is rejected before the board,
  input, streams, or any game column is touched (previously the mismatch surfaced
  only at `EndRead`, after everything had been overwritten).
- `XxHash64` moved from `Sv.Compose` to `Sv` (one source of truth; namespace lookup
  keeps `Tape` and the north-star hash tests compiling, now against `Sv` internals —
  `Sv.NorthStar` joined the internals list).

## Tests (+4, `StateIntegrityTests`)

- `BoardState_DistinguishesCountSwaps` — F18's own collision example throws.
- `BoardState_DistinguishesNamesBasesAndSources` — renamed stat, changed base,
  changed derived coefficient each reject the load.
- `GameLoad_MismatchedTimelineCount_LeavesGameUnchanged` — the F17 receipt: the load
  throws from the header and the target game's live count and broadcast bits are
  exactly what they were.
- `GameLoad_MismatchedSchema_LeavesGameUnchanged` — same, schema leg.

## Receipts

- `dotnet test sv.slnx -c Release` — 251/27/6 passed; `-p:SV_CHECKED=true` — 251.
- All existing save/load/rollback/tape tests green against the new format
  (same-binary round trips are unaffected).

## Provenance

`main@c7cf0b0` + this atom. The F18 collision pair was constructed and observed
loading (mis-aligned) before the fix; both legs now reject at the header.
