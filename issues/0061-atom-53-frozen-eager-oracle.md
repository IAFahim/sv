# Issue 0061 — Atom 53 (roadmap 0.3): the eager oracle, frozen

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) phase 0, atom 0.3 — phase 0 is
now complete. `tests/Sv.Tests/EagerOracle.cs` + `EagerOracleTests.cs`. Labels: `ai`,
`area:core`, `kind:test`. Contract: none.

## What is frozen

`EagerOracle` — an independent, managed model written from the contract text and the
atom receipts, **not** by calling production:

- `FrameStep` — one eager intrinsics pass: enabled rows take the floor-first clamp of
  the saturating add (`clamp((long)v + δ)`), disabled rows untouched with applied 0,
  uniform or per-row deltas, nullable bounds mirroring `Clamp.Columns`. This is law
  L7's reference: phase 4's `ClampShift` drift must stay bit-equal to it.
- `Fold` — the clause-3 canonical fold, verbatim operation order: `+Added` in
  (kind, id) order, then `× (1 + ΣAdditive)`, then `× (1 + Multi)` per entry.
- `BoundOf`, `CanonicalNaN` — clause 16 / atom 48, restated.
- `EagerModel` — the whole stat surface op for op: insertion-id modifiers, derived
  synthetic entries (`int.MinValue + k`, ahead of every real id), (kind, id)
  canonicalization, set-change propagation to dependents, source values read from the
  last resolution (base while a source pair never resolved), zeroed rows for dead and
  never-resolved pairs, `Acquire` dirtying every pair (production's
  `MarkSlotStatsDirty`), expiry and source-tag removal.

## Tests

- `IntrinsicsFrameStep_MatchesProduction` (6 seeds × 40 rounds): random windows,
  uniform/varying deltas, constant/columns bounds (columns include crossed floors —
  today's oscillation is part of the frozen reference until clause 10 rules), full,
  empty and random mask words. Compares value **and** applied bit-for-bit against
  `Board.RunIntrinsicsChunk`/`RunEnabledChunk` (both delta arities × words variants).
- `RandomProgram_RecalculateMatchesOracle` (5 seeds × 600 ops): add / remove-by-handle
  / remove-source / expire / spawn / despawn / recalculate over 7 stats (4 base, 3
  derived — one derived-of-derived), then compares **every** slot × stat's broadcast
  bits against the model, dead and never-resolved pairs included (must read 0).

## Receipts

- `dotnet test sv.slnx -c Release --filter EagerOracle` — 11/11 passed.
- Full suite 232/27/6 passed (was 221/27/6 — the 11 new facts), again green with
  `-p:SV_CHECKED=true`.
- Two model bugs were caught by the comparison before it agreed — the receipt that
  the oracle is genuinely independent: (a) `Acquire` resolves pairs via
  `MarkSlotStatsDirty` (the model first assumed bare acquires stay unresolved);
  (b) enabled-mask words are relative to the window offset, not absolute.

## Scope notes

- The changed-mask bits are not compared yet — F10 (acquire bits erased by
  `Recalculate`) and F12 (double-resolve marks extra rows) make masks differ from the
  ideal; masks get pinned by atoms 1.4 and 2.3.
- Renumber is not in the generator yet (law L3 lands with phase-2 atoms; existing
  `RenumberSlots` tests cover the permutation mechanics meanwhile).
- The oracle changes only alongside a bus-backed contract bump (clause 10 crossed
  bounds, clause 18 saturating applied), never to chase production.

## Provenance

`main@f569969` + this atom. Phase 0 (0.1 bench, 0.2 fuzz, 0.3 oracle) is complete;
phase 1 starts with issue 0062.
