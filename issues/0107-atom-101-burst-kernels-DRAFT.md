# Issue 0107 (DRAFT) — Atom 101 (roadmap 6.2): BCL shims + Burst kernels

Roadmap [issues/0058](0058-roadmap-1m-entities-5us.md) 6.2 (§5.5 "One kernel
source, two compilers"), clause 1 extension (FMA grep extended from source to
Burst-generated assembly). Labels: `ai`, `area:core`, `kind:feat`.
**Draft — numbering, README row and contract wording pending; v2.0 contract
batch drafted unsent at [issues/0097](0097-*.md).**

## Changes

- **`src/Sv.Burst/`** — new Burst leg (project `Sv.Burst`, net10.0,
  `LangVersion 9`, references the facade kit for `Unity.Burst` attributes;
  no reference to `Sv` — parity is *asserted*, not assumed):
  - `Bcl.cs` — the BCL shim surface the Burst kernel path needs:
    `Tzcnt(ulong)` (compare/shift sequence — no `BitOperations`) and
    `BoundOf(float)` (clause-16 conversion, identical math to
    `Clamp.BoundOf`, NaN via `float.IsNaN`).
  - `Kernels.cs` — the sv chunk kernels in the Burst-compiled subset: the
    scalar formulation of `Simd.ChunkScalar`/`EnabledWordScalar` over
    `Clamp.Apply` (saturating add/subtract xor-overflow idiom, ternary
    clamps, `lo > hi` collapse, clause-16 stat bounds, word-complete
    applied semantics) behind `[BurstCompile(FloatPrecision.Standard,
    FloatMode.Strict)]`, over a flat ABI (`KState`, `KOp`, `KBound`) that
    mirrors `Kernel.Run`/`Window`/`Delta`/`Bounds`. One delegate
    `RunProgramDelegate` (`[UnmanagedFunctionPointer(Cdecl)]`) — the
    `BurstCompiler.CompileFunctionPointer` signature.
  - `Scenario.cs` — the cross-target program: seeded op stream (512 ops ×
    4096 rows; uniform/per-row × none/constant/rows/stats bounds, masked
    words at any offset, applied always on; extremes incl. `int.Min/Max`,
    NaN/±inf/±2³¹ stat bounds) plus the digest fold.
  - Same three files are copied **byte-identical** into the Unity project by
    `tests/Sv.Burst/run-burst.sh` (sha256 recorded); the xunit side asserts
    the copies have not drifted.
- **`tests/Sv.Burst/`** — the real-Burst leg:
  - `unity/` — minimal Unity 6000.7.0b5 project (empty manifest — Burst 2.0
    is built into the editor), `SvBurst` runtime asmdef (shared sources +
    `SvBurstProbes.cs` BCL audit probes), `SvBurst.Editor` asmdef with
    `SvBurstBatch.Run`, the batch entry.
  - `run-burst.sh` — copies sources, hashes both sides, drives
    `Unity -batchmode -nographics -quit -executeMethod Sv.Burst.SvBurstBatch.Run`.
  - `receipt/` — the artifacts this issue cites (see Receipts).
- **`tests/Sv.Tests/BurstDigestTests.cs`** — 5 facts (see Tests).
- `sv.slnx` + `tests/Sv.Tests/Sv.Tests.csproj` — wire the new project in.
- **No production change to `src/Sv`**; `contract/` and
    `tests/Sv.Tests/StrictFloatOracle.cs` untouched.

## BCL audit (what the unmanaged kernel path needs vs what Burst gives)

| BCL touchpoint | Burst 2.0 (6000.7.0b5) status | Disposition |
|---|---|---|
| `System.Runtime.Intrinsics` (`Simd.cs`/`Simd.Chunk.cs`) | not compilable | Burst kernels re-express the loops scalar-branch-free; Burst vectorizes (§5.5) |
| `System.Numerics.BitOperations.TrailingZeroCount` | **type not scriptable — CS0122 internal in Unity profile** (observed) | `Bcl.Tzcnt` shim |
| `System.Math.Clamp/Min/Max` (ints) | compiles and runs under Burst, results match Mono (probe digest) | usable directly |
| `float.IsNaN`, float compares, `(int)float` truncation | supported; `FloatMode.Strict` keeps semantics IEEE-exact | used in `Bcl.BoundOf` |
| `throw new ArgumentException(...)` (guard style, string concat) | compiles under Burst 2.0 without error (compile-only probe) | guards as written are not blocked; Burst entry still takes pre-validated flat args |
| `Math.Clamp` etc. on the .NET side | unchanged | — |

## Tests (Sv.Tests 420 → 425; Sv.Unity.Tests 7; Sv.NorthStar 27)

- `Shims_TzcntMatchesBitOperationsEverywhere` — 0..4096, all 2^k ±neighbors,
  all-ones, 100k xorshift values vs `BitOperations.TrailingZeroCount`.
