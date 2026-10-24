# Atom 100 (roadmap 7.5): `Cap` modifier kind — full-fat caps fold after `Multi`

## Changes

- `src/Sv/Modifier.cs` — `ModifierKind.Cap = 3` joins the enum (fourth op kind, §4.11);
  the `Modifier` constructor accepts it (finite value still required).
- `src/Sv/Board.Stats.cs` — `Fold` gains a fourth pass after `Multi`: the folded value
  takes the cap with the ceiling idiom `value > cap ? cap : value` (the same
  ternary-select form the total clamp uses; NaN comparisons are false, so a NaN fold
  passes through to `CanonicalNaN` instead of being silently capped). The canonical
  order `(kind, value bits)` needs no change: kind 3 sorts last, so caps apply to the
  post-multi value by construction, and `min` over caps is commutative and associative
  — the fold stays a function of the modifier multiset (history-free property of atom
  98 carries over). Both call sites — the `Recalculate` set fold and the on-demand
  `Stat(slot, stat)` read — run the same `Fold`, so caps are visible immediately.
- Set interning needed nothing: the content hash already mixes the kind byte, so caps
  intern and identical loadouts (caps included) collapse to one set. The digest is
  content-derived, so existing pins are untouched.

## Semantics (receipt: facts)

- Fold order is `base + Adds → × (1 + Additives) → × (1 + Multis) → min(Caps)`;
  a cap binds the **post-multi** value: base 5, `Multi +2`, `Cap 10` → **10**
  (cap-before-multi would read 15).
- The tightest cap wins: base 5, `Added 95`, caps {85, 75, 95} → **75**; a cap above
  the folded value is inert (content still interns it — the cap is live for later
  modifiers).
- Permutation invariance holds with caps in the loadout: a random 7-modifier loadout
  over all four kinds delivered to 16 slots in 16 random permutations — every slot
  bit-identical; SetCount 3 (empty, loadout, and the derived stat's synthetic-source
  set — the atom-99 intern pattern).
- Board matches the eager oracle step-for-step over a three-round program mixing all
  four kinds (accumulating, no removals): 40 → 12.5 → −37.5, crit and its derived
  stat both.

## Tests

- `tests/Sv.Tests/CapTests.cs` (new, 6 facts): `Cap_FoldsAfterMulti`,
  `Cap_AboveFoldedValue_IsInert`, `TightestCap_Wins`,
  `CapLoadout_RandomPermutations_Converge`, `CapProgram_MatchesEagerOracle`,
  `Modifier_Kind_AcceptsCap_RejectsUnknown`.
- `tests/Sv.Tests/EagerOracle.cs` — oracle `Fold` mirrors the cap pass (kind 3);
  the oracle's canonical `(kind, bits)` sort and content key needed no change.

Sv.Tests 434 (+6), Sv.Unity.Tests 7, NorthStar 27 — all green; `-p:SV_CHECKED=true`
green; bench `all scenarios ok`; FMA grep clean.

## Receipts

- Local, this host (i9-14900K): `dotnet test tests/Sv.Tests/Sv.Tests.csproj -c Release`
  → 434/434; SV_CHECKED → 434/434; `Sv.Unity.Tests` → 7/7; `Sv.NorthStar` exit 0;
  `Sv.Bench` → `all scenarios ok`.
- CI: GitHub Actions dead since 2026-10-24 (billing; receipt 0102) — local receipts
  authoritative.
- Contract: v2.0 batch clause 12 (`Cap` op kind) — implemented ahead of the fold per
  the bus-0053 pattern; the clause text matches this semantics (fourth kind, folded
  after Multi). Digest pins unchanged (no pinned scenario includes a cap).

## Provenance

main@d79c6a2 + this atom. Atom 101 (Burst kernels) and atom 102 (Unity adapter v2)
completed green in their worktrees meanwhile; integration follows this landing.
