# Issue 0047 — Intrinsics add overflow: wrap (pinned) or saturate — bus ruling needed (review C-1)

Filed as GitHub issue #27 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:feat`, `contract:proposed`. Bus message 0039 (to `user`)
carries the decision; this file is the repo-side record. Status: **open — pending bus**,
precedent [issues/0021](0021-bind-column-length.md), [issues/0022](0022-board-memory-scales-with-capacity.md).

## Problem

`RunIntrinsicsChunk`/`RunEnabledChunk` compute `before + delta` as an unchecked `int`
on every path (scalar `Simd.ChunkScalar`/`Simd.EnabledWordScalar`; vector
`Simd.Chunk.cs` Loop 256/128/scalar tail/`MaskedWord`) and feed the wrapped sum to the
clamp. With `Constant(0,100)`, a full heal (`delta = int.MaxValue` on hp 50) wraps
negative, clamps to 0, and reports `applied = -50` — the heal kills the target
(review C-1, HIGH; reproduced against source).

The wrap is not accidental: `IntrinsicsTests.Overflow_WrapsUnchecked` pins
`unchecked(int.MaxValue + 1)`. The vendored contract v1.0.3 is silent on integer add
overflow, but a pinned math behavior on the public verb surface is contract-grade —
changing it unilaterally would violate the bus-first rule.

## Shape (if saturate is ruled)

- Scalar: `sum = a + b; overflow = ((a ^ sum) & (b ^ sum)) < 0; overflow ? (b < 0 ?
  int.MinValue : int.MaxValue) : sum` — select-based, clause-2 compatible.
- Vector128/256: `ConditionalSelect` over the overflow mask, same lattice result.
- Apply on all six add sites; `applied = after - before` unchanged.
- Rewrite `Overflow_WrapsUnchecked` to pin saturation; add the
  `{int.MinValue, -1, 0, 1, int.MaxValue}²` lattice test across scalar/128/256/masked/
  enabled paths (prototyped during verification; reverted with the change).
- Clause 16 already pins saturating float→int bound conversion; saturating the add is
  the consistent reading.

## Blast radius

- Only overflowing adds change outcome; in-range arithmetic is bit-identical.
- Tapes/digests recorded before the change diverge after it (re-record or version bump).
- Hosts using huge deltas as "fill to max" change result — that is the intent.

## Receipts

- Prototype (scalar + 128 + 256 + masked + enabled paths, lattice tests) built green;
  only `Overflow_WrapsUnchecked` failed, which is the ruling itself. Reverted cleanly;
  suite back to green (181 Sv.Tests) at `e546d9d`.
- Bus: `agent-bus/inbox/user/0039-stats-ruling-needed-sv-intrinsics-add-overflow.md`.