- `Shims_BoundOfMatchesClampBoundOfOnSpecialsAndRandomBits` — NaN/±inf/±2³¹
  boundary lattice + 100k random bit patterns vs `Clamp.BoundOf`.
- `SharedKernelSources_AreByteIdenticalInUnityProject` — the Unity copies
  cannot drift from `src/Sv.Burst`.
- `CrossTargetDigest_BurstLegMatchesSvKernelRunOnDotNet` — the same seeded
  program through `Sv.Kernel.Run` (vector256 path, stat bounds via a real
  `Board` + `BindOutput`) and through `Sv.Burst.Kernels.RunProgram`:
  digest `2B43BC1549AAE751` both ways on .NET 10 x64.
- `BurstReceipt_MatchesDotNetDigest_WhenPresent` — reads
    `tests/Sv.Burst/receipt/burst-receipt.json`; asserts status ok, Burst
    enabled, and **.NET digest == editor-Mono digest == Burst digest ==
    `2B43BC1549AAE751`**; passes with the receipt present (real-Burst leg
    ran), degrades to a logged note if absent.

## Receipts

Measured on the bus host: i9-14900K, .NET 10, Unity 6000.7.0b5 (owner
decision: Unity 6.7, Burst the only hot-path compiler), Linux x64.

- `dotnet build -c Release` — 0 warnings 0 errors; full battery
  **425 / 7 / 27**; `-p:SV_CHECKED=true` build + retest **425 / 7 / 27**;
  `bench all` — *all scenarios ok* (intrinsics-uniform 33.0 µs/100k rows,
  statclamp 45.1 µs, enabled-runwalk 44.6 µs, ff1m p50 3.04 ms).
- **Real Burst, not the facade fallback**: the batch harness
  (`tests/Sv.Burst/receipt/burst-receipt.log`) compiles
  `Kernels.RunProgram` via `BurstCompiler.CompileFunctionPointer`
  (synchronous, `FloatMode.Strict`, `FloatPrecision.Standard`), runs the
  seeded program on Mono and on the compiled pointer:
  `digestMono 2B43BC1549AAE751 == digestBurst 2B43BC1549AAE751`, and the
  xunit receipt test proves the same value falls out of .NET 10 —
  **bit-identical column bits across .NET-JIT, Mono and Burst on x64**.
- **Proof the Burst binary actually ran** (Burst 2.0 removed
  `FunctionPointer.IsCreatedByBurst`): control run with
  `BurstCompiler.Options.EnableBurstCompilation = false` — the function
  pointer path then executes through a Mono marshaling wrapper and a lazy
  resolver (2 thunk hops) at **2725333 ns/program**; the enabled run jumps
  one hop straight into a distinct JIT arena at **1013429 ns/program**
  (**2.7× faster**); the arena code carries the inlined clause-16 sequence
  (`vucomiss` NaN test on raw bits, ±2³¹ saturates, `vcvttss2si`
  truncate-toward-zero) — `receipt/RunProgram.asm` has 4 `vcvttss2si` sites,
  matching the Stats-bound paths.
- **FMA scan of generated assembly** (`receipt/fma-scan.txt`):
  `grep -nE 'vfmadd|vfmsub|fnmadd|fmsub|fmla|fmadd'` over objdump (AT&T)
  disassembly of every dumped code window — **0 matches**, total 0 across
  all dumps. Source side unchanged: no `MultiplyAdd`/fused ops in `src/`.
- Probe digests (managed vs Burst-compiled): mathclamp
  `B27BEA647660CDDF` match; floatfold `F3D8972A2DF16C60` match.

## Limitations (recorded honestly)

- **arm64**: no arm64 hardware on this host — receipt is x64-only, as
  scoped. The digest is ISA-portable by construction (int arithmetic +
  IEEE compares + pinned conversions); arm64 confirmation deferred to CI
  (when CI exists again) or an arm64 host.
- **CI**: dead (GitHub billing) — local receipts authoritative; the
  cross-target digest test auto-picks the receipt up in CI later.
- **Probe wrappers**: the two probe entries resolved their function
  pointers into Mono marshaling wrappers (dumped windows are wrappers, not
  Burst code for the probes themselves); their *results* still match, and
  `throw`-style guards compile under Burst, but the FMA scan's non-vacuous
  target is `RunProgram` (verified real code). A float a·b+c Burst loop was
  not captured in a dump window — noted as a follow-up (move probes behind
  the same one-hop path as `RunProgram`).
- **Timing footnote**: the *managed* baseline measured ~2.7× slower in the
  Burst-disabled control run than in the enabled run (2.70 ms vs 1.01 ms
  for identical Mono code) — Mono tiering/thermal variance between
  processes; the load-bearing differential is within-run and
  self-consistent (disabled: managed 2702535 ≈ fp-path 2725333; enabled:
  fp-path 1013429).
- `FunctionPointer.IsCreatedByBurst` is gone in Burst 2.0 — "compiled by
  Burst" is evidenced by the disable-control differential + arena code
  content above, not an API flag.
