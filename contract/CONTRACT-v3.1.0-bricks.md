owner approval pending, bus unreachable (written by B1 for the owner, 2026-11-13; additive over v3.0.1)

# The Composition Contract - v3.1.0 (rule bricks; amendments over v3.0.1)

v3.1.0 keeps every clause of v3.0.1. It adds five rule bricks found missing by the Lego test (`docs/LEGO-REPORT.md`, issue 0330).
Every brick is additive: a pack that uses none of them reads, hashes, compiles and runs exactly as under v3.0.1 (same `PackHash`,
same digests). The reference interpreter (`src/Sv.Rules/Reference`) is the oracle for each clause; compiled kernels equal it.

Words used below. The *frame index* `f` of a step is the number of frames stepped before it (0 for the first step; after `Rewind(e)`
it is the first frame of epoch e). The *frame-start value* of a cell is its value at the start of Decide (clause 7, A4: persistent
cells are frozen until Commit). A record *counts* when its taken amount `Taken = TakenPerHit x TakenHits - Absorbed` is not zero.
Its *per-hit taken amount* is `TakenPerHit` when nothing was absorbed by amount, otherwise `sat(Taken)` (the rule emits with
`amount: Taken` already use, `ConsumedPerHit`). `sat` is clause 29's saturation to `int`.

> **Clause 34, new. Assign modes.** A channel row with `into` may carry `"mode"`: `"add"` (the default, clause 19 as before),
> `"set"`, `"max"`, `"min"` or `"source"`. A flow term may be written `{"set" | "max" | "min": [intrinsic, channel, ...]}` beside
> `add` and `subtract`. For an intrinsic X with frame-start value `b`, the *assign value* `a` starts at `b` and takes, in this order:
> 1. every counting record of the target whose channel has `into: X` and a mode other than add, in canonical record order (clause
>    21); its value is the per-hit taken amount, or for mode source the record's source slot (`-1` when it has no source);
> 2. every same-frame self emit into such a channel, in self-rule order (stratum, then rule id; within a rule its units in canonical
>    order, then its actions in written order) whose shaped amount times hits is not zero; its value is the shaped amount per hit,
>    or for mode source the entity's own slot (an emit's source is the emitting entity, clause 25);
> 3. every flow term with a mode other than add whose flow applies and whose channel total `T` is not zero, in flow id then term
>    order; its value is `T` (64-bit).
> Mode set and source replace: `a = v`, so the last value wins. Max and min fold: `a = max(a, v)`, `a = min(a, v)`; both include `b`,
> so `max` refreshes ("stun = max(stun, d)") and never lowers. The add terms `A` of X (flows add and subtract, channels in add mode,
> absorb and spend reports) are summed as before. X then gets ONE delta, `delta = Add(A, a - b)` in saturating 64 bits, and folds
> once by clause 19: `after = clamp(b + sat(delta))`, which is `clamp(a + A)` unless a sum leaves the `int` range (D4: sum, then
> clamp once). Floor and ceiling are unchanged. Channel totals of an assign channel are still the sum of taken amounts (flows and
> conditions read them as before); only its `into` contribution is the assign value instead of the total.
> Determinism: every value comes from canonical records, the pack's static rule order and frame-start cells; max and min are
> commutative and associative; set and source take the last element of a sequence whose order is clause 21's total order followed
> by the compile-time self and flow order. No value depends on workers, arrival or the host.
> Errors: `mode` without `into` (BadValue); an unknown mode (BadValue); a flow term `source` (BadValue: a flow has no record); two
> different assign modes on one intrinsic across all channels and flows (AssignConflict).

