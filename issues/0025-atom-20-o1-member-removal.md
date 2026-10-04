# Issue 0025 — Atom 20: O(1) set-member removal (per-pair member-position index)

Filed as GitHub issue #25 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:perf`.

## Problem

`SetTable.RemoveMember` was a linear scan over the set's member array, so every
modifier-content migration paid O(set size). Sets are content-deduped and shared —
the base-content set of a popular stat holds every unmodified live slot — so bulk
waves that pull many slots out of one huge set were pathological. Measured on this
box (sv-showcase MillionArmy work, 2026-10-04): 15,625 slots leaving a
1M-member set cost ~6.4 s in one `Recalculate`.

## Shape

- `SetTable.AddMember` now returns the member's position;
  `RemoveMember(set, slot)` is replaced by `RemoveMemberAt(set, position)`,
  which swap-removes and returns the slot relocated into the freed position
  (-1 when the removed member was last).
- `Board` keeps a per-pair `pairPos` column (4 B per slot-stat pair, allocated
  eagerly beside `pairSet`): the invariant is `pairSet[pair] >= 0` implies
  `pairPos[pair]` is that member's index. Migration and `ClearSlotStats`
  fix up the relocated member's position through the pair formula
  (`movedSlot * statCount + stat`).
- `RenumberStats` re-indexes `pairPos` alongside `pairSet`; `RenumberMembers`
  rewrites member values in place, so positions survive renumbering untouched.

## Trade

+4 B native per slot-stat pair (~14% on top of the existing 29 B pair bookkeeping;
on a 1M × 3-stat board, ~12 MB) bought against the O(set) removal. Release-path
cost per migration is two extra int writes. The broadcast loops are unchanged.

## Receipts

- Migration repro (scratch project, Release, this box): add 15,625 modifiers to a
  1M-slot board after the first `Recalculate`, then one `Recalculate` —
  **0.9 ms** (was ~6,431 ms pre-fix, ~7,000x). Broadcast verified: `attack[64]`
  12.50 after `Multi 0.25`.
- Tests default: 136 + 16 + 6 passed, 0 failed. SV_CHECKED: 138 + 16 + 6
  passed, 0 failed (`SetMemberPositionTests`: bulk migration off a shared set,
  churn/release/re-acquire sweep against the `Stat()` fold oracle, interleaved
  group swaps).
- NoAllocationTests unchanged green — removal path stays unmanaged.
- no-FMA grep clean.
