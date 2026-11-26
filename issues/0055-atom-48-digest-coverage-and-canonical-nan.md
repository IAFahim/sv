# Issue 0055 — Atom 48: digest reads the effective column; computed NaNs are canonical (review D-1 partial, D-3)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #35 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), findings D-1 (partial)
and D-3 — verified against source before fixing.

## Problem

- **D-1 (partial):** `Tape.WriteBody` digested `board.OutputPtr(stat)` — the sv-owned
  column. A bound stat's owned column keeps its pre-bind value (zeros), so hosts using
  `BindOutput` (the Frent/Unity pattern) got digests that never covered those stats
  and never complained.
- **D-3:** `Fold` output could reach NaN (e.g. `Multi(-1)` on an `Added`-overflowed
  `+Inf`: `Inf × 0`). x86_64 generates `0xFFC00000`, ARM64 `0x7FC00000` — `MarkChanged`
  compares raw bits (spurious changed flags) and the tape digest differs across
  architectures (false desyncs).

## Shape

- `Tape.WriteBody` reads the effective column — the bound external column (honoring
  its stride) when bound, the owned column otherwise. The full-word `MemoryCopy` fast
  path stays stride-1-only; strided bindings walk live slots.
- Computed values are canonicalised to `0x7FC00000` when NaN, at both `SetComputed`
  sites, before any broadcast, mirror write, `MarkChanged` comparison or digest read.
  Fold arithmetic itself is untouched (clause 1 strict IEEE policy intact); ±Inf
  results pass through unchanged.
- Remaining D-1 gaps are unchanged and tracked on #24: intrinsics (S-1), modifier
  lists/expiries, timeline ids/clocks, pending events, applied, input ring, tick.

## Receipts

- `CanonicalNaNTests` (3 facts): an `Added`-overflow + `Multi(-1)` fold broadcasts bit
  pattern `0x7FC00000`; `+Inf` results pass through; `CanonicalNaN` leaves
  finite/±Inf values alone and maps both NaN signs to the canonical pattern.
- `DigestCoverageTests` (2 facts): the body's first live stat float comes from the
  bound column (owned stays zero), mutating the bound column changes the digest, and
  a stride-2 binding digests the strided values.
- Suite: build green; 239 tests (25 NorthStar + 6 Unity + 208 Sv.Tests), green under
  `SV_CHECKED` (210). Tape round-trip tests (`TapeTests`, `Playback` digests)
  unchanged and green — unbound boards digest identically to before.
