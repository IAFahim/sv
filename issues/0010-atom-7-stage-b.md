# Issue 0010 — Atom 7 stage B: real ecs-kernels Fall in the fused tick, bit-identical

Filed as GitHub issue #10 (closed — execution record). Labels: `ai`, `area:north-star`, `kind:test`.
Umbrella #2 remains open only for the integrator's Unity cross-host leg.

## Shipped surface

- Durable copy of the refactored ecs-kernels tree: `/home/i/GitHub/ecs-kernels-new`
  (from the integrator's `/tmp/ecs-kernels-new` per bus 0016; `/tmp` is volatile).
  Still the kernel agent's job to publish a remote — this is a local mirror, recorded
  here so the dependency is known.
- `tests/Sv.NorthStar/Sv.NorthStar.csproj` consumes it conditionally: when a sibling
  `ecs-kernels-new` tree exists (override with `/p:KernelsRoot=...`), it adds
  ProjectReferences to `dotnet/Kernels` (runtime) and `generator/Kernels.Generator`
  (as Analyzer), compile-includes the canonical `Simulation` sources
  (`Components.cs`, `Fall.cs`), and defines `KERNELS_PRESENT`. A plain clone without
  the tree builds and runs stage A unchanged. (Note: `$(MSBuildThisFileDirectory)`
  concatenation needs `$([System.IO.Path]::GetFullPath(...))` normalization for
  `Exists()` — recorded, cost an hour.)
- `Kernels.Testing.StrictFloat` adopted as sv's oracle VERBATIM (namespace + method
  names `Add/Subtract/Multiply/Divide/Sqrt`): `tests/Sv.Tests/StrictFloatOracle.cs`
  is now a byte-for-byte copy of the canonical
  `packages/com.kernels/Testing/StrictFloat.cs`; all sv call sites renamed
  (`Sub`→`Subtract`, `Mul`→`Multiply`). The fork-risk the integrator flagged in 0010
  is closed: one text, linked into Sv.Tests/Sv.NorthStar/Sv.Bench.
- Stage-B composition in `NorthStarHarness` (supersedes stage A's scalar-only Fall):
  the tl jump effect folds into VELOCITY (`vy += Y[i] * jumpPowerColumn[i]`, strict
  two-step, JumpPower stat seam), then derived motion runs the REAL generated SIMD
  kernel — `new Simulation.Fall { Dt = input.AxisF(0), Gravity = -9.8f }.RunChunk(
  positions, velocities)` on the live tick state — and a strict scalar fold of the
  exact kernel body (`pos + v*dt`; `v - g*dt` with `g*dt` hoisted exactly like the
  generated code; `pos < 0 → {0,0}`) asserts BIT-IDENTITY per row per tick. Without
  the kernels tree, the scalar fold drives alone (stage A behavior).
  `landed` is derived host-side from the position transition (`posBefore != 0 &&
  posAfter == 0`), feeding the Landed emitter → next-frame drain → modifier chain.

## Acceptance receipts

- `dotnet test sv.slnx -c Release` — 93/93 + 4/4; `-p:DefineConstants=SV_CHECKED` —
  93/93 + 4/4 (kernel branch active in both: the divergence-guard string is compiled
  into Sv.NorthStar.dll; verified via UTF-16 literal scan — plain `strings` misses
  the #US heap).
- Bit-identity: 3 ticks × 4 entities, generated `RunChunk` outputs equal the strict
  scalar fold bitwise (positions and velocities, `SingleToUInt32Bits`); replay from
  the ring remains bit-identical end-to-end WITH the kernel driving.
- Detector proof (sabotage): perturbing the reference gravity fold to `v - dt`
  makes the guard throw (`vel 0.16333334 vs -0.016666668`); restored build green.
- Anchors unchanged from stage A: tick 0 `y = 2f`, all 4 land, mana 40→65, applied
  25; tick 1 move_speed 6f→15f, 4 landed + 4 mana events drained; tick 2 steady.

## Fleet notes

- The canonical Fall zeroes velocity on landing (no restitution) and integrates
  position before gravity; stage A's bounce variant is superseded — the fused test
  now composes the kernel system's shipped semantics, which is what the Unity leg
  will run.
- StrictFloat equivalence caveat (documented in the kit itself): double-emulated
  ops can double-round vs single-precision IEEE in rare ULP-boundary cases
  (~2^-29); for this scenario's magnitudes the two agree on every operation — the
  empirical receipt is the bit-equality assert itself.
- Remaining for issue #2: the integrator's Unity 6.7 editor leg (handoff record in
  issues/0007; sv.unity does not exist yet).
