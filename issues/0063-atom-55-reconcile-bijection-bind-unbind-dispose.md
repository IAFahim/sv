# Issue 0063 — Atom 55 (roadmap 1.2): bijective reconcile, exception-safe Bind/Unbind, idempotent Population.Dispose

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 1, atom 1.2 — F4, F6, F7.
Labels: `ai`, `area:adapter`, `kind:fix`. Contract: none.

## Changes

- **F4** — `Population.Reconcile` now proves the **bijection** before rewriting
  anything. The first pass marks each seen `SvSlot` in the permutation array
  (`-1` unseen / `0` seen — no extra memory) and requires every slot to be in
  range, live on the board, and unseen; only then does the count check run, and
  only after both does the rewrite pass start. A duplicated `SvSlot` (ECS clone)
  counterbalanced by a missing binding (destroyed without `Unbind`) — the case
  that used to pass the count check, rewrite the world, and only then make
  `Board.RenumberSlots` throw — now throws with world and board untouched.
- **F6** — `FrentBoard.Bind` is idempotent (a bound, live entity returns its
  existing slot; a bound but stale slot throws instead of aliasing), and
  compensates on failure: if `entity.Add` throws after `Acquire`, the slot is
  released before the exception escapes. `Unbind` validates before acting —
  `Board.Release` (which rejects non-live slots) runs first, so a failed unbind
  leaves the component in place.
- **F7** — `Population.Dispose` is idempotent (disposed flag, the same class of
  fix as atom 39's `Game.Dispose`).
- `Board.IsLive(slot)` added as an internal helper (`Sv.Frent` already sees
  internals).

## Tests (ReconcileAtomicityTests, +5)

- `Reconcile_DuplicateAndMissing_Counterbalanced_ThrowsBeforeRewrite` — the F4
  killer: a cloned `SvSlot` plus a deleted-but-bound entity make the counts match;
  reconcile throws and every surviving `SvSlot` is bit-identical (law L11).
- `Bind_IsIdempotent_ForBoundLiveEntity` — second bind returns the same slot,
  live count unchanged.
- `Bind_FailedAdd_LeavesNoLiveSlot` — bind of a dead entity either binds once or
  throws with **zero** live slots left behind (the leak contract, independent of
  Frent's dead-entity semantics).
- `Unbind_StaleSlot_KeepsTheComponent` — releasing the slot under the component
  first makes `Unbind` throw with the component still attached.
- `Dispose_Twice_FreesOnce` (law L15).

## Receipts

- `dotnet test sv.slnx -c Release` — 245/27/6 passed (240 + 5 new);
  `-p:SV_CHECKED=true` — 245 passed.
- Bench cold-path-only change: `frame-run` 688.0 µs, `ff1m` p50 3,092.4 µs — parity.

## Provenance

`main@788969a` + this atom. F4's repro (counterbalanced duplicate + missing) is the
roadmap's own; it failed the old code's atomicity in exactly the way §3 describes.
