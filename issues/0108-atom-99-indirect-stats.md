# Issue 0108 — Atom 99 (roadmap 7.4): indirect stats — a population-wide buff costs O(1)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 7.4 / §4.11 auras.
Labels: `ai`, `area:core`, `kind:feat`. Contract: clause-4 wording (v2.0
batch, drafted unsent in `issues/0097`); implemented ahead of the fold per
owner instruction (bus 0053).

## Changes

- **`Board.BindSourceAnchor(stat, ordinal, anchorSlot)` /
  `ClearSourceAnchor`** — points one declared source of a stat at another
  slot's value of that source stat. The indirect form of a population-wide
  buff: **one `AddModifier` on the anchor reaches every slot's fold** (the
  receipt: the modifier work is O(1); the recalc walks dependents once —
  bounded below by the broadcast itself, which is O(N) by definition).
  Identical outcomes intern to one shared set — a global buff does not
  multiply set count.
- **Resolve order is anchor-first**: `Recalculate` runs a pre-pass
  (`ResolveAnchors`) that resolves a dirty anchor's source pair *before* the
  slot-ascending wave, then — when the anchor's value changed — marks the
  source's dependents dirty on **every** live slot (O(live) marking, the
  same order-determinism as the wave). The per-(slot, stat) resolve body is
  extracted as `ResolvePair` (one code path for wave, anchors, and the
  pre-pass).
- **Anchors ride the binding doctrine** (clause 12): they are not state —
  `InvalidateBindings` (renumber/load) clears them and cross-marks
  dependents; releasing the anchor slot clears them and cross-marks; the
  host rebinds. `SourceAnchorOf` internal accessor for tests.
- The frozen eager oracle moved with the contract: `sourceAnchors` on the
  model, an anchor pre-pass + `ResolveOne` extraction mirroring sv, and the
  release cross-marking.

## Tests (Sv.Tests total 428)

- `GlobalBuff_OneAnchorModifier_ReachesEverySlot` — one modifier on the
  anchor reaches 8 slots' derived stat (15f everywhere, then 17.5f after an
  edit), SetCount 3 (one shared derived set — no per-slot set explosion).
- `AnchorAtAnySlot_IsOrderIndependent` — anchor at slot 0 vs slot 7 with a
  decoy modifier on another slot: identical columns (resolve-order receipt).
- `AnchoredProgram_MatchesEagerOracle` — board vs oracle through add → edit
  → anchor release, values equal at every step.
- `ReleasedAnchor_RevertsToPerSlotReads` and `Renumber_ParksAnchors_BackToPerSlot`
  — both clearings cross-mark; after renumber the host rebinds the parked
  column (atom 91 doctrine) and the anchor reads per-slot again.
- `BindSourceAnchor_Guards` — ordinals, dead anchors, source-less stats.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (428 / 7 / 27).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok
  (the anchor pre-pass is a no-op scan of declared sources when no anchors
  are bound — the hot path is unchanged).
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@083cfb5` + this atom. Bus checked before and after (inbox empty).
Atom 102 (Unity adapter v2) completed in its worktree meanwhile; integration
follows this landing. Atom 101 (Burst) still in flight.
