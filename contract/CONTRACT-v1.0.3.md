# The Composition Contract — v1.0.3

Changelog: v1.0.3 adds clause 17 (events are sparse signals; born from sv's
fnptr-thunk profile, sv issue #9, bus 0019) and consolidates this changelog to one
paragraph. v1.0.2 added clause 16 (pinned float→int conversion for stat bounds;
born from a failed sv width-invariance receipt). v1.0.1 folded the clause-7
intrinsics-home sentence (requested by sv, bus message 0006). v1.0 was the
15-clause negotiation of messages 0001–0005.

Master copy for the three-system composition: **ecs-kernels** (derived math) ×
**tl** (authored motion) × **board** (settings/state: stats, intrinsics, events, input).
One noun — dense columns over entity slots. Three verbs over it.

- Status: negotiated on the agent-bus (message thread 0001–0004); all citations
  reproduced on this machine before folding.
- Ownership: each system vendors the clauses it obeys into its own repo. The bus copy
  is the merge point; changes are append-only messages, then a version bump here.
- Verification: clause conformance is proven by the north-star test
  ([NORTH-STAR.md](NORTH-STAR.md)) plus each system's own suite; mask/run semantics by
  [mask-fixtures.json](mask-fixtures.json).

## Clause index (17)

1. **Strict two-step IEEE float policy**; no FMA anywhere (grep-enforced); StrictFloat oracle vendored.
2. **Clamps are ternary/select** (compare+blend); never Min/Max on floats; ints may use Avx2.Min/Max.
3. **Canonical aggregation order**: (op-kind, insertion id) strict left fold, scalar per set; vertical-only vectorization, width-invariance receipts required.
4. **Recalc ends in a broadcast** into bound columns; per-entity board reads are tooling-only; no gathers.
5. **Dense slots**; adapter owns entity→slot at create/destroy; single chunk shape `RunChunk`/`RunEnabledChunk` (+ `Span` twins).
6. **Condition ingestion by run-walk** (tzcnt, `length == 64` guard); semantics conform to `mask-fixtures.json`.
7. **Frozen frame order**: input edges → drain prev events → stat recalc + broadcast → tl Apply/Advance → kernels → condition run-walk → write events → swap. Mid-frame structural changes apply next frame. Intrinsics writes happen inside event-drain handlers (step 2) and condition responses (step 6), and both read frame-frozen bounds from the previous broadcast.
8. **Runtime dependencies: none**; tl and ecs-kernels are test-only references.
9. **Bool normalization**: any nonzero byte = true; compare-≠0 → movemask before any bit trick; arithmetic OR of raw bytes banned.
10. **Clamp bounds**: Constant / Columns / Stat; Stat resolves at construction to the bound output column; bounds are frame-frozen; no clamp–modifier cycles.
11. **Axis bit-capture**: raw uint bits in the ring; replay is bit-verbatim.
12. **Pointer stability**: bound output pointers (`BindOutput`) and `Clamp.Stat`-captured columns are stable from warmup until an explicit rebind; any storage growth forces re-resolve before the next op; use-after-rebind detected in checked builds.
13. **tl effect columns accumulate** (`+=`): (a) one column, one writer kind — store-writers (stats broadcast) and accumulate-writers (tl) never share a column; (b) the host zeroes each tl effect column once per frame, immediately before the timeline step of clause 7; (c) tl's position in the frozen order fixes the add order.
14. **Warmup before parallel frames**: all timeline ids loaded and first-use folds completed during warmup; first-use inside a warm frame is a contract violation, asserted in checked builds.
16. **Pinned float→int conversion for stat bounds**: NaN → 0; saturate to [int.MinValue, int.MaxValue]; otherwise truncate toward zero. Implemented identically in scalar and vector paths (sv: `Clamp.BoundOf` / `Simd.BoundsOf`); raw `(int)float` casts on bound paths are banned — ECMA leaves out-of-range conversion unspecified and JIT tiers and ISAs disagree (x64 `cvttss2si` indefinite vs ARM `fcvtzs` saturating).
15. **Stats is float/int only; no double effect columns** (tl's double combine branch never exercised); any shipped `IBlend` follows clauses 1–2 and prefers pre-folded tables so Blend never runs at runtime.
17. **Events are sparse signals; dense per-row payloads move through columns**: emitters and makers exist for edge-triggered facts (threshold crossed, timeline finished, damage dealt), never as a per-row per-frame data channel — full-density payload traffic goes through broadcast/accumulate columns per clauses 4/13. Hosts must not build full-density make-call loops in perf-critical frames: on .NET each maker call pays the `UnmanagedCallersOnly` entry gate (~10 ns, IL stub + TLS, measured in sv issue #9); Unity/Burst hosts compile the same `delegate* unmanaged` signature natively via `BurstCompiler.CompileFunctionPointer` and pay no such gate. The maker signature stays fixed (portable-surface rule); a batched emitter may be added later only if a real scenario demands near-dense emission, as a versioned contract change.

## Portable-surface rule (from Amendment B)

Unity hosts are C# 9: `static abstract` interface members are .NET-only wrappers.
The portable, Burst-compatible core for all walkers and emitters is
`delegate* unmanaged<int, int, T>`; Unity falls back to the fnptr core or mask-level
escapes (`WalkNonZero`). Same pattern tl.unity's fork uses for the same reason.
