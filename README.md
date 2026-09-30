# sv — state variables

One noun — dense columns over entity slots. Three verbs over it: **tl** (authored
motion), **ecs-kernels** (derived math), **sv** (settings/state: stats, intrinsics,
events, input).

`sv` is the standalone state library negotiated on the agent-bus: float **stats**
(recalculated, modifier-driven), int **intrinsics** (directly written, clamped),
**events** (condition-column ingestion), and **input** (bit-captured axes, button
edges, replay ring). Entity-free at the core; ECS hosts adapt through one chunk shape.

- Contract: **The Composition Contract v1.0.3** below, vendored verbatim
  ([local copy](contract/CONTRACT-v1.0.3.md); merge point:
  `agent-bus/contract/CONTRACT.md`). Negotiated under the name "board"; shipped as
  `sv` — same library, owner's pick. Changes are bus messages first, then a version
  bump at the merge point; the full changelog lives in the vendored header.
- Verification: clause conformance is proven by the north-star fused test
  (`contract/NORTH-STAR.md`, seated as [issues/0002](issues/0002-north-star.md)) plus
  this repo's own suite; mask/run semantics by `contract/mask-fixtures.json`
  (extracted bit-exact from ecs-kernels editor-verified facts — the fleet conformance
  set). The north-star leg consumes the published tl pair only (Tl.Runtime /
  Tl.Gen.CSharp / tlb 1.3.0). Since atom 13 the harness rides `Sv.Compose`'s
  `Frame.Run`; bit-identity to the pre-migration manual loop is pinned by baked
  digest constants (`MigrationTests`), and whole-run determinism rides the tape
  (`Record`/`Playback`: per-frame digest always compared, full body byte-compared
  under `SV_CHECKED`).
