# Issue 0041 — Atom 36: changed masks — `Board.ChangedWords(stat)`

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #41 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 4 (clause 4,
additive) — vendored contract stays v1.0.3 until ack.

## Problem

After `Recalculate`, hosts that react to stat changes (UI, threshold events, derived
caches) had to diff whole columns themselves — O(capacity) per stat per frame even
when one row changed.

## Shape

- `ulong* ChangedWords(StatId stat)`: `LiveWordCount` words; bit i of word w is set
  iff row w*64+i was broadcast a value whose **bits** differ from what its output
  column held before. Valid until the next `Recalculate`. It is the clause-5 word-span
  shape, so it feeds `RunEnabledChunk(value, delta, clamp, words, wordCount, 0, ...)`
  and the emitters directly.
- Set in both broadcast paths (`BroadcastOne`, full-set) by comparing old and new
  bits before the store (external columns compared through their stride).
- Cleared lazily: `Recalculate` zeroes only the words it set last time (touched-word
  list), so the cost is O(changes), not O(capacity).
- A bind rebroadcast compares against the new column's prior content (documented by
  the "differs from what the column held" definition).

## Receipts

- `ChangedMaskTests`: 2 seeds x 100 frames (acquire/release, timed modifiers,
  RemoveSource, ExpireModifiers, reversal renumber at 40, stride-2 external rebind at
  70) — after every `Recalculate`, for every row of both stats, the mask bit equals
  "bits differ from the pre-recalc snapshot", padding words stay zero. A run-walk
  driven by the mask touches exactly the two changed rows.
- Bench: stat-recalc 19 -> 22 ns/op, stat-churn 14 -> 17 ns/op (the extra read +
  compare); frame-run unchanged (700 us).
- Tests default: 169 + 21 + 6 passed, 0 failed. SV_CHECKED: 171 + 21 + 6 passed.
- no-FMA grep clean.
