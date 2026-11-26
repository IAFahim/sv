# Issue 0107 — Atom 98 (roadmap 7.3): history-free canonical fold

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 7.3 / §4.5, **owner
decision 0046** (*"Accept history-free fold (kind, value bits) for v2.0"*).
Labels: `ai`, `area:core`, `kind:feat`. Contract: clause 3 rewording (v2.0
batch, drafted unsent in `issues/0097`); implemented ahead of the fold per
owner instruction (bus 0053).

## Changes

- **The canonical order is `(kind, value bits)`, not `(kind, insertion
  id)`** — `Canonicalize`'s within-kind tiebreak is now the raw `uint` bits
  of the modifier's float (a total order on representations; entries with
  equal keys are equal entries, so ties cannot matter). The fold stays a
  strict, scalar, kind-major left fold — only the within-kind order changed.
- What this buys (§4.5, now true):
  - **The fold is a function of the modifier multiset.** Two peers that
    receive the same buffs in different orders converge bit-exactly — packet
    reordering stops being a desync source. Receipt below.
  - **Identical loadouts intern to one set regardless of history** — the set
    content hash was already id-free; the canonical array now is too, so
    shared loadouts share sets (more interning, fewer sets).
  - Insertion ids shrink to removal handles (already 64-bit
    generation-tagged from atom 92); they no longer order anything.
- The frozen eager oracle moved with the contract (its header permits
  bus-backed changes; 0046 is one): its canonical sort and the random-set
  mirror test both order by value bits. The cross-host digest pin moved
  `0781B61BE6F53640 → A2ACAA69E55F79D8` (fold order changed for identical
  programs — expected, receipted).

## Tests (Sv.Tests total 422)

- `SameMultiset_RandomPermutations_AllSlotsConverge` — the roadmap receipt:
  a random 6-modifier loadout delivered to 16 slots in 16 independent random
  permutations — every slot bit-identical, one shared set (SetCount 2).
- `ModifierOrder_IsHistoryFree_ValuesAndSetsConverge` (replaces
  `InsertionOrder_IsCanonical…`, the old law): the historical
  `(1e8, −1e8, +1)` vs `(+1, 1e8, −1e8)` pair — insertion order used to give
  9 vs 8; both now read the same bits and intern together. Precision loss
  under reorder is gone because there is no reorder.
- `FoldOrder_IsByValueBits_WithinAKind` — pins the actual order: `(+1,
  −1e8, +1e8)` arrives, folds by bits `1, 1e8, −1e8` → 8 exactly.
- `SameMultiset_AnyOrder_InternsToOneSet` (replaces
  `DifferentOrder_DistinctSets`): SetCount 3 → 2.
- `Fold_MatchesStrictFloatOracle_OnRandomSets` mirror re-keyed; digest pin
  re-baked; everything else (keyframes, undo, wheel, fuzz) green unchanged.

## Receipts

Measured on the bus host (decision 0048): i9-14900K, 24C/32T, .NET 10 Release.

- `dotnet build sv.slnx -c Release` — 0 warnings, 0 errors; SV_CHECKED build
  + full retest green (422 / 7 / 27).
- `dotnet run -c Release --project benchmarks/Sv.Bench` — all scenarios ok;
  stat-recalc 24 ns/op, stat-churn 20 ns/op (unchanged — the tiebreak swap
  is the same comparison count; the insertion sort compares bits instead of
  ids).
- FMA grep clean; no production comments; `contract/` and
  `StrictFloatOracle.cs` untouched.

## Provenance

`main@6723d60` + this atom. Bus checked before and after (inbox empty).
Parallel legs in flight: atom 101 (Burst) and atom 102 (Unity adapter v2)
run in isolated worktrees by agents; this atom is single-writer on the fold
core by design.
