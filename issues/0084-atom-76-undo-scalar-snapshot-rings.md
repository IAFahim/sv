# Issue 0084 — Atom 76 (fix, roadmap 5.3 follow-up): undo windows never log class-field addresses

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) — fix to the atom 74
composed rollback ([issues/0082](issues/0082-atom-74-composed-rollback.md)).
Labels: `ai`, `area:core`, `kind:feat`. Contract: none.

## Changes

- **The defect**: the atom 74 InputState/EventStreams undo hooks noted the addresses
  of *class instance fields* (`ringHead`, `tickCounter`, `replayCursor`, `active`)
  under momentary `fixed` pinning. The undo log stores that address in native memory
  and replays writes through it later — but the GC may relocate the owning object in
  between, leaving the replay writing through a stale address into reused heap. This
  is a correctness bug by construction and explains the `ComposeRollbackTests`
  flakes observed on otherwise pristine main (~1 in 8 runs during atom 75's
  verification, GC-timing dependent).
- **The fix** (`src/Sv/InputState.cs`, `src/Sv/EventStreams.cs`): the pinned scalar
  notes are gone; each component keeps GC-safe managed snapshot rings instead —
  `ringHead`/`tickCounter`/`replayCursor` and the streams' `active` half are captured
  at every `BeginUndoFrame` (frame-open mark) and restored in `RollbackTo` by target
  ordinal, mirroring the board's scalar snapshot discipline. Every address the undo
  logs now record targets NativeMemory blocks, which never move. All other notes
  (offsets, peaks, ring slots, button edges) were already native-memory targets and
  are unchanged.

## Tests

No new facts — the existing `ComposeRollbackTests` family is the regression suite
(rollback depths 1..8 byte-compare against per-frame marks, resim bit-identity).
Verified: full suite 333/333 Release and `SV_CHECKED`, NorthStar 27/27, and a
40-run soak of `ComposeRollbackTests` with zero failures.

## Receipts

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test tests/Sv.Tests -c Release` — 333/333; `-p:SV_CHECKED=true` — 333/333.
- `dotnet test tests/Sv.NorthStar -c Release` — 27/27.
- `dotnet run -c Release --project benchmarks/Sv.Bench -- rollback-resim` —
  264.7 µs/cycle (unchanged); `-- keyframe` — 380.3 µs/cycle (unchanged); all
  scenarios ok.
- FMA grep clean; no production comments; `contract/` and `StrictFloatOracle.cs`
  untouched.

## Provenance

`main@50c5879` + this atom. Next: roadmap 6.1 — `Sv.Core` (plan staged).
