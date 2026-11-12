owner-approved, bus unreachable (owner decision D11, 2026-11-12)

# The Composition Contract - v3.0.1 (integer stats; amendments over v3.0.0)

v3.0.1 keeps every clause of v3.0.0 that is not named below. Owner decision in force: D11, stats are integers. The legacy float stat
machine of `src/Sv` and its pins (soak 6D6C602BF07E9366, cross-host A2ACAA69E55F79D8, Burst kernel 2B43BC1549AAE751) are untouched
legacy until M11; nothing in this text applies to them. The law lives in one file, `src/Sv.Core/Stats/StatLaw.cs`.

> **Clause 28, new. Integer values.** Every stat cell is an `int`. Every stat and intrinsic carries a decimal scale stored in the pack
> (`scale`: 1, 10, 100, 1000, 10000, 100000 or 1000000; default 1). A cell holds the designer value times its scale: health 100.5 at scale
> 10 is the cell 1005. The pack compiler turns decimal text into the cell once and exactly; text that is not a whole number of steps of
> 1/scale, or does not fit an `int` at that scale, is rejected (`NotExact`); a scale outside the list is rejected (`BadScale`). The
> engine holds no float for state anywhere. A float exists only at a host edge for display and never flows back.

> **Clause 29, new. One arithmetic rule.** Products are taken in 64-bit integers from 32-bit operands, so they are exact. There is one
> division rule: truncate toward zero (C# `/` on `long`). Every result that is stored or passed to the next step is saturated to the
> `int` range: above `int.MaxValue` it is `int.MaxValue`, below `int.MinValue` it is `int.MinValue`. No step rounds, no step uses a
> float, no step depends on the host, the worker count or the vector width.

> **Clause 30, new. Percentages.** A percentage is an `int` in basis points: 10,000 is 100 %. Additive and Multi modifier values are
> basis points (`additive: 0.25` in a pack is 2,500; `multi: 1.0` is 10,000, which doubles). Added and Cap values are cells at the scale
> of the stat they modify (for a gate Shape, at the scale of the record amount, which is 1).

> **Clause 31, new. The stat law (replaces the float algebra of StatMath for the v3 engine).** For a start value `b` and terms in
> canonical order (by kind Added, Additive, Multi, Cap, then by value ascending):
> 1. `v = sat(b + sum Added)`, the sum taken in 64 bits;
> 2. `f = sat(10000 + sum Additive)`, the sum taken in 64 bits;
> 3. `v = sat(v * f / 10000)`;
> 4. for each Multi `m`, ascending: `v = sat(v * sat(10000 + m) / 10000)`;
> 5. for each Cap `c`: `v = min(v, c)`.
> The order of steps is fixed. Steps 1, 2 and 5 do not depend on the order of their terms; step 4 does (each product truncates), which is
> why the terms are canonical. No terms give `v = b`.

> **Clause 32, new. Derived stats.** A derived source of stat T reads stat S of the same entity and adds one Added term, computed before
> T's own modifiers: linear, `sat(S * coefficient * scale(T) / (10000 * scale(S)))` with the coefficient in basis points of displayed
> units and one truncation (when `scale(T) >= scale(S)` the product is taken first and saturates if it leaves 64 bits); or by a curve,
> `table[clamp((S - min) / step, 0, length - 1)]` with the table at T's scale (`S <= min` is bucket 0). Sources form a directed acyclic
> graph checked when the machine is built; within one entity a stat is resolved after every stat it reads.

> **Clause 33, new. Scale conversions.** `Rescale(v, from, to)` multiplies by `to / from` with saturation, or divides by `from / to`
> truncating toward zero. An intrinsic bounded by a stat (clause 19's floor and ceiling) is bounded by `Rescale(stat, scale(stat),
> scale(intrinsic))`; this replaces `BoundOf` for stat bounds. An expression that reads a stat reads `Rescale(stat, scale(stat), 1)`, the
> stat in whole designer units. An emitted amount scaled by a stat is `sat(amount * stat / scale(stat))`.

> **Clause 24, amended. Gates.** Shape applies clause 31 with the record's amount per hit as the start value and the Shape effects of the
> applicable admitted rules as terms. The `BoundOf` step is gone: the result is already an `int`.

> **Clause 7, amended. Stats.** Step (2) is: expiry of every modifier whose expiry tick is at or before the frame's tick (in order of
> (expiry, id)), then the stat operations of the frame in canonical order, then recalculation. Canonical order of stat operations is by
> (slot, operation, source, stat, kind, value, expiry), with RemoveSource before Add, so a source's removal never removes what the same
> batch adds. Modifier ids are assigned in that order. Recalculation resolves dirty (slot, stat) pairs in ascending slot order and, within
> a slot, in derived-graph order; a pair whose value is unchanged writes nothing; a changed value is written into the stat's lane through
> the lane journal of clause 26 and reported in the changed list (pairs ascending by slot, rows per chunk).

> **Clause 27, amended. Replay.** The stat machine keeps one undo mark per epoch for the ring's span. `Rewind(e)` undoes every modifier
> change after the mark of epoch e and restores the id counter, while the lane journal restores the stat lanes; the two meet at the same
> frame boundary, where every live stat lane equals clause 31 applied to its entity's modifiers. Stat operations are inputs from outside
> sv and are logged in canonical order.

What changes against v3.0.0: clause 24 drops `BoundOf`; clause 7 step (2) is spelled out; clause 27 covers the stat machine; clauses
28 to 33 are new. Proofs that the vector forms equal the scalar law are in the test phase: the 256-bit `Rescale` up-scaling is integer
compare, select and 32-bit multiply; down-scaling divides exactly representable doubles (`|v| < 2^31`, divisor a power of ten up to
10^6, so the correctly rounded quotient truncates to the exact integer quotient); `Cap` is integer compare and select.
