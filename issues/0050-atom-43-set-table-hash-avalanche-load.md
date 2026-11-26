# Issue 0050 — Atom 43: set-table hash avalanche + 50% load + format bump (review X-1 b/c)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #30 (numbering offset — see issue #24 provenance note). Labels:
`ai`, `area:core`, `kind:perf`. Source: design review 2026-10-20
([issues/0044](0044-2026-10-20-design-review.md), GitHub #24), finding X-1 parts (b)
and (c) — verified against source before fixing. Part (a) (sets never freed; the
state/cache split) is structural review atom 12 and goes through the bus.

## Problem

- **(b)** `ContentHash` mixed values as `kind | floatBits << 8` into an FNV multiply,
  which carries only upward: hash bits 0–7 were independent of the float values, so
  every same-shape set landed in one start bucket for any mask ≤ 255 (review measured
  200 value-distinct sets → 1 start bucket). `Resolve` degraded to O(chain) per call
  for the most common shape (per-entity buff values).
- **(c)** Buckets grew only when a probe wrapped the entire table (100% load), so the
  table sat near-full before every growth.

## Shape

- Murmur3-style 64-bit avalanche finalizer (xor-shift, multiply, xor-shift ×2) after
  the FNV body. Equality unchanged: `setHash` compare + `ContentEquals` byte compare
  still decide identity; the finalizer only distributes.
- `Resolve` grows buckets before inserting when `(setCount + 1) * 2 > bucketMask + 1`
  — load stays ≤ 50%.
- `Board.StateShape` XORs a `SetTableFormatVersion = 2` constant: states saved before
  this change fail `Expect` loudly (`InvalidDataException: state does not hold a board
  of this shape`) instead of loading a bucket layout that no longer matches the hash.
- Internal test surface: `SetTable.HashOf` and `SetTable.BucketCount`.

## Receipts

- `SetTableSpreadTests.ContentHash_FloatOnlyDifferences_SpreadAcrossLowBuckets`:
  200 sets differing only in value cover ≥ 16 of 32 low buckets (old hash: 1).
- `Resolve_ManyDistinctValues_KeepsLoadAtMostHalf`: 300 distinct values keep
  `buckets >= 2 × sets`; re-resolving the first set returns the same identity.
- `BoardLoadState_PreFormatV2State_IsRejectedLoudly`: a state whose shape field is
  flipped by one version bit is rejected with `InvalidDataException`.
- Suite: build green; 218 tests pass (23 + 6 + 189) and green under
  `-p:DefineConstants=SV_CHECKED` (191). `StateRollbackTests` (bit-identical rollback
  digests) unchanged and green — same-process determinism is unaffected; only
  cross-format loads are refused.
