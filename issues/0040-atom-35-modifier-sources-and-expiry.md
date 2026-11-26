# Issue 0040 — Atom 35: modifier sources + timed expiry (`RemoveSource`, `ExpireModifiers`)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #40 (pending — see receipts). Labels: `ai`, `area:core`,
`kind:feat`, `contract:proposed`. Contract: bus 0036 amendment 3 (clause 3, additive)
— vendored contract stays v1.0.3 until ack.

## Problem

Every game with buffs needs "this buff lasts N ticks" and "unequip removes everything
the item granted". Hosts had to keep handle lists per item and per timer themselves,
outside sv's deterministic order.

## Shape

- `AddModifier(slot, stat, modifier, int source, long expiresAtTick = long.MaxValue)`;
  the existing 3-argument overload is `source 0, never expires`.
- Per-pair modifier block grows a third lane: entries | ids | sources (one native
  allocation, 16 B per modifier).
- `RemoveSource(slot, source) -> int`: removes every modifier of that source on the
  slot across all stats; each touched pair is re-queued once.
- `ExpireModifiers(tick) -> int`: a native binary min-heap keyed by
  (expiresAtTick, insertion id) — deterministic order; pops while top tick <= tick.
  Stale entries (removed by handle/source, slot released, slot reused) are skipped:
  insertion ids are never reused, so an id match is exact. `PendingExpiries` exposes
  the heap size (stale entries included until their tick).
- `RenumberSlots` remaps the heap's slots (dead -> -1, skipped on pop); heap order is
  slot-independent.
- All changes are structural (clause 7): visible at the next `Recalculate`.

## Receipts

- `ModifierLifetimeTests`:
  - exact scenario: tick/id order, stale entry skipped, pending count, source removal
    counts, folded values;
  - independent oracle: 2 seeds x 150 ticks x 80 ops (acquire/release/add timed or
    permanent with 4 sources/remove-by-handle/RemoveSource) with a reversal renumber
    every 40 ticks — `ExpireModifiers` and `RemoveSource` return exactly the model's
    removal counts and every live slot's output matches the StrictFloat oracle fold;
  - warm add-timed + RemoveSource + ExpireModifiers + Recalculate: 0 managed bytes
    over 200 ticks.
- Tests default: 166 + 21 + 6 passed, 0 failed. SV_CHECKED: 168 + 21 + 6 passed.
- no-FMA grep clean.
