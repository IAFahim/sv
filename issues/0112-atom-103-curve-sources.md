# Issue 0112 — Atom 103 (roadmap 7.5, §4.11 curves): curve sources — diminishing returns as one table load

## Changes

- `src/Sv/Schema.cs` — `SchemaBuilder.Curve(name, base, source, table, min, step)`:
  a single-source derived stat whose contribution is the **pre-folded table entry**
  at the bucket holding the source value, in place of `coefficient * value`.
  `StatSource` carries optional `Curve`/`CurveMin`/`CurveStep` (schema-declared —
  not a runtime binding: it rides renumber, load and `InvalidateBindings` like a
  coefficient). The schema `ContentHash` mixes the table bytes, min and step, so a
  curve change is a state-compat change (clause 15 prefers pre-folded tables; the
  hash already said so).
- `src/Sv/Board.Stats.cs` — `InitSources` flattens the tables: `sourceCurve[at]`
  is the offset into `curveData` (−1 = linear), with per-source `curveMin`,
  `curveStep`, `curveLength` and a precomputed `curveCeiling = min + (n−1)·step`;
  the curve block is allocated only when the schema declares curves, so linear
  boards are byte-for-byte unchanged. `GatherInto` evaluates one bucket per set
  resolution: below `min` reads entry 0, at/above the ceiling reads the last
  entry, otherwise `(int)((value − min) / step)` — one IEEE subtract, one divide,
  one truncate, deterministic everywhere (NaN fails both comparisons and the C#
  `float→int` NaN result 0 picks entry 0). The anchor hook composes: an anchored
  curved source evaluates the table at the **anchor's** value for every slot.
- Set interning needed nothing: the synthetic entry is `Added(table[bucket])`, so
  slots whose sources land in the same bucket produce identical content and intern
  to one set — "evaluated once per set" falls out of the existing fold.

## Tests (Sv.Tests 434 → 444; Unity 13; NorthStar 27 unchanged)

- `tests/Sv.Tests/CurveTests.cs` (new, 5 facts):
  `CurveSource_QuantizesAcrossBuckets_MatchesOracle` (8 slots spanning below-min,
  every bucket edge, the ceiling, a negative and a 3e38 value — board == extended
  eager oracle, bucket by bucket), `CurveSource_SameBucket_SharesFold`,
  `CurveSource_TracksSourceChanges` (add/remove rides the dirty propagation),
  `CurveSource_AnchorInterplay` (anchored curved source at any slot),
  `Curve_Builder_Guards` (empty table, non-finite entry, non-positive step,
  undeclared source).
- `tests/Sv.Tests/EagerOracle.cs` — `EagerModel` takes optional per-source curve
  tuples and quantizes with the same clamps; every existing constructor call is
  unchanged (default null).

## Receipts

- Local, i9-14900K: Sv.Tests **444/444**, SV_CHECKED **444**, `Sv.Unity.Tests`
  13/13, `Sv.NorthStar` exit 0, `Sv.Bench` `all scenarios ok`, FMA grep clean
  (the bucket math is subtract/divide/truncate — nothing fused).
- Memory bills unchanged: the curve block allocates only for curve-declaring
  schemas; the `IntrinsicsOnly_ExactBill` and load-state pins pass untouched.
- CI: GitHub Actions dead since 2026-10-24 (billing; receipt 0102) — local
  receipts authoritative.

## Provenance

main@86f5d53 + this atom. Bus checked before and after (inbox empty both checks).