- Usage: [sv-examples](https://github.com/IAFahim/sv-examples) — five runnable
  samples (core stats/intrinsics, events, input replay, the Frent adapter, and the
  frozen-frame composition with whole-run replay bit-identity), each printing a
  zero-managed-allocation receipt on its warm path. This repo also carries
  `samples/Sv.Samples` — three limit-pushing showcases (one million slots under every
  core verb; 100k entities on the composed frame with tape record → bit-identical
  playback; 100k Frent entities fragmenting and healing through `Reconcile`):
  `dotnet run -c Release --project samples/Sv.Samples`.

## The Composition Contract — v1.0.3

Clause index (17), verbatim from the vendored master copy:

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

**Portable-surface rule** (from Amendment B):

Unity hosts are C# 9: `static abstract` interface members are .NET-only wrappers.
The portable, Burst-compatible core for all walkers and emitters is
`delegate* unmanaged<int, int, T>`; Unity falls back to the fnptr core or mask-level
escapes (`WalkNonZero`). Same pattern tl.unity's fork uses for the same reason.

## Performance receipts

`benchmarks/Sv.Bench` (100k rows/frame, AVX2): intrinsics chunk 5.9–6.8 M rows/s
(bandwidth-bound, 100% of samples inside `Simd.ChunkW256`); stat clamp 3.2 M rows/s
(the clause-16 pinned conversion costs ~1.9x vs constant clamps); enabled run-walk
1.45 M rows/s after inlining `Clamp.Apply` (+81%); stat add+remove+recalculate 121 ns;
input capture 9 ns; every scenario verified against a scalar reference and prints
`alloc=0B`. Post-atoms-11–13: the composed one-call frame (`frame-run`, sv-owned
columns) runs at parity with the hand-rolled fused tick (~0.85–0.93 ms/frame at
100k rows — the composition layer itself is free, and sv-owned broadcast edges it
ahead of the external-column loop); the determinism tape adds ~0.6–1.0 ms/frame at
100k rows (FNV-1a digest + body over ~1.2 MB of slot space, streamed to disk);
both print `alloc=0B`. Standing `NoAllocationTests` assert exact-zero managed bytes for every
warm verb; the managed-state audit shows zero managed collections outside
`Schema` (cold authoring). Known .NET-only cost: the `UnmanagedCallersOnly` entry
thunk charges ~10 ns per event `make` call (~29% of a dense-emit frame); ruled a
contract cost (clause 17) — events are sparse signals, dense payloads belong in
columns. Unity/Burst hosts compile the same `delegate* unmanaged` signature natively.

<!-- bench:begin (auto-generated — CI refreshes this on every push to main; do not edit by hand) -->

Last refreshed 2026-09-30 14:46 UTC · `main@3cb63be` · AMD EPYC 7763 64-Core Processor (2 cores) · sv-bench rows=100000 vector256=True vector128=True

| Scenario | µs/frame | rate | Managed alloc |
|---|---:|---:|---|
| intrinsics-uniform | 50.507 | 1.98 M rows/s | 0 B |
| intrinsics-varying | 47.989 | 2.08 M rows/s | 0 B |
| intrinsics-statclamp | 101.731 | 0.98 M rows/s | 0 B |
| enabled-runwalk | 224.939 | 0.44 M rows/s | 0 B |
| stat-recalc | 0.266 | 266 ns/op | 0 B |
| events-emit | 953.337 | 0.10 M rows/s | 0 B |
| input-capture | 0.020 | 20 ns/op | 0 B |
| fused-tick | 1567.495 | 0.06 M rows/s | 0 B |
| frame-run | 1539.505 | 0.06 M rows/s | 0 B |
| tape-digest | 2580.954 | 0.04 M rows/s | 0 B |

<!-- bench:end -->

## Atoms

| # | Atom | Status |
|---|---|---|
| 1 | Schema + Board slots + `Clamp` (Constant/Columns) + `RunIntrinsicsChunk`/`RunEnabledChunk` (+ `Span` twins), ternary clamps, applied-delta — [issues/0001](issues/0001-atom-1.md) | done |
| 2 | Stats: insertion-id modifiers, content-deduped sets, canonical fold, `BindOutput` broadcast, `Clamp.Stat`, StrictFloat oracle vendored | done |
| 3 | Event streams: pooled double buffer, parallel writer, fnptr walkers, mask conformance via `contract/mask-fixtures.json` | done |
| 4 | Input: bit-captured axes, button edges, replay ring | done |
| 5 | SIMD pass: vector chunk clamps, scan words, ANDNOT edges — width-invariance receipts, pinned stat-bound float→int (NaN→0, saturate), zero FMA | done |
| 6 | Adapter: Frent chunk seam (`sv.frent`, SvSlot mapping, zero-copy contiguous fast path, Pull/Push fallback) + Unity 6.7 handoff record | done |
| 7 | North-star fused test — .NET leg complete: stage A ([issues/0008](issues/0008-atom-7.md)) + stage B real kernel bit-identical ([issues/0010](issues/0010-atom-7-stage-b.md)); Unity cross-host handed to the integrator ([issues/0015](issues/0015-atom-12.md), [issues/0016](issues/0016-atom-13.md)) | done (.NET) |
| 8 | Perf: benchmark harness, perf receipts, zero-allocation tests, managed audit, run-walk inlining (+81%) ([issues/0009](issues/0009-perf-bench.md)) | done |
| 9 | Binding generations + sv-owned output columns: `BindExternal` handle, `InvalidateBindings` stale-throw (checked), `OutputPtr`/`Output` self-owned broadcast — [issues/0012](issues/0012-atom-9.md) | done |
| 10 | Slot renumbering: `Board.RenumberSlots`, `Sv.Columns.Permute`, `Sv.Frent.Population` fused chunk windows + `Reconcile()` 64-aligned healing — [issues/0013](issues/0013-atom-10.md) | done |
| 11 | `Sv.Compose`: Game/GameHost/`Frame.Run` frozen-frame composition (clause 7 verbatim), static-generic step dispatch, tl naming seam absorbed, 0 B warm — [issues/0014](issues/0014-atom-11.md) | done |
| 12 | `Sv.Unity` adapter (com.sv.unity): SvSlot/UnityPopulation/SvChunk + svSystemBase pattern; .NET facade tests, editor leg is the integrator handoff — [issues/0015](issues/0015-atom-12.md) | done (.NET) |
| 13 | North-star on `Frame.Run`, bit-identical to the pre-migration manual digests (baked oracle test); determinism tape `Record`/`Playback`; sv-owned outputs replace the NativeMemory blocks — [issues/0016](issues/0016-atom-13.md) | done |

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
