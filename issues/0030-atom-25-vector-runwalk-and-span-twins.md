# Issue 0030 — Atom 25: vectorized enabled run-walk (masked AVX2) + vectorized `Span` twins

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #30 (pending — see receipts). Labels: `ai`, `area:simd`,
`kind:perf`.

## Problem

`RunEnabledChunk` (all six overloads) walked set bits one at a time through scalar
`Clamp.Apply`, ~4x slower per row than the dense chunk even on fully enabled words.
The `Span` twins of `RunIntrinsicsChunk` never reached `Simd` at all — pure scalar.

## Shape

- `Simd.EnabledWord` per 64-row word:
  - word `== ~0`: the dense specialized loop (atom 24) over 64 rows — portable;
  - AVX2, partial word: per 8-lane group — empty group writes only `applied` zeros;
    full group is a plain vector op; mixed group uses `vpmaskmovd` masked loads for
    value, delta and int bound columns (`vmaskmovps` for stat bound columns) and a
    masked store for value. Disabled rows are **never read or written**: hosts may run
    complementary masks over the same window on different threads, and release builds
    do not range-check the window (`Guard.EnabledRange` is checked-only), so an
    unmasked access could race or fault.
  - otherwise (ARM partial words, no SIMD): the previous scalar bit-walk
    (`EnabledWordScalar`).
- `applied` semantics unchanged: whole window written, disabled rows 0.
- `Span` overloads validate lengths up front (`Guard.SpanReach` — value/delta must
  reach the highest enabled row; `Guard.SpanWindow` — a non-empty applied covers the
  window), then pin and take the pointer path. Behavior change on error only: a
  too-short span now throws `ArgumentException` before any write, instead of
  `IndexOutOfRangeException` after a partial write.

## Receipts

- `EnabledWordTests.EnabledWord_MatchesScalar_AllClampKinds`: 8 clamp shapes x 18
  words (edge patterns + random sparse) x 4 offsets x uniform/varying delta x
  applied on/off — value and applied bit-identical to the scalar walk (NaN / +-1e30
  stat bounds included).
- `EnabledWord_NeverTouchesDisabledRowsPastAGuardPage` (Linux, AVX2): the window
  straddles an `mprotect(PROT_NONE)` page with only rows 0-2 enabled; uniform,
  column-delta and column-bound shapes run without faulting.
- Bench `enabled-runwalk` (alternating mask, 100k rows): 63.0 -> 22.8 us (2.8x).
- Tests default: 146 + 16 + 6 passed, 0 failed. SV_CHECKED: 148 + 16 + 6 passed.
- no-FMA grep clean.
