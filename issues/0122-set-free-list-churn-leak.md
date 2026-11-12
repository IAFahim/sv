# Issue 0122 - Set table never reused freed sets under steady churn (pre-existing leak)

Agent D, branch `proto/d-frame`. Reported by agent C (repro `c-out/repro`). Labels: `ai`, `area:core`, `kind:fix`.

## Findings

1. **C's repro is two effects.** (a) Its add and the remove of the same (slot, source) often land in the
   same canonical batch; remove sorts before add (plan law 5, re-trigger refresh), so the remove is a
   no-op and the add stays live: `Apply` returned 56,000 of 112,000 per round, live modifiers grow by
   250 per tick (sets 5,568 -> 9,997). That is the documented semantic, not a leak: a source removed in
   the tick it was added must be removed next tick. (b) A real leak, below.
2. **Real leak, pre-existing on sv main a0963c8 with the per-call API** (`AddModifier`/`RemoveSource`,
   adds at t, removes at t+1, 250 per tick, 10k slots, ~12 live sets): MemoryBytes 252 KB -> 897 KB
   over 20 x 224 ticks and still doubling. Same on proto/int with `Apply`.
3. **Root cause** `SetTable.CreateSet`: the free list is LIFO and only its head was tested against the
   reclaim horizon (`generation - freedAt[head] > ReclaimHorizon`). Every Recalculate frees sets and
   creates sets in the same generation, so the head is always freed "this generation" and the older,
   reclaimable sets under it were never reached: `setCount` (high-water index) grew by ~1 per change
   (466 -> 14,041 in 40 rounds) with 11-18 live sets; record pool, wheel and blocks were flat.

## Fix (`src/Sv/SetTable.cs`, local)

Reuse the first reclaimable node of the free list instead of only the head. The list is ordered by
`freedAt` descending, so the boundary (last non-reclaimable node) is found by one walk per
(generation, horizon) and kept as a transient cursor; pushes above an empty boundary move it; unlinking
from the middle notes `setFreeNext[prev]` in the undo log; undo restore and state load drop the cursor.
When the head is reclaimable the behaviour is identical to before; it differs only where the old code
leaked. Same rule under an undo window (horizon = frames).

## Receipts

- Churn (next-tick removal), 40 x 224 ticks: memory 185,516 -> 1,637,036 B before; **163,916 B flat**
  after; ms/tick 0.4 -> 0.05-0.1 (no growth). Per-call API on the fixed source: 161,276 B flat over 20 rounds.
- All suites unchanged and green: Sv.Tests 490 (incl. pinned digests), Sv.Portable.Tests 374 (both
  before adding the new test), Sv.NorthStar 27, Sv.Unity.Tests 13.
- New `tests/Sv.Tests/SetChurnTests.cs` (2): MemoryBytes equal across rounds 2-11 (96 ticks each,
  250 add + 250 remove per tick via StatOp/Apply/Recalculate/TakeBroadcast), live sets < 600,
  0 B warm; same flatness inside a 4-frame undo window. Both fail on the old SetTable
  (270,764 -> 362,924 B; 209,324 -> 255,404 B).

## Not done

`CreateSet` still grows capacity when `setCount == setCapacity` even if it then reuses a node (one
extra doubling at most; kept for bit-exactness of state layout).
