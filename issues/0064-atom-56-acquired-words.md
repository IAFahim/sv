# Issue 0064 — Atom 56 (roadmap 1.4): `AcquiredWords` — `ChangedWords` means value change only

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](issues/0058-roadmap-1m-entities-5us.md) phase 1, atom 1.4 — F10.
Labels: `ai`, `area:core`, `kind:feat`. Contract: additive (new API; `ChangedWords`
narrows to its documented meaning).

## Change

- `Board.AcquiredWords(stat)` — a per-stat acquired mask beside `ChangedWords`:
  bit i of word w marks row `w·64+i`, set by `Acquire`, cleared when the next
  `Recalculate` begins (with the changed masks, and by `RenumberSlots` like atom 46).
  Read it in the clause-7 spawn-to-recalc window (step 2 → step 3).
- `Acquire` no longer touches `ChangedWords`. Before this atom the acquire marks
  were ORed into the still-live previous-frame changed mask (F10's mixing), and a
  stat whose base is 0 never survived the first broadcast (zero → zero), so its
  spawns were invisible everywhere.
- The acquired masks ride `SaveState`/`LoadState` (state stream grows by the
  touched-list + words per stat — same backward-incompatibility class as atom 43's
  format bump; states saved before this atom do not load).
- `MemoryBytes` includes the new masks (the formula's mask term doubles).

## Tests

- `Acquire_MarksAcquiredNotChanged` — pre-recalc: acquired bit set, changed bit
  clear; after the first recalc: changed set by the broadcast (0 → 10), acquired
  cleared; after the second: both clear. (Rewrites atom 46's
  `Acquire_MarksSlotChanged` — that fact's surviving-broadcast half is preserved.)
- `SpawnWithBaseZeroStat_IsVisibleInAcquiredWordsOnly` — the roadmap's receipt: a
  `bonus_armor` with base 0 spawns visibly in `AcquiredWords`, never in
  `ChangedWords`, before and after the recalc; `power` (base 10) marks changed via
  the broadcast as before.
- `AcquiredWords_ClearPerWord_NotWholeMask` — clearing is per touched word across
  recalcs (a fresh spawn doesn't resurrect a cleared word-mate's bit).

## Receipts

- `dotnet test sv.slnx -c Release` — 247/27/6 passed; `-p:SV_CHECKED=true` — 247.
- Bench: `stat-churn` 20 ns/op, `frame-run` parity — the extra mask write is one
  word per (spawn, stat) on the cold acquire path.

## Provenance

`main@f427613` + this atom. F10's two symptoms (mask mixing, base-0 invisibility)
are both pinned by the new facts.
