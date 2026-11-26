# Issue 0009 — Perf atom: benchmark harness, perf receipts, zero-allocation proofs, managed audit

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Filed as GitHub issue #9 (closed — execution record). Labels: `ai`, `area:core`, `kind:perf`.

## Shipped surface

- `benchmarks/Sv.Bench` — console harness in the tl `benchmarks/Alpha` style. Eight
  scenarios, each with (a) a scalar-reference verify pass consumed before timing
  (vector path vs `Simd.ChunkScalar`/reference walk memcmp, event-count expectations,
  edge references, fused-tick anchors), (b) 5-batch min-of timing at calibrated
  iteration counts, (c) per-thread managed-allocation delta
  (`GC.GetAllocatedBytesForCurrentThread`) measured around the frame loop only —
  setup/teardown excluded. Single-scenario mode for `perf`. Uses
  `InternalsVisibleTo(Sv.Bench)` + the shared StrictFloat oracle source link.
- `tests/Sv.Tests/NoAllocationTests.cs` — standing zero-allocation receipts: 2000
  warm iterations per verb family; assert managed delta == 0 exactly.
- `Clamp.Apply` now `AggressiveInlining` (perf fix, semantics unchanged — full suite
  green both build flavors before and after).
- `Sv.Frent` aligned to NuGet `Frent 8.0.0-beta` (fleet one-line per integrator
  0016; adapter tests green across the jump, API-identical as audited).
- Vendored contract bump `contract/CONTRACT-v1.0.2.md` (clause 16 = the conversion
  pin).

## Bench table (this box: AVX2/no AVX-512, Vector256 engaged, 100k rows/frame)

| scenario | before | after | throughput (after) |
|---|---|---|---|
| intrinsics-uniform | 16.6–16.9 us | same | 5.9–6.0 M rows/s |
| intrinsics-varying | 14.3–19.4 us | 14.6 us | 6.8 M rows/s |
| intrinsics-statclamp | 31.5–32.1 us | same | 3.2 M rows/s |
| enabled-runwalk | 108–125 us | **69.8 us** | **1.45 M rows/s (+81%)** |
| stat-recalc (add+remove+recalc) | 0.128 us | 0.121 us | ~8.2 M ops/s |
| events-emit (2 scans + ~15k appends + drain) | ~497 us | same | 0.20 M rows/s |
| input-capture (capture + 3 queries) | 9–12 ns | 9 ns | — |
| fused-tick (sv-side full composition) | 933 us | 964 us | 0.10 M rows/s |

All scenarios print `alloc=0B`; `all scenarios ok` exit 0.

## perf receipts (perf 7.2.3, DOTNET_PerfMapEnabled=1, EnableWriteXorExecute=0
for JIT symbol resolution)

- `perf stat -d` events-emit: IPC 1.3, branch-miss rate 0.6%, L1d misses 1.3M over
  the run — not branch-bound.
- intrinsics-uniform `perf record`: 100% of hot samples inside `Simd.ChunkW256` —
  the loop IS the vector kernel. ~12 B/row at 0.166 ns/row ≈ 72 GB/s effective on a
  1.2 MB/frame working set: memory-bandwidth-bound. No compute headroom left in the
  chunk shape; the kernel is done.
- intrinsics-statclamp: 80.8% `ChunkW256` with the `BoundsOf` fixup chain inlined
  and vectorized — the clause-16 pin costs ~1.9x vs constant clamps, paid in vector
  ops, correctness non-negotiable.
- enabled-runwalk BEFORE fix: 42.9% of samples in non-inlined `Clamp.Apply` (per
  set bit). After `AggressiveInlining`: folded into the walk, 124.5 → 69.8 us.
- events-emit: ~29% of the scenario in the `UnmanagedCallersOnly` entry path —
  `IL_STUB_PInvoke` (14.7%) + `__tls_get_addr` (12.1%) + a coreclr helper — i.e.
  the reverse-P/Invoke thunk charges ~10 ns per `make` call on .NET. NOT a bug:
  allocation stays 0; it is the portable fnptr core's .NET tax. Unity/Burst hosts
  compile makers natively (no thunk). Sequential StructLayout on the payload does
  not remove it. Recorded as the #1 remaining headroom item — mitigation would be a
  bus-level API discussion (e.g. a blittable no-make emitter variant), NOT a silent
  change to the settled walker surface.

