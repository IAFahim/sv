# Issue 0002 — The North-Star Fused Test (acceptance gate)

Status: closed (2026-11-26, execution record of the sv 2.1 engine, removed in db8dbac on 2026-11-14)

Seating: this repo owns the widest surface, so the fused test lives here — CI (.NET)
and Unity Test Runner (EditMode) once the adapter atom lands. Filed as GitHub issue #2
(open — acceptance gate). The spec below is the bus copy, vendored verbatim from
`agent-bus/contract/NORTH-STAR.md`.

---

# The North-Star Fused Test

The executable definition of "the three systems blend". If this test is green on both
hosts, the composition is real; if it cannot be written, a seam is a lie and we fix
the design, not the test.

## Shape

One headless tick loop, one entity population, three ticks, then a ring replay of all
three ticks. Assert **bit-identical** state and event streams:

1. on .NET (CoreCLR) versus Unity (Burst 2.0, tl.unity host), tick by tick, and
2. live run versus ring replay (rollback determinism).

Every float comparison is `BitConverter.SingleToInt32Bits` equality (NaN counts as NaN).

## The tick (clause 7 order, all three systems exercised)

```
warmup:  load + bake jump.tlb, bind all timeline ids, BindOutput(MoveSpeed, JumpPower),
         schema build, board create, spawn entities (Frent/.NET, Entities/Unity)

tick:    input.Capture(axesBits, buttons)          // clause 11: raw uint bits
         input.UpdateEdges()                        // pressed = down & ~prev
         drain previous events                      // handlers: board.AddModifier, RunIntrinsicsChunk
                                                       (clamped writes; applied deltas recorded)
         board.Recalculate()                        // clause 3+4: canonical fold → broadcast
                                                       (MoveSpeed, JumpPower columns refreshed)
         host zeroes tl effect columns              // clause 13b
         Timeline<JumpTrack, JumpClip>.Apply(...)   // accumulate (+=), reads JumpPower column
         Timeline.Apply / Advance                   // authored motion, quantized ticks
         kernel.RunChunk(position, velocity, 0, n)  // derived motion, reads stat columns
                                                       (fall kernel: if/clamp + select path)
         events.EmitFromBools(landed, ...)          // clause 6+9: normalize → mask → tzcnt runs
         events.EmitFromNonZero<ManaGained>(applied, ...)   // fnptr core
         swap
```

## Assertions per tick

- Positions/velocities bit-identical across hosts (strict two-step: clauses 1–2).
- Stat columns bit-identical after broadcast (clause 3 order).
- Event stream: same types, same slot order, same payloads, in clause-7 order.
- Disabled-entity invariance: with an enableable component present, entities masked
  off are bit-untouched (fixtures from `mask-fixtures.json`).
- Replay: `input.Replay(fromTick)` re-runs ticks 1–3 from ring bits; full state + event
  streams bit-identical to the live run (clauses 11 + 7).

## Seating

- Lives in the stats library repo (it owns the widest surface), as issue #2 — acceptance
  gate for the atoms, run on CI (.NET) and in-editor (Unity Test Runner, EditMode).
- Vendored oracle: `Kernels.Testing.StrictFloat` (NoInlining double-emulated ops).
  Never compare against inline arithmetic in-editor (Mono contracts/promotes).
- The Unity leg re-verifies tl.unity fork parity (clause 15's cousin: fork drift is
  what this leg catches, on purpose).

## Known traps this test must encode (from empirical sessions)

1. First Burst execution after a recompile runs managed fallback — schedule a warmup
   job or `CompileSynchronously = true`, plus a `[BurstDiscard]` canary asserting Burst
   actually ran (the canary pattern ships in ecs-kernels' FallBurstEquivalenceTests).
2. Editor tests in package `Tests/` folders are not discovered by consuming projects —
   seat the Unity leg in Assets or a `samples~` import.
3. `Simulation.asmdef`-style references must include `Unity.Collections` on Entities 6.7
   (DC0061) — the facade won't catch it; only a real editor will.
