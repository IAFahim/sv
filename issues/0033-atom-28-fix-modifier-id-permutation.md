# Issue 0033 — Atom 28: fix — `Recalculate` permuted stored modifier ids, `RemoveModifier` removed the wrong modifier

Filed as GitHub issue #33 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:bug`, `severity:high`.

## Problem

`Recalculate` (and `Board.Stat`) copied a pair's entries into `scratch` and then called
`Canonicalize(scratch, mods.Ids, count)` — sorting the *copy* of the entries but the
*live* id array in place. After any recalculation where canonical order (op-kind, then
insertion id) differed from storage order, `Ids[i]` no longer described `Entries[i]`.
The next `RemoveModifier(handle)` found the handle's id at the wrong index and removed
a different modifier; later folds also canonicalized with the wrong ids (clause-3
order within a kind).

Minimal repro (now `RemoveAfterRecalc_RemovesTheHandlesOwnModifier`): base 10; add
Multi 1.0 (id 1), add Added 5 (id 2); recalc -> 30 (correct). Remove id 1 (the
Multi); recalc -> expected 15, old code produced 20 (it removed the Added 5).

Present since atom 2. Not caught because every existing stats test either removed
before any recalc, or compared the board against itself (`Board.Stat` shares the same
corrupted storage). The north-star scenario never removes a modifier after a
canonical reorder, so its baked digests were unaffected.

## Shape

- `scratchIds` buffer beside `scratch`; `Gather` copies entries **and ids**, and
  `Canonicalize` sorts the two copies. Stored `Entries`/`Ids` stay in insertion
  pairing.

## Receipts

- New `ModifierOracleTests` — an independent model (own modifier lists, StrictFloat
  oracle fold in canonical (kind, seq) order, no board reads): the minimal repro plus
  3 seeds x 120 frames x 200 ops of acquire/release/add/remove, with a full reversal
  `RenumberSlots` every 30 frames. All 4 fail on the pre-fix code (verified by
  stash: e.g. "frame 1 slot 11 stat 1: expected 350 got 174.5625"), all pass after.
- North-star `MigrationTests` and tape tests unchanged and green.
- Tests default: 152 + 21 + 6 passed, 0 failed. SV_CHECKED: 154 + 21 + 6 passed.
- no-FMA grep clean.