## Headroom verdict

1. Chunk kernels: bandwidth-bound, no headroom worth chasing on this shape.
2. Enabled run-walk: fixed this atom (+81%); remaining cost is the essential
   per-bit ternary chain + scattered stores.
3. Event `make` thunk: ~29% of a dense-emit frame on .NET — documented, contract
   surface; ruled (see the addendum below), not changed here.
4. Stat clamp: 1.9x vs constant is the pinned-conversion price; games needing
   constant-clamp speed can author Constant clamps where the design allows.
5. Everything else (stat recalc 121 ns add+remove+recalc, input 9 ns) is far from
   frame budgets at 100k entities.

## Managed-type audit (structural receipts)

- `grep -rnE 'new (List|Dictionary|HashSet|Queue|Stack)<|new X[|Select(|Where('
  src/Sv/` → 14 hits, ALL in `Schema.cs` (name→id dictionaries; authoring layer,
  built once, never on a frame path). Zero in Board/Board.Stats/SetTable/
  EventStreams/InputState/Simd/Clamp/Guard.
- Classes in src/Sv: `Board`, `EventStreams`, `Channel<T>`, `InputState`, `SetTable`
  (native-block owner handles — the contract carve-out), `Schema`/`SchemaBuilder`
  (authoring), stateless statics `Simd`/`Guard`. No other reference types.
- Strings only in Schema definitions and cold fail-fast exception messages.
- `delegate |event |lambda |catch` in the warm files: 0 hits (`delegate* unmanaged`
  fnptrs are the mandated exception).
- Zero-allocation tests: Board verbs (chunk ×2, enabled, add/remove modifier,
  recalculate, acquire/release), event verbs (Write, 3 emitters, Swap, drain),
  input verbs (Capture, Down/Pressed/Released, AxisBits, BeginReplay+ReplayStep) —
  all exact-zero managed bytes over 2000 warm iterations each.

## Tier-0 divergence claim — withdrawn (fleet record correction)

The original atom-5 receipt reported scalar `unchecked((int)(-1e30f))` returning the
wrapped value -724917978 vs vector 0x80000000 "tier-0 vs tier-1 on the same
machine". Reproduction attempts (this box): standalone console Release and Debug,
and a fresh xunit test host, all return 0x80000000 on first call and warm. The
integrator independently could not reproduce. Corrected claim: single unreproducible
observation under the original full-suite run's compilation state; the clause-16
pin's justification rests on the confirmed grounds alone — ECMA-unspecified
conversion + x64 (cvttss2si indefinite) vs ARM (fcvtzs saturating) ISA divergence.
The pin stands unchanged.

## Validation

- `dotnet test sv.slnx -c Release` — 93/93 (Sv.Tests, +3 allocation tests) + 4/4
  (Sv.NorthStar). `-p:DefineConstants=SV_CHECKED` — 93/93 + 4/4.
- Frent aligned 0.8.0-beta → 8.0.0-beta: adapter tests green unchanged.

## Ruling addendum (2026-09-28, bus 0021 — recorded here to match the GitHub #9 mirror)

The fnptr-thunk question in headroom item 3 was ruled on the bus and folded as
**clause 17** of the Composition Contract (v1.0.3, vendored at
`contract/CONTRACT-v1.0.3.md`):

- Events are sparse signals; dense per-row payloads belong in broadcast/accumulate
  columns (clauses 4/13) — the ~10 ns `UnmanagedCallersOnly` entry gate per make call
  is accepted as contract cost, and Unity/Burst hosts compile the same
  `delegate* unmanaged` signature natively and pay no such gate.
- A no-make emitter variant was DENIED.
- A batched emitter may be added later only if a real scenario demands near-dense
  emission, as a versioned contract change.

The clause text also carries the carry-over note for the Unity editor leg: keep
`MethodImpl(AggressiveInlining)` on `Clamp.Apply` through any port.
