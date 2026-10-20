# Issue 0053 — Atom 46: spawn visibility — zeroed rows, acquired-changed bits, cleared masks on renumber (review C-7, C-14)

Filed as GitHub issue #33 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), findings C-7/C-14 —
verified against source before fixing.

## Problem

- **C-7:** `Release` left the slot's output and mirror values; `Acquire` did not clear
  them. A re-acquired slot showed its previous occupant's values until the next
  broadcast, and `MarkChanged` bit-compared new broadcasts against that residue — a new
  entity whose first value equaled the previous occupant's was never flagged, so
  `ChangedWords` consumers missed the spawn.
- **C-14:** `RenumberSlots` left `changed`/`changedTouched` indexed by old slots;
  `ChangedWords` between a renumber and the next `Recalculate` was garbage.

## Shape

- `Release` zeroes the row in every sv-owned output column and every live mirror
  (`ZeroSlotRows`). Dead rows are outside digests/tapes (live-row walks), so
  determinism surfaces are unchanged; snapshot bytes for dead rows become canonical
  zeros (review S-5 direction).
- `Acquire` ORs the slot's bit into every stat's changed mask with touched-list
  bookkeeping (`MarkSlotAcquired`), so a spawn is visible to `ChangedWords` consumers
  from acquisition through the first broadcast (zeroed row ≠ first value).
- `RenumberSlots` clears the changed masks and touched list (`ClearChangedMasks`,
  also extracted into `Recalculate`'s head).

## Receipts

- `SpawnVisibilityTests` (4 facts): released rows read zero before recalculation;
  acquired slots are changed-marked and stay marked through the first broadcast, then
  clear; mirror rows zero at release; renumber leaves all changed words zero.
- Suite: build green; 231 tests (23 + 6 + 202); `StateRollbackTests` bit-identical
  rollback digests unchanged and green.
