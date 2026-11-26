# Issue 0034 — Atom 29: paged pair storage — memory follows touched slots, not capacity

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #34 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:perf`. Implements the chunked/lazy direction of
[issues/0022](0022-design-capacity-memory.md) under the user's in-session direction
(2026-10-10); bus ruling 0033 was never answered — recorded on the bus with this atom.

## Problem

`InitStats` eagerly allocated `statCount x capacity` of `PairMods` (24 B) + set (4 B)
+ position (4 B) + queued (1 B): a 1M x 3-stat board committed ~110 MiB before a
single slot was acquired (atom 21's receipt), and every pair paid modifier-list
bookkeeping whether or not it ever held a modifier.

## Shape

- Slots are grouped in 4096-slot pages. Two page tables, allocated per page on first
  need:
  - metadata (`PairMeta`: set, member position, queued — 12 B/pair) on first
    `Acquire` of a slot in the page;
  - modifier lists (`PairMods`: 16 B/pair) on first `AddModifier` to a slot in the
    page.
- `PairMods` shrinks 24 -> 16 B: entries and ids live in one block (`Ids` is the tail
  after `Cap` entries).
- sv-owned output columns stay eager (clause 4 broadcast targets; `OutputPtr`
  promises a stable capacity-sized column).
- `RenumberSlots` builds fresh page tables for the targets and moves list pointers
  over. Fixes a native leak: released slots kept their modifier buffers (`Count = 0`)
  and the old renumber overwrote those pointers without freeing them; they are now
  freed with the old pages.
- `MemoryBytes` walks touched pages (`PairBytes`); the doc now says so.
- Determinism unchanged: same queue order, same set resolution, same folds.

## Receipts

Scratch harness, 1M x 3-stat board, `MemoryBytes` old -> new:
- empty: 109.8 -> 15.4 MiB (7.1x); `Create` 13.3 -> 3.1 ms
- 10k live: 110.1 -> 16.1 MiB (6.8x)
- 1M live, modifier-free: 137.8 -> 77.8 MiB; first recalc 25.5 -> 19.1 ms
- 1M live + 100k modifiers: 142.8 -> 128.8 MiB; recalc 8.0 -> 6.0 ms

Tests: `MemoryBytesTests.MillionSlotBoard_BillScalesWithTouchedPages` pins empty
< 20 MiB, +10k live < 1 MiB, full in (40, 100) MiB. `ModifierOracleTests` (independent
oracle, renumber every 30 frames) green on the paged layout. Bench unchanged within
noise (stat-recalc 19 ns, stat-churn 14 ns, frame-run 672 us).
Tests default: 152 + 21 + 6 passed, 0 failed. SV_CHECKED: 154 + 21 + 6 passed.
no-FMA grep clean.