> **Clause 35, new. Amount from a cell; scale by an intrinsic.** An emit amount may be `{"cell": "self.X"}`, `"source.X"`,
> `"owner.X"` or `"<reference>.X"` with X a stat or an intrinsic. It reads the frame-start value: an intrinsic as its cell, a stat as
> `Rescale(stat, scale(stat), 1)` (clause 33's read rule), saturated to `int`. Hits are counted as for a constant amount (the
> record's hits under per Hit, else 1). `scale` may name an intrinsic of the emitting row as well as a stat; the emitted amount is
> then `sat(amount * cell / scale(intrinsic))`, clause 33's rule with the intrinsic's scale. Determinism: reads are of frozen cells
> (A4); a record's source is a key field, so `source.X` is a function of the canonical record. Errors: the cell names an event or
> nothing (UnknownName or BadEmit); `source.X` in a rule per Frame (ScopeViolation); a cell amount or an intrinsic scale on an edge
> rule (BadEmit: edges run after Commit, when cells are no longer frame-start values).

> **Clause 36, new. Value-driven modifiers.** (a) A card modifier with `"perCount": true` adds the term `sat(value * count)`, where
> `count > 0` is the card's intrinsic (int64 product, clause 29). A card that is not held (count <= 0) adds nothing, as before.
> The term then joins clause 31 like any other; `StatLaw.Canonicalize` and `Fold` are unchanged (they already order and fold terms
> whose values are only known at run time). (b) A gate Shape value may name a cell: `{"additive": {"cell": "source.X"}}` (also
> added, multi, cap; subjects as in clause 35). It is read once per record from frame-start state and converted to the Shape's unit
> by clause 33: `Rescale(raw, scale(X), 10000)` for additive and multi (the cell holds the decimal a designer would write, `0.25`
> becomes 2,500 basis points), `Rescale(raw, scale(X), 1)` for added and cap. The term then joins clause 24 with the constant terms.
> A Shape that reads `source.X` makes its gate record-dependent (it no longer applies to same-frame self emits, issue 0180 item 8).
> Errors: `perCount` not a boolean (WrongType); a Shape cell that names an event or nothing (UnknownName).

> **Clause 37, new. Deterministic roll.** A condition operand `{"roll": p}` with `0 <= p <= 10000` (basis points) is true for the
> share p of records: `RollHash(seed, f, key, amount, rule, ordinal)` is the SplitMix64 finaliser `M(z)` applied as
> `h = M(seed ^ f); h = M(h ^ key); h = M(h ^ (uint)amount); h = M(h ^ (uint)rule); h = M(h ^ ordinal)`, with
> `M(z) = z' ^ (z' >> 31)` where `z0 = z + 0x9E3779B97F4A7C15`, `z1 = (z0 ^ (z0 >> 30)) * 0xBF58476D1CE4E5B9`,
> `z' = (z1 ^ (z1 >> 27)) * 0x94D049BB133111EB` (unsigned 64-bit, wrapping). The roll is true iff `((h >> 32) * 10000) >> 32 < p`.
> `seed` is the pack's `"seed"` (a non-negative 64-bit integer at the top of `values.sv.json`, default 0, written only when not 0, so
> it is part of `PackHash`); `key` and `amount` are the canonical record's (clause 21); `rule` is the id of the gate or emit rule
> whose condition holds the roll (a chain is inlined into that rule); `ordinal` is 0 in v3.1.0: a record standing for h identical
> hits rolls once for all of them. Determinism: every input is pack data or a canonical value; the same on every host, worker count,
> SIMD width and replay. A roll reads the record, so: Errors: p outside 0..10000 or not an integer (BadValue); a roll in a flow,
> event, edge, or an emit per Source or Frame (ScopeViolation, the existing "no single record" rules); a gate with a roll does not
> apply to same-frame self emits.

> **Clause 38, new. Spend.** A gate effect `{"spend": "X"}` (X an intrinsic) admits hits while the budget lasts and denies the rest.
> Spend and absorb effects run after Admit and Shape, in gate order (priority descending, then id), each over the admitted records
> its gate applies to, in canonical record order. With `used(X)` shared by every absorb and spend on X, a spend gives each candidate
> record `take = min(max(remaining, 0), TakenHits)` with `remaining = max(b, 0) - used(X)` (b the frame-start value), sets
> `TakenHits = take` (the other hits become NotTaken) and adds `take` to `used(X)`. Absorb is the same walk with the opposite
> verdict (it removes `take` hits). The gate never writes X during the frame (A4); `used(X)` reaches X as the add term `-used(X)`
> of clause 34, exactly as absorb's report. Same compile-time rules as other gates (priority, card guard, condition scope).
> Determinism: canonical order and frame-start budget only. Errors: the budget names no intrinsic (UnknownName); a gate rule with
> both absorb and spend (BadValue: their relative order inside one rule would be a second ordering rule).

## Readings taken (the code follows them; owner to confirm)

- Assign values are per-hit, not per record total: a merged record of h equal hits sets the same value as one hit (set is
  idempotent and merge-invariant, T1). Max and min include the frame-start value.
- A same-frame self emit into a `source`-mode channel sets the entity's own slot; a record without a source sets `-1`.
- Shape cells use clause 33 to reach the Shape's unit, so an intrinsic used as a percentage needs a decimal scale.
- A roll is one draw per record (ordinal 0); a per-hit draw would need an evaluation per hit and is left for a later version. The
  frame index is the count of frames before the step, the same counter the host journal and replay use.
- Spend counts hits only (no per Amount form); a spend-denied hit keeps `Admitted` true and only lowers `TakenHits`, so per Hit
  emits still count offered hits (issue 0330 reading).

## Not in this version

Stat layers and a derived-stat row (Lego 17), a same-frame lethal check (Lego 13), masks above bit 63 (Lego 7), removal of the
redundant bricks.
