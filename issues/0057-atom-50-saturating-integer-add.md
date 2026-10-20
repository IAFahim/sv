# Issue 0057 — Atom 50: intrinsics integer add saturates (review C-1 resolved: SATURATE)

Filed as GitHub issue #37 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`, `contract:proposed`. Resolves [issues/0047](0047-add-overflow-wrap-vs-saturate-bus-ruling.md)
(GitHub #27). Decision authority: the owner delegated the [issues/0047](0047-add-overflow-wrap-vs-saturate-bus-ruling.md)
ruling in-session on 2026-10-20 ("make the decision for me that would be best for long
run"); recorded on the bus as the Reply-To to message 0039.

## Decision

**Saturate.** `before + delta` on every intrinsics path is replaced by a saturating add
fed to the clamp. Rationale:

- Wrap has no defensible semantics in bounded HP/mana arithmetic: `2_000_000_000 +
  2_000_000_000` under `Constant(0, 100)` wrapped to `-294_967_296`, clamped to **0** —
  a full heal killed the target (review C-1, HIGH).
- Clause 16 already pins saturating float→int bound conversion; saturating the add is
  the consistent reading of the contract's integer policy.
- Saturation composes with the clamp exactly: `clamp(saturate(before + delta)) ==
  (int)Math.Clamp((long)before + delta, min, max)` for all int operands and all bounds
  `min ≤ max`, so the oracle is total and the behavior is author-predictable.

## Change

- `Sv/Simd.cs` — `SaturatingAdd(int, int)` (sign-bit overflow test, select-based,
  clause-2 compatible: no branches on values, no FMA); `ChunkScalar` uses it for both
  uniform and per-slot deltas.
- `Sv/Simd.Chunk.cs` — `SaturatingAdd(Vector128<int>, …)` and
  `SaturatingAdd(Vector256<int>, …)` via `ConditionalSelect` over the overflow mask;
  wired into Loop 256/128, scalar tail, `EnabledWordScalar`, and `MaskedWord`
  (full-lane and masked-lane branches).
- `Sv.Frent/Population.cs` — both chunk remap walks (`ChunkWindow.Apply`,
  `ApplyEnabled`) use `Simd.SaturatingAdd`; the contiguous fast paths already inherit
  the fix through `Board.RunIntrinsicsChunk`/`RunEnabledChunk`.
- `applied` reporting unchanged: signed overflow requires same-sign operands, so
  `after - before` is exact under saturation (no new wrap surface).

## Tests

- `IntrinsicsTests.Overflow_WrapsUnchecked` rewritten as
  `Overflow_SaturatesBeforeClamp` (MaxValue + 1, bounds `MinValue..MaxValue` →
  MaxValue, `applied = 0`).
- New `SaturatingAddTests` (10 facts): `{MinValue, -1, 0, 1, MaxValue}` lattice ×
  bounds `{Min..Max, -10..10, 0..100}` against the total `Math.Clamp(long)` oracle,
  across scalar `ChunkScalar` (uniform + varying delta), `EnabledWordScalar` (partial
  word, disabled slots zeroed and untouched), board chunk vector widths (uniform +
  varying), enabled full word, enabled partial word (masked lanes), plus the two money
  cases: overflow heal → clamp max (was wrap-death), overflow damage → clamp min (was
  wrap-heal), and the enabled-word overflow heal.
- New PopulationTests facts: `RemapChunk_OverflowHeal_SaturatesInsteadOfWrapping`,
  `RemapEnabledChunk_OverflowDamage_SaturatesInsteadOfWrapping` — the remap walks
  saturate (previously these paths were missed by the atom-41 prototype).

## Blast radius

- Only overflowing adds change outcome; in-range arithmetic is bit-identical.
- Tapes/digests recorded before this change diverge after it (re-record; no format
  change — the digest reads values, not the arithmetic).
- Hosts using huge deltas as "fill to max" now get clamp max — that is the intent.

## Contract amendment

Clause 18 proposed to the integrator over the bus (message after the decision record):
"intrinsics integer adds saturate at `int.MinValue`/`int.MaxValue` before the clamp;
the composition `clamp(saturate(before + delta))` is the author-visible total
function". The vendored contract copy in this repo is **not** edited in place — the
amendment lands when the integrator folds it into the composition contract and the
vendored copy is refreshed from upstream.

## Receipts

- `dotnet build sv.slnx -c Release -m:1` — 0 warnings, 0 errors.
- `dotnet test sv.slnx -c Release --no-build` — 220/6/27 passed (Sv.Tests / Unity /
  NorthStar), including 12 new/rewritten facts.
- `dotnet test` with `-p:SV_CHECKED=true` — 220 + 27 passed.
- Bus: decision record (Reply-To 0039, to `user`), amendment proposal (to
  `integrator`); GitHub #27 closed by this issue.
