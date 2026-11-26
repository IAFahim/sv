# Issue 0028 — Atom 23: join-only broadcast — Recalculate is O(changed pairs), not O(set size)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #28 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:perf`.

## Problem

A set's content is immutable (content-deduped at `SetTable.Resolve`) and the stat base
is fixed at schema build, so a set's folded value never changes after creation. Yet
`Recalculate` marked a set dirty whenever any pair joined it and then re-folded and
re-broadcast the set's entire member list. The common buff-expiry pattern — one entity
loses its last modifier and rejoins the "no modifiers" set shared by the whole
population — rewrote every row of that stat: O(population) per expiry frame.

Second, latent: `BindOutput`/`BindExternal` did not refill the new column. A freshly
bound column held garbage for every slot whose set was never touched again.

## Shape

- `SetTable` tracks `setFolded`; `SetComputed` sets it. Fold runs once per set lifetime.
- `Recalculate`, per dequeued pair: a not-yet-folded set is queued for the full
  fold + broadcast (members are only this frame's joiners); a folded set gets a single
  `BroadcastOne` write of the cached value for that slot (same binding guards).
- `BindOutput`/`BindExternal` queue every populated set of the stat
  (`SetTable.MarkStatDirty`) — the next `Recalculate` rebroadcasts the full column
  from cached values. Cold path, O(sets of the stat + members).
- Values bit-identical: same fold, same inputs; only redundant writes are dropped.

## Receipts

- New `JoinBroadcastTests`: rejoining a 4096-member set writes exactly one row
  (sentinel survives elsewhere); bind refills on next recalc; randomized churn
  (3 seeds x 200 frames x 40 ops of acquire/release/add/remove) matches `Board.Stat`
  bit-exactly for every live slot and stat. The first two fail on the pre-fix code
  (verified via stash), the third passes on both.
- New bench `stat-churn` (100k population, add+remove on a modifier-free slot):
  30,818 ns/op -> 13 ns/op (~2,400x). `stat-recalc`: 116 -> 19 ns/op (fold skipped
  for existing sets).
- North-star `MigrationTests` baked digests unchanged.
- Tests default: 144 + 16 + 6 passed, 0 failed. SV_CHECKED: 146 + 16 + 6 passed.
- no-FMA grep clean.
