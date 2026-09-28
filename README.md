# sv — state variables

One noun — dense columns over entity slots. Three verbs over it: **tl** (authored
motion), **ecs-kernels** (derived math), **sv** (settings/state: stats, intrinsics,
events, input).

`sv` is the standalone state library negotiated on the agent-bus: float **stats**
(recalculated, modifier-driven), int **intrinsics** (directly written, clamped),
**events** (condition-column ingestion), and **input** (bit-captured axes, button
edges, replay ring). Entity-free at the core; ECS hosts adapt through one chunk shape.

- Contract: **The Composition Contract v1.0** below, vendored verbatim from
  `agent-bus/contract/CONTRACT.md` (merge point). Negotiated under the name "board";
  shipped as `sv` — same library, owner's pick. Changes are bus messages first, then a
  version bump at the merge point.
- Verification: clause conformance is proven by the north-star fused test
  (`contract/NORTH-STAR.md`, seated as [issues/0002](issues/0002-north-star.md)) plus
  this repo's own suite; mask/run semantics by `contract/mask-fixtures.json`
  (extracted bit-exact from ecs-kernels editor-verified facts — the fleet conformance
  set).

## The Composition Contract — v1.0

1. **Strict two-step IEEE float policy**; no FMA anywhere (grep-enforced); StrictFloat oracle vendored.
2. **Clamps are ternary/select** (compare+blend); never Min/Max on floats; ints may use Avx2.Min/Max.
3. **Canonical aggregation order**: (op-kind, insertion id) strict left fold, scalar per set; vertical-only vectorization, width-invariance receipts required.
4. **Recalc ends in a broadcast** into bound columns; per-entity board reads are tooling-only; no gathers.
5. **Dense slots**; adapter owns entity→slot at create/destroy; single chunk shape `RunChunk`/`RunEnabledChunk` (+ `Span` twins).
6. **Condition ingestion by run-walk** (tzcnt, `length == 64` guard); semantics conform to `mask-fixtures.json`.
7. **Frozen frame order**: input edges → drain prev events → stat recalc + broadcast → tl Apply/Advance → kernels → condition run-walk → write events → swap. Mid-frame structural changes apply next frame. Intrinsics writes happen inside event-drain handlers (step 2) and condition responses (step 6), and both read frame-frozen bounds from the previous broadcast. *(The last sentence is the explicit intrinsics home, requested on the bus post-v1.0; pending fold into the merge-point copy as v1.0.1.)*
8. **Runtime dependencies: none**; tl and ecs-kernels are test-only references.
9. **Bool normalization**: any nonzero byte = true; compare-≠0 → movemask before any bit trick; arithmetic OR of raw bytes banned.
10. **Clamp bounds**: Constant / Columns / Stat; Stat resolves at construction to the bound output column; bounds are frame-frozen; no clamp–modifier cycles.
11. **Axis bit-capture**: raw uint bits in the ring; replay is bit-verbatim.
12. **Pointer stability**: bound output pointers (`BindOutput`) and `Clamp.Stat`-captured columns are stable from warmup until an explicit rebind; any storage growth forces re-resolve before the next op; use-after-rebind detected in checked builds.
13. **tl effect columns accumulate** (`+=`): (a) one column, one writer kind — store-writers (stats broadcast) and accumulate-writers (tl) never share a column; (b) the host zeroes each tl effect column once per frame, immediately before the timeline step of clause 7; (c) tl's position in the frozen order fixes the add order.
14. **Warmup before parallel frames**: all timeline ids loaded and first-use folds completed during warmup; first-use inside a warm frame is a contract violation, asserted in checked builds.
15. **Stats is float/int only; no double effect columns** (tl's double combine branch never exercised); any shipped `IBlend` follows clauses 1–2 and prefers pre-folded tables so Blend never runs at runtime.

**Portable-surface rule** (Amendment B): Unity hosts are C# 9 — `static abstract`
interface members are .NET-only wrappers. The portable, Burst-compatible core for all
walkers and emitters is `delegate* unmanaged<int, int, T>`; Unity falls back to the
fnptr core or mask-level escapes (`WalkNonZero`). Same pattern tl.unity's fork uses for
the same reason.

## Atoms

| # | Atom | Status |
|---|---|---|
| 1 | Schema + Board slots + `Clamp` (Constant/Columns) + `RunIntrinsicsChunk`/`RunEnabledChunk` (+ `Span` twins), ternary clamps, applied-delta — [issues/0001](issues/0001-atom-1.md) | done |
| 2 | Stats: insertion-id modifiers, content-deduped sets, canonical fold, `BindOutput` broadcast, `Clamp.Stat`, StrictFloat oracle vendored | done |
| 3 | Event streams: pooled double buffer, parallel writer, fnptr walkers, mask conformance via `contract/mask-fixtures.json` | — |
| 4 | Input: bit-captured axes, button edges, replay ring | — |
| 5 | SIMD pass: broadcast stores, ANDNOT edges, run-walk — width-invariance receipts; compare+blend clamps only; zero FMA | — |
| 6 | Adapter: single chunk API (Frent IJobChunk shape, Unity Entities 6.7) | — |
| 7 | North-star fused test ([issues/0002](issues/0002-north-star.md)) — acceptance gate | — |

Atom 1 scope note: `Clamp.Stat` and the StrictFloat oracle land with atom 2 (the first
float math); the walkers land with atom 3. Nothing ships half-wired.

## Discipline

Mirrors tl's `AGENTS.md`: issues carry the spec, atoms are short and green before the
next starts, receipts (commands + outputs) are recorded in the issue, production code
carries no explanatory comments, the data path is unmanaged, and every public
abstraction pays for itself. GitHub issues are the record; `issues/` keeps the
authored copies.

Before starting and before finishing any atom, check the agent bus:
`/home/i/GitHub/agent-bus/bin/check stats --body`. Reply and ack per
`/home/i/GitHub/agent-bus/README.md`. Cross-repo decisions never live only in chat.
