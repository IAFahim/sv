# sv v3: one-file brief for the senior (design a compiled, step-by-step, rewindable game-state engine)

Prepared 2026-11-12. This is ONE self-contained file: the requirements, a worked example, what exists today, and the actual source code
of everything relevant (appendices). You cannot browse or run anything; everything you need is in this file.

## 0. How to read this file

- **OWNER** = the owner's intent, from their own explanation (the examples are theirs). **FACT** = read from the code or docs in the
  appendices. **READING** = the writer's interpretation. **ILLUSTRATION** = numbers made up to show a rule.
- The writer (an assistant managing agents) does NOT fully understand the owner's game-state problem. Do not trust the READING lines.
  Do not invent requirements. Where something is missing, say you do not know and state your assumption. You are not asked what the
  owner wants in general: it is written below. Point out specific gaps and contradictions.
- The deliverable is a PLAN written as `plan.md` (section 7). The owner wants the foundation and source-code examples for how
  things should look; the assistant then manages agents who build from your plan.

## 1. In one paragraph (OWNER)

A game-state engine that runs outside any game engine (plain .NET and browser WASM; Unity 7 is only one host) and works closely with
tl (timelines) without depending on it. Every entity holds all of its stats, intrinsics and events at fixed offsets in contiguous
memory, so a read is O(1) by generated enum, with no dictionary and no strings. Everything that happens to an entity during a frame is
STORED (not applied, not discarded), and calculated in layers at the start of the next frame. Rules, gates and priorities are authored
by designers and compiled at build time into straight, fast code. The world can be rewound by snapshots (for example 32 worlds) for
fair server-side lag compensation. It must be very fast and very small at 1,000,000 entities, exact, and deterministic.

## 2. Requirements (numbered so you can refer to them)

R1. **Values are fixed-offset and contiguous.** Every entity has ALL stats (float), intrinsics (number) and events, each with a
    designer-set default. 100 stats + 100 intrinsics + 100 events means everyone has all 300, and an entity with nothing special holds
    defaults. The generated enum IS the offset. O(1) access. Adding an enum value stays O(1). No strings in gameplay code.
R2. **Stats and intrinsics relate.** A stat can be the max or min of an intrinsic (max health is a stat, current health an intrinsic).
    A damage type is itself a stat or intrinsic on the entity: physical, healing, life, poison, electric, light, fire, bleeding, ...
R3. **Contiguous and copyable.** All of an entity's values sit together so whole chunks can be saved, loaded and unloaded instantly,
    and so SIMD and caches are used well.
R4. **Designers define everything, with no code per value.** In Unity and in a web tool (C# as WASM) a designer can add as many
    stats, intrinsics and events as they want; enums and code are generated. The web app also lets them SIMULATE. A .NET host
    exists too.
R5. **Inputs are stored, not applied.** When something affects an entity in frame N (a bullet hit), the effect is stored with its
    details. It is calculated at the start of frame N+1, because systems can run at any moment and many things can arrive in one frame.
R6. **Each stored effect keeps its source.** It records how much damage per type, how much it consumed, WHICH ENTITY it came from, and
    what that source is (for example, the shooter is a sniper). Nothing is lost; later layers group by source and by type.
R7. **Layers process stored records, step by step**, each layer for all entities before the next, like Unity systems and jobs:
    accumulate per type and per source; gates by type; gates by source; reflection; triggered effects; apply; events. Checks sit
    between layers. (The order of layers is a READING: correct it.)
R8. **Gates and rules.** Examples: "physical damage is not allowed" drops every physical record. "I am immune to snipers" drops
    EVERYTHING from a sniper's bullet, including the poison it also carried. "If immune to damage, do not add." "Cursed: healing is
    blocked." "Cannot heal while double jumping." "Another card heals twice while jumping": the two rules conflict, so a PRIORITY
    number decides and the higher one wins. Chains can have chains (is any blocking chain active for health?). Items and cards add or
    remove checks. Immunity is just a number or bit mask (5 = bits 101: immune to damage and to something else; 5 times immune).
R9. **Reflection and complications.** A card says "when I take damage, the shooter takes the same". Possible only because the source is
    known. A layer reads all the entities that damaged me and writes the same amount back to them (what I did not take is added to
    them). I can be immune and still reflect. The shooter may also be immune or may also reflect. The owner expects a REMOVE pass that
    removes records instead of looping add passes forever. You design the exact scheme (bounded, terminating, deterministic).
R10. **Triggered effects.** "Every damage you take, you heal 5": a later layer.
R11. **Compile time.** Rules (with items and cards) are created at game development time and COMPILED DOWN; about 10,000 rules
    across stats, intrinsics and events; no lookup tables; as close to machine code as possible; over a million entities at once.
R12. **Timelines.** A designer puts ticks on a tl timeline at some duration; each tick changes a value (poison). tl does timelines
    today; the design must fit it.
R13. **Entities beyond players.** A player has a hand with its own values (health, healing); the hand's healing goes to the player's
    health; a shield's curse goes to the player. Any entity can pass to any other entity and read another's values (a player checks
    another player's health). A job resolves entity-to-entity passes in the simulation. Designers do not build a targeting or linking
    system; any entity can simply pass to any other.
R14. **Rewind by snapshots.** A dead entity's state is stored, not deleted, and deleted after a while. If two players shoot each other
    together and one has a very high ping, the world goes back to a snapshot (for example one of 32 worlds) and simulates forward.
    Not frame-by-frame rewind. Cheap, performant, and only a few stored values.
R15. **Stateless, perfect, step by step.** Layers do all their work, then the next layer.
R16. **Speed and memory.** Very fast, very low memory at 1,000,000 entities; storing everything stupidly would grow too much.
    Tension to design explicitly: "store everything that affects an entity" versus "very low memory".
R17. **Independence.** Works closely with tl but does not depend on tl at all, and does not depend on Unity (no Unity lookup tables).
    Unity 7 is a host. The owner wants to learn and understand it fast: steps and layers must be easy to follow.
R18. **Constraints we already hold** (keep unless you justify): deterministic and bit-identical across machines, worker counts and
    SIMD paths; no FMA; zero warm allocation; integer-only crossing between entities unless you show a deterministic float route; the
    Unity side is C# 9 / Burst. Backward compatibility is NOT required (rename, move, delete freely). Pinned digests stay unless you
    explain a bump.

## 3. Worked example (ILLUSTRATION: numbers invented, rules are the owner's)

Player P, health 100 (max 100). Frame 10, three bullets arrive. They are STORED, not applied:

| rec | target | source | source kind | type | amount |
|---|---|---|---|---|---|
| 1 | P | S1 | sniper | physical | 40 |
| 2 | P | S1 | sniper | poison | 10 |
| 3 | P | S2 | rifle | physical | 20 |
| 4 | P | S2 | rifle | bleeding | 5 |
| 5 | P | S3 | rifle | healing | 15 |

P carries: card A "immune to sniper" (source-kind gate), card B "when damaged, the shooter takes the same" (reflection), card C
"every damage you take heal 5" (triggered). Frame 11 start, layers run in order:

1. Accumulate by type: physical 60, poison 10, bleeding 5, healing 15. By source: S1 = {physical 40, poison 10}, S2 = {physical 20, bleeding 5}, S3 = {healing 15}.
2. Gate by type: nothing forbids a type here.
3. Gate by source: card A drops every record of S1, both physical 40 AND poison 10. Remaining: recs 3, 4, 5.
4. Reflection: for each source that damaged P, write the same damage back: S2 receives physical 20 and bleeding 5 as new stored records for S2's own next calculation (S3 only healed, nothing to reflect).
5. Complications (the owner's point): if S2 is immune to damage or reflects too, S2's gates and reflection run in the same scheme and must terminate; the owner expects a remove pass over records.
6. Triggered: card C heals 5 per damage taken.
7. Apply: health = 100 - (20 + 5) + 15 + 5 = 95 (ILLUSTRATION; whether healing applies before or after damage is yours to decide and justify).
8. Events: for example "health below 30 percent" derived from the result.

Notice: source and source kind must be stored per record, layers must group by source and by type, and every layer is a separate step that
finishes for all entities before the next. All of it must be compiled and fast for 1,000,000 entities.

## 4. Time and rewind

All stored inputs of frame N are consumed in frame N+1 (READING: a per-frame double buffer). Snapshots: a ring of worlds (the owner
said "32 worlds"). Lag compensation goes back to a snapshot and simulates forward from there. Dead entities are retained for a while for
this. How inputs are kept for resimulation, and the snapshot cost at 1,000,000 entities, is yours to design.

## 5. What exists today (FACT; code in the appendices)

| Piece | What it does today | Gap against the requirements |
|---|---|---|
| sv `Board` (appendix C) | slots, float stats with modifiers (Added/Additive/Multi/Cap), `Recalculate` into output columns, rollback state | no intrinsic storage (host-owned int columns), no per-entity enum-offset block, no stored input records, no source tracking |
| sv fold (Fold.cs, Kernel.Fold.cs, Inbox.cs) | one fold per tick: `after = clamp(before + saturate32(delta + inbox), floor, ceiling)`; integer-only atomic inbox across rows | sums deltas, so it cannot group by source or by type, cannot drop by source, cannot reflect |
| sv conditions and events (Condition.cs, EventStreams*.cs) | hysteresis bits, events derived from edge bits, per-channel cap | events are not per-entity values |
| sv Unity adapter (appendix D) | `SvHost`, link registry, scatter/fold/derive jobs, send pass; every intrinsic needs a hand-written `[TlState]` struct | not designer-driven, not host-independent |
| sv rollback (Board.Keyframe.cs, Board.Undo.cs, StateCodec.cs, SvState.cs) | save/load of board state | no snapshot ring, no input log, no dead-entity retention |
| tl (appendix E) | designers author timelines as data; kinds with `Combine` and `Apply`; pulses; generated Burst jobs; baked library | tl's own kind job costs 8.45 ms at 1M rows; one column per kind, no sources |
| previous plan (appendix G) | the earlier senior's plan, implemented in phases 1 and 2 | it covered the engine-to-tl seam only, not the designer layer, source tracking, or compiled rules |

Measured by this work (FACT; Unity 7000.0.0a16, 1M rows, 11 workers, Burst on, safety off): sv fold total 2.35 ms; tl's Damage kind
job 8.45 ms; whole frame 10.83 ms. Pinned digests: cross-host A2ACAA69E55F79D8, Burst kernel 2B43BC1549AAE751, soak 6D6C602BF07E9366.
Unity 7 facts: Editor 7000.0.0a16 with built-in Entities 7.0.0, Collections 7.0.0, Burst 2.0.0; the editor runs CoreCLR; no Mono player
ships; a `[TlState]` struct is one component type; chunk change filters have false positives only; Burst probes use
`[BurstDiscard]`; untyped handles (no generic jobs). More in appendix H.

## 6. What the writer could not resolve (say how you resolve each, or flag it)

- Whether triggered effects may cascade (a heal triggers another rule) or are one layer deep.
- Whether events are per-entity values (counter or edge bit) or messages.
- Whether the hand and shield are separate entities with their own blocks or part of a parent's block.
- Whether SIMD across entities or per-entity contiguous blocks wins when rules are branchy, and whether a chunked array-of-structure-of-arrays gives both.
- How reflection and "remove pass" resolve mutual immunity and mutual reflection deterministically and in bounded time.
- How long stored inputs live for rewind, and what exactly a snapshot contains at 1,000,000 entities.
- Whether rules compile per game build or per content pack, and how a card "adds a check" when everything is compiled.

## 7. What plan.md must contain

1. Data model: per-entity contiguous layout with enum offsets and defaults, chunking, whole-chunk save/load, sizes for 100 stats +
   100 intrinsics + 100 events, SIMD across entities while chunks stay copyable, and where stored inputs live at 1M entities.
2. The stored-input record: fields (type, amount, consumed, source entity, source kind), fixed-size form, per-frame double buffering,
   grouping by source and by type, bounded size and overflow behaviour.
3. The passes in order with determinism arguments (accumulate, gate by type, gate by source, reflection and the remove pass, triggered
   effects, apply, events), termination and bounds, and where checks sit.
4. Rules: the designer-authored format (graph or table), the intermediate representation, compiler passes, priority and conflict
   handling, chains of chains, how items and cards add or remove checks, scaling to 10,000 rules.
5. Entity-to-entity passing and reads (parts and owners, any two entities), deterministic order, hot targets.
6. tl integration without dependence on tl: how timeline ticks feed stored inputs; the boundary.
7. Rewind: snapshot ring, input log, resimulation cost, dead-entity retention, lag-compensation resolution.
8. Hosts: core API for plain .NET and WASM, the Unity 7 adapter (Burst, jobs, C# 9), the web simulator.
9. From today's sv: what stays (fold law, digests), what is replaced or deleted, a migration order for agents.
10. Verification: determinism proofs, rule-set simulation, a slow reference interpreter diffed against the compiled code, per-pass
    budgets, gates and kill rules.
11. Source-code examples showing how generated and hand-written code should look, a file layout, and milestones.
12. Risks, unknowns, owner decisions listed separately, and what you could not verify. Say when you are guessing.

## 8. Appendix index

A. sv: AGENTS.md, contracts, README excerpt, docs (UNITY, USING-WITH-TL, PRODUCTION-CHECKLIST)
C. sv core source (`src/Sv`)
D. sv Unity adapter source (`packages/com.sv.unity`)
E. tl: README, JSON format, coder API, kind attributes; tl internals excerpts; tl.unity guide, game kinds, authoring
F. Research: StateSmith and QuanTAlib notes, StateSmith generated output (Balanced2 and Balanced1)
G. The previous senior plan (plan-sv.md)
H. Unity CLI skill field notes (Unity 7 facts, rules the agents follow)

Every appendix file is embedded verbatim under its path, in fenced blocks.


---

# Appendices (verbatim source and documents)

---

## Appendix A. sv: rules, contracts, docs

### sv AGENTS.md

Path: `sv/AGENTS.md`  (1694 bytes)

`````
# sv repository rules

`sv` is the state-variable library of the three-system composition (tl × ecs-kernels ×
sv). The README's vendored Composition Contract is the law for every line of math and
every public signature; read it before touching anything.

- The contract changes only through the agent-bus merge point
  (`agent-bus/contract/CONTRACT.md`): bus message first, then vendor the bumped copy
  here verbatim.
- Owner-approved (2026-11-12): while the agent-bus is unreachable, the owner replaces
  the bus as contract approver; approved texts land as new files in `contract/`
  (first: `contract/CONTRACT-v2.1.0.md`).
- Atoms are short: green build + tests, committed, receipts recorded in the issue file
  (`issues/`) before the next atom starts. File the issue files on GitHub verbatim
  once the remote exists.
- Production code: no explanatory comments, no FMA (grep-enforced), ternary/select
  clamps only on floats, unmanaged data path, deterministic order everywhere. Tests
  and tooling may be managed freely.
- Before starting and before finishing any atom, check the agent bus:
  `/home/i/GitHub/agent-bus/bin/check stats --body`. Reply and ack per the bus README.
- `contract/` holds verbatim bus artifacts — never edit them; re-copy on bump.
- The on-disk `/home/i/GitHub/ecs-kernels` tree is a stale pre-refactor copy (bus
  message 0010). Never grep it as evidence of the kernel system's current state; the
  canonical `Kernels.Testing.StrictFloat` kit (Add/Subtract/Multiply/Divide/Sqrt, all
  NoInlining double-emulated) is vendored in `tests/Sv.Tests/StrictFloatOracle.cs` and
  must stay textually identical to the kernel repo's copy. Any diff is a bus incident.
`````

### sv contract/CONTRACT-v2.1.0.md

Path: `sv/contract/CONTRACT-v2.1.0.md`  (4762 bytes)

`````
owner-approved, bus unreachable (owner request 2026-11-12)

# The Composition Contract - v2.1.0 (amendments over v1.0.3)

**Step A0, ratify what is already built (no code change, no digest moves).** The code is ahead of the vendored v1.0.3: the v1.1.0 batch (bus 0036), clause 18 (saturating add, bus 0042), the seven v1.2 items of `issues/0097`, and the implemented v2.0 items (history-free fold, `Cap` kind, indirect stats, crossings on the wheel). Their pins already moved at atoms 90 and 98. v2.1.0 starts by folding that text in as written in `issues/0097`.

**Step A1, the new text.**

> **Clause 3, appended.** The canonical order of a modifier set is (op-kind, value bits) over the kinds Added, Additive, Multi, Cap; insertion ids are removal handles only. Accumulation into columns is ordered by clause 13, not by this clause.

> **Clause 4, appended.** A host whose stat storage is chunked binds a sink instead of a column. Recalculate appends one (slot, stat, value bits) record per changed cell in ascending (slot, stat) order, and the host stores every record before the intrinsic fold of the same frame. A record carries the canonical bits a bound column would have received. Template spawn appends nothing; the host fills spawned rows from the template.

> **Clause 7, replaced.** Frozen frame order: input edges -> commands and expiry, as modifier operations in canonical order -> stat recalc + broadcast -> intrinsic fold -> timeline step -> kernels -> condition and event derivation -> swap. Mid-frame structural changes apply next frame. Store columns (stat outputs, intrinsic values, condition bits, sense columns) are read-only from the end of the fold to the end of the timeline step. Intrinsic values change only in the fold, in event-drain handlers and in condition responses. The fold reads the bounds broadcast in the same frame; drain handlers read the previous broadcast. Accumulate columns written in the timeline step of frame t are consumed by the fold of frame t+1.

> **Clause 12, appended.** On chunked hosts no pointer into host storage outlives the job that resolved it. sv retains only slot-indexed arrays that it owns; they are fixed in capacity and address from warmup to dispose.

> **Clause 13, replaced.** Accumulate columns (`+=`): (a) one column, one writer kind, one consumer; store-writers and accumulate-writers never share a column; (b) every cell of an accumulate column is zero when the timeline step begins; the consumer restores zero in each cell it consumes, and a column without a registered consumer is zeroed by the host; (c) a row's cells are written only by the worker that holds the row, in (system, kind declaration, entry) order; (d) integer accumulation is exact 32-bit wrapping addition; each contribution is bounded (magnitude at most 2^20, at most 2^10 writer kinds per column) so that no sum wraps, and checked builds trap a wrap; (e) a contribution to another row is an integer, is added atomically to that row's 64-bit inbox cell, and is consumed by the same fold; (f) float accumulate columns are row-local.

> **Clause 17, appended.** Events raised by the fold are derived from edge bits after the parallel step, per channel in ascending slot order. A channel has a build-time capacity. When a frame's edges exceed it, the channel delivers one Dense record carrying the count and no per-row events; the edge bits stay readable until the next fold, and every handler of the channel provides a mask form. The portable maker is the struct maker.

> **Clause 19, new. Intrinsic fold.** For each live row of a dense intrinsic, once per frame: after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling), computed without intermediate overflow; delta and inbox become zero; applied = saturate32(after - before). The result is a function of the multiset of the frame's contributions. An implementation of any width, on any host, equals the scalar reference bit for bit.

> **Clause 20, new. Connections are data.** Every use of a column by a kind is declared as (kind, column, mode, scope), with mode in {Read, Check, Accumulate, Delta, Modifier, Event} and scope in {Self, Remote}. The table is validated at load against the schema, and its hash is part of state identity. Modifier and Event connections act at clip edges through canonical batches; modifier operations are ordered by (slot, operation with remove before add, source, stat, kind, value bits).

> **Portable-surface rule, replaced.** Unity-visible sources are C# 9 on netstandard2.1. The portable core is static functions over pointers and plain structs, with struct-generic strategies. `static abstract` members, `delegate* unmanaged` and `System.Runtime.Intrinsics` are .NET-only front doors over the same core.
`````

### sv contract/CONTRACT-v1.0.3.md

Path: `sv/contract/CONTRACT-v1.0.3.md`  (5375 bytes)

`````
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
`````

### sv docs/UNITY.md

Path: `sv/docs/UNITY.md`  (8413 bytes)

`````
# Using sv from tl.unity kind jobs

Unity 7000.0.0a16, Entities 7.0.0, Collections 7.0.0, Burst 2.0.0 (the editor's built-in packages).
tl does not reference sv and sv does not reference tl. The glue is in your game assembly.

## 1. Install

- `eng/unity-sync.sh sync <project>/Packages/com.sv.core` copies `src/Sv` into an embedded
  package (asmdef `Sv.Core`). `eng/unity-sync.sh check <same path>` fails on any drift.
- Add `"com.sv.unity": "file:<sv checkout>/packages/com.sv.unity"` to `Packages/manifest.json`.
  Reference `Sv.Core` and `Sv.Unity` from your game asmdef.

## 2. Declare the columns as tl state

Every sv column is a `[TlState]` partial struct with exactly one 4-byte field. One struct is one
component type, and tl's generated kind job binds it directly. Do not declare sv columns as
`Span<T>` fields of a `[TimelineSystem]`: those get a per-system wrapper component.

```csharp
[TlState] public partial struct Hp { public int Value; }          // intrinsic value: sv writes it in the fold
[TlState] public partial struct HpDelta { public int Value; }     // delta: kinds add into it, sv consumes it
[TlState] public partial struct MaxHp { public float Value; }     // stat output: sv scatters it
public struct LowHp : IComponentData, IEnableableComponent { }    // condition gate (optional)

[TlKind]
public struct Damage
{
    public static void Combine(ref int total, in int next) => total += next;
    public static void Apply(in int total, ref HpDelta delta) => delta.Value -= total;
}
```

A kind writes a delta with `+=` or `-=`, never `=`, and it never writes the value or a stat column.
A kind reads `Hp` and `MaxHp` as ordinary read-only columns. tl adds the kind's Apply column itself;
add `SvSlot`, `SvSlotCleanup`, the value and stat columns in the same `ComponentTypeSet` as tl's
columns (without `SvSlotCleanup` in the archetype a row moves archetype once, at its first frame).

## 3. Register the links once, install the host

```csharp
var links = new SvLinkRegistry();
links.Stat<MaxHp>(schema.Stat("max_hp"));
var hp = links.Intrinsic<Hp, HpDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.OfStat<MaxHp>(), remote: true);
links.Condition<LowHp>(hp, 30, 100, 40, 100, capacity: 16384);      // enter below 30 %, leave at 40 %
links.Send<Outgoing, Target>(hp);                                     // optional cross-row send pass
links.Link<HpDelta>(kindId, LinkMode.Delta);                          // one line per use, for Validate and Hash
var host = SvHost.Install(world, board, links, digestEvery: 8);      // throws on a LinkFault or a second host
```

`Install` creates `SvBeginSystem` (first in `SimulationSystemGroup`) and `SvEndSystem` (last).
tl's systems run between them. Limits: 4 dense intrinsics, 8 stat columns, stat index below 64,
4 send passes, one `SvHost` per world (a second `Install` throws). `Install(world, board, links,
writeCapacity = 65536, opCapacity = 65536, digestEvery = 8)`: the two capacities are buffer sizes, not
limits (the StatOp queue doubles when full, the scatter runs in `writeCapacity` chunks).
`Install` adopts entities whose `SvSlot` names a slot that is already live on the board (a board
restored before the world) and scatters all their stats on the first frame.

## 4. Per frame (section 2.4 of the plan)

| Where | Call |
|---|---|
| main thread, before the group | `host.Enqueue(StatOp.Add(...))`, `host.Enqueue(StatOp.RemoveSource(...))` |
| `SvBeginSystem` | expire, canonical Apply, Recalculate, scatter of changed stats, fold job |
| tl systems | kinds read store columns and add into deltas |
| a kind that writes another row | a tl kind writes its own row only: it adds the amount to a row-local effect column (`[TlState] Outgoing`, `+=`); the game writes the target's slot into a target column (`Target`, an `int`, a slot from `SvSlot` or `Bind`). `links.Send<Outgoing, Target>(hp)` makes `SvEndSystem` send every nonzero effect through the inbox and zero it. Targets outside `[0, Capacity)` are dropped. Your own `SvInboxWriter` from `host.InboxWriter(hp)` still works in any job |
| `SvEndSystem` | send passes (Burst, parallel), derive events, digest every `host.DigestEvery` ticks (1 = every tick, default 8; use 1 in debug builds) |
| main thread, after the group | `host.Events(hp)` (ascending slot), `host.Channel(hp).Dense`, `host.Digest` (taken at tick `host.DigestTick`; between digest ticks it is the older value) |

Deltas written in tick t are folded at the start of tick t+1. The fold is
`after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling)`; it clears the
delta and the inbox. Above the channel capacity no per-row events are delivered: one Dense record,
and `host.EdgeBit(hp, slot)` stays readable until the next fold.

## 5. Rows

- `host.Bind(entity)` gives a slot; the next `SvBeginSystem` adds `SvSlot` + `SvSlotCleanup` if missing
  and writes both. `host.Unbind(slot)` frees the slot, cancels a `Bind` of the same frame and the next
  `SvBeginSystem` removes both components.
- Destroying a bound entity (or removing its `SvSlot`) frees its slot at the next `SvBeginSystem`
  (`SvSlotCleanup` is a cleanup component). No `Unbind` needed.
- Waves: `host.Spawn(entityManager, prefab, templateSlot, spawned)` instantiates a prefab that
  already carries the template's stat values and assigns slots with `Board.Spawn`.
- Structural changes cost sv nothing: sv keeps slot-indexed arrays only, never chunk pointers.

## 6. Sharp edges (what is still true)

- Adding and removing the same (slot, source) in one batch leaves the modifier live (remove sorts before add).
- All sends to one slot serialize (about 10 ns each, `Interlocked.Add`); the core `InboxLanes` fallback exists but the adapter's send pass does not use it yet (issue 0151). `digestEvery: 0` turns the digest off; the StatOp queue grows with managed memory when `opCapacity` is exceeded, so size it for the busiest frame.
- Above the channel capacity (`Condition(..., capacity)`) only one Dense record; the edge bits stay readable until the next fold.
- `host.Digest` is the value of tick `host.DigestTick`; compare digests only on ticks where `DigestTick == Tick - 1`.
- `Unbind`, `Bind` and `Enqueue` are main-thread calls between frames. Structural changes for new, unbound and
  destroyed rows happen at the start of `SvBeginSystem` (one sync point per frame that has such rows).
- An entity carrying `SvSlot` that was never `Bind`-ed is folded as whatever slot its `SvSlot` says: bind every
  row that has the component, or add `SvSlot` only through `Bind`.
- `Spawn` instantiates from a prefab: cleanup components are not copied, so every spawned row moves archetype
  once when `SvSlotCleanup` is added.
- No Mono player on Unity 7000.0.0a16 (only CoreCLR and IL2CPP variations ship; `Mono2x` is silently ignored). Not measured.

## 7. Rules that keep it exact

- Remote writes are integers through the inbox. Floats never cross rows.
- Every delta writer goes through a read-write type handle (tl's generated job does). A write
  through a cached pointer bypasses the chunk change filter and is not folded.
- All sends to one slot serialize (about 10 ns each): a measured, documented limit.

## 8. Measured (Unity 7000.0.0a16, CoreCLR, Burst on, safety off, 3 fresh processes, probes 0, 0 B main thread)

P1 = 1,048,576 rows, tl's generated Damage kind job fed by W 2.5 Poison tracks per row, cross-row 0 %,
send pass on, condition lane on, digest every 8, 874 StatOps per tick (issue 0143, `SvTlBench`).

| workers | sv total ms (fold) | tl kinds ms | frame ms |
|---|---|---|---|
| 11 | 2.31 (1.26) | 8.28 | 10.56 |
| 7 | 2.53 (1.46) | 9.47 | 11.99 |
| 4 | 3.26 (1.89) | 12.41 | 15.71 |
| 2 | 4.65 (2.72) | 18.30 | 22.91 |

- sv meets the 8.3 ms gate at every worker count. tl's own kind work at 1M rows is 8.3 ms at 11 workers.
- P2 / P3 stand-in cells: worst sv total 1.14 ms (P2) and 2.18 ms (P3).
- RAM at 1M rows: board 18.6 B per row + 58.8 B per live modifier; host native 33.1 B per row;
  sv ECS columns 32 B per row in that test (4 B per column) + 1 enable bit per condition.
- Determinism: NorthStar2 through the adapter equals the .NET twin at 1,000 and 1,048,576 rows, 1 and 11
  workers (issue 0142); the send pass digest is equal at 1 and 11 workers (issue 0141).
- FMA: none in the Burst code of the fold, scatter, send and digest jobs (disassembled JIT cache, issue 0143).
`````

### sv docs/USING-WITH-TL.md

Path: `sv/docs/USING-WITH-TL.md`  (5868 bytes)

`````
# Using sv with tl

sv holds the numbers a game simulates (stats, intrinsics such as hp) and tl plays the timelines that
change them. Neither library references the other: the glue owns two tables and calls both in a fixed
frame order. A complete, tested example is `tests/Sv.NorthStar2` (tl 4.0, .NET 10).

## 1. Columns and links

Every column is listed once as a `ColumnDef(nameHash, role, schemaId, consumer)` and every use by a kind
once as a `Link(kind, column, mode, scope)`. `Links.Validate` returns the first `LinkFault`;
`Links.Hash` is part of state identity (pin it beside `Schema.ContentHash`).

| Role | Writer | Example |
|---|---|---|
| StatOutput | sv (Recalculate -> sink or bound column) | max_hp, armor |
| Intrinsic | sv fold only | hp |
| Delta | tl kinds (`+=`), consumed and zeroed by the fold | `[TlState] HpDelta { int Value; }` |
| Inbox | any worker, atomic 64-bit add, consumed by the same fold | `InboxBuffer` |
| Effect / Signal | tl kind, row-local, consumed once | `Outgoing`, pulse columns |
| Condition | sv fold (hysteresis bits) | hp below 30 %, re-arm at 40 % |
| Sense | an external module, integer, frozen like stats | grid fields |

On tl's side a column is a plain `[TlState]` struct; a kind is a `[TlKind]` struct with `Combine` and
`Apply`; a track feeds it with `[Feeds]` (every tick) or `[Pulse]` (clip Start/End).

## 2. What a kind does per mode

| Mode | The kind | sv's part |
|---|---|---|
| R read | loads a store column | stores were written before the step and are frozen in it |
| K check | uses a condition bit or compares a frozen value | fold computes the bits |
| A accumulate | `+=` into a row-local effect column | its single consumer clears it |
| D delta | `+=` into a delta column (`Apply(in int total, ref HpDelta d) => d.Value += total`) | the next fold sums, clamps once, zeroes |
| D remote | tl kinds cannot address other rows: write the amount to a row-local int effect, then an sv send pass calls `Inbox.Send(inbox, target, amount)` and clears it | atomic, order-free |
| M modifier | pulse at clip Start/End into a pulse column | turned into `StatOp.Add` / `StatOp.RemoveSource` (source `0x40000000 | timeline << 8 | slot`) for the next frame |
| E event | nothing (edge bits are the signal) or a signal column | `EventDerive` emits events after the jobs |

## 3. Frame order (contract v2.1.0 clause 7)

1. Commands: journal -> `StatOp`s, inbox sends (every external integer write enters the inbox).
2. Expiry: `Board.ExpireModifiers(tick)`.
3. Apply: `StatOps.Canonicalize` + `Board.Apply` (pulse StatOps of the previous frame included).
4. `Board.Recalculate`.
5. Scatter: `TakeBroadcast` into the host's stat columns (`BindSink(stat, true)` once at setup fills
   existing rows; the .NET alternative is `BindOutput`). Stores are frozen from here.
6. Fold: `Kernel.Fold(value, window, delta, bounds, changed, inbox)` per worker partition, then
   condition bits and edge counts (`EventDerive.AddCount`).
7. tl: `world.Tick(players)` and the generated `TlAuto_<Assembly>.Run(world, players, true, workers)`,
   then the send pass.
8. Derive: `EventDerive.Derive(...)` from edge words; pulses -> StatOps for t+1.
9. `EventStreams.Swap`, digest, tape (the tape stores causes: the journal; replay rebuilds the rest).

Deltas written in step 7 of frame t are folded in step 6 of frame t+1 (one frame lag).

## 4. Fold semantics: sum, then clamp once

`after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling)`; delta and inbox become
zero. All contributions of a frame are summed first. Heal +10 and hit -20 on hp 95/100 give **85**
(sequential clamping would give 80). Order-dependent rules (shield first, then hp) stay on the sparse,
ordered path (`ClampShift.Then`). The result depends only on the multiset of contributions, so worker
count and arrival order cannot change a bit (NorthStar2: 1, 2, 4, 7, 11 workers and 100 shuffles equal).

**Floats never cross rows.** Remote writes are integers into the inbox; float accumulation is row-local.
A stat used as a bound converts through `Clamp.BoundOf`, never `(int)`.

## 5. Events: cap and Dense

A channel has a build-time capacity, default **16,384** (65,536 tripped the plan's 0.5 ms sort kill rule:
0.50-0.68 ms; 16,384 sorts in 0.08-0.11 ms). If a frame's edges are at most the capacity, every event is
delivered in ascending slot order. Above it, exactly one `DenseEvent {Channel, Tick, Count}` is delivered
and no per-row events; the edge words stay readable until the next fold, so every handler must also
accept the bit words (mask form). A 1M-edge death wave costs ~0.01 ms and 0 B.

## 6. Limits found (2026-11-12)

- A modifier added and removed (same slot, source) in one batch stays: remove sorts before add (re-trigger
  refresh). Remove a clip modifier in a later frame than its add.
- Before issue 0122 the set table never reused freed sets under steady churn (~70 B per change, unbounded);
  fixed, memory now flat.
- `Bounds.StatRows(floor, ceiling)` folds against host float stat columns (sink-filled), same
  `Clamp.BoundOf` conversion as `Bounds.Stats` (issue 0129); an int bound column is no longer needed.
- tl kinds cannot write another row; use the effect column + send pass of section 2.
- Frame rollback over dense columns is off by default; `Rollback`, `LoadKeyframe` and `LoadState` refill
  a bound sink from the restored board (issue 0129).
- Many sends to few slots: give each worker an `InboxLanes` lane (`lanes.Lane(worker)->Send`, `Flush` at
  the end of its send pass). Same bits as `Inbox.Send`; 15-23 % off the kinds stage when 10 % of sends
  hit one slot, equal cost otherwise (issue 0130).
- Digest cadence: `DigestCadence.Default` is every tick in debug builds and every 8 ticks in release
  (issue 0132).
- frame1m numbers (issue 0125) use an sv stand-in for the kinds, not tl.
`````

### sv docs/PRODUCTION-CHECKLIST.md

Path: `sv/docs/PRODUCTION-CHECKLIST.md`  (4841 bytes)

`````
# Production test checklist (sv with tl)

For a first real-project test of sv 2.1.0 with tl on .NET 10 or Unity 7. Frame order and modes:
`docs/USING-WITH-TL.md`. Unity specifics and open Unity sharp edges: `docs/UNITY.md`.

## Enable

1. **One frame order** (USING-WITH-TL section 3). Deltas written by tl in frame t fold in t+1.
2. **Columns declared once**: `Links.Validate` returns `LinkFault.None` at load; pin `Links.Hash` and
   `Schema.ContentHash` in your own test next to the game's state hash.
3. **Stats to host columns**: `board.BindSink(stat, true)` once at setup, drain `TakeBroadcast` every
   frame. Rollback, `LoadKeyframe` and `LoadState` refill the sink; drain fully after each.
4. **Bounds from stats**: `Bounds.Stats(board, ...)` (sv columns) or `Bounds.StatRows(floor, ceiling)`
   (host float columns). Never `(int)` a stat; `Clamp.BoundOf` is the only conversion.
5. **Remote writes are integers**: tl kind -> row-local effect column -> send pass. Use one
   `InboxLanes` lane per worker (`Flush` at the end of the pass); it costs the same as `Inbox.Send`
   and less when many sends hit few slots.
6. **Events**: one `EventDerive` per channel, cap at host build (default 16,384, max 16,777,216). Every
   handler must accept the `DenseEvent` + edge-word form; above the cap no per-row event exists.
7. **Digest**: `SvHost.Install` defaults to `digestEvery: 8` in every build; pass `digestEvery: 1` in a development player (`DigestCadence.Default` is the core-level default: every tick in debug, every 8 in release). `digestEvery: 0` turns the digest off. Log
   `(tick, Board.Digest, CellDigest sums per intrinsic column)` on due ticks.
8. **Test builds**: run once with `-p:DefineConstants=SV_CHECKED` (Unity: add `SV_CHECKED` to Scripting Define Symbols) (range guards throw; after `InvalidateBindings` a bound column parks instead of throwing, see issue 0151), and run
   `sh eng/isa-legs.sh` on each machine you compare.
9. **Unity**: `eng/unity-sync.sh sync <project>/Packages/com.sv.core`, then `check` in CI (the sums need LF files: `.gitattributes` pins that); the repo copy
   lives in `packages/com.sv.core`.

## Expected numbers (measured, Ryzen 5 8500G 6C/12T)

| What | Value | Source |
|---|---|---|
| .NET P1 (1,048,576 rows), W 2.5, 0 % cross, 6 workers, stand-in kinds | ~6.1 ms/tick total, sv stages ~3.3 ms | issues 0125, 0130 |
| same, 10 % cross to one slot, `InboxLanes` | 7.3 ms (atomic 8.0 ms) | issue 0130 |
| .NET digest of 2 x 1M intrinsics | 1.2-1.4 ms when computed (hence K = 8) | issue 0125 |
| Unity Burst P1, W 2.5, 0 % cross, sv + stand-in kinds | ~4.7 ms/tick | issue 0127 |
| same with tl's real Damage kind job, 11 workers | sv 2.35 ms, tl kinds 8.45 ms, frame 10.83 ms (gate covers sv only) | verifier, issue 0143 |
| gate | 8.3 ms per tick at P1 (25 % of 30 Hz) | plan |
| warm managed allocation in sv calls | 0 B (soak: 50,000 ticks) | issue 0133 |
| board memory under churn | flat after warm-up | issue 0133 |

tl's own kind jobs are not in the .NET numbers; budget their time on top.

## Reading a digest mismatch

Compare the per-tick logs and find the **first** differing tick, then:

| Differs between | Likely cause |
|---|---|
| two runs, same host, same workers | uninitialized memory (zero every native column), wall-clock or `Random` without a seed, dictionary order in the glue |
| worker counts (1 vs N) | a float summed across rows, a non-integer remote write, a store column written during the tl step, a send pass that is not inside the frame |
| hosts or ISA legs | float math outside `StatMath`/`BoundOf`, FMA introduced by your code, a Mono/IL2CPP path not in the legs |
| `Board.Digest` only | modifier stream differs: StatOp order (always `Canonicalize`), expiry ticks, add+remove of one (slot, source) in one batch (the remove sorts first) |
| one intrinsic's `CellDigest` only | that column's deltas or inbox: bisect by partition, then by slot |
| live vs replay | something outside the journal changed state |

## Failure modes

| Signal | Meaning |
|---|---|
| `InvalidDataException: keyframe does not fold onto this base` | the receiver was not at the keyframe's base (missed a keyframe, or local edits not rolled back) |
| `InvalidOperationException: slot exhaustion` | board capacity too small for live entities |
| `stale binding — storage moved; rebind` (SV_CHECKED) | a bound column moved; rebind before the next fold |
| `ArgumentException: ... does not cover the window` | delta, inbox or changed column shorter than the fold window |
| `DenseEvent` on a channel | edges above the cap this frame; read the edge words, not per-row events |
| host stat column stale | sink not drained to zero, or bound without fill |
| frame over the gate | look at the stage split: fold and digest are DRAM-bound, cross-row sends live in the kinds stage |
`````

### sv README.md (first 120 lines)

Path: `sv/README.md`  (6286 bytes, excerpt)

`````
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
- Contract v2.1.0 amendments (owner-approved, bus unreachable, 2026-11-12): vendored as
  [contract/CONTRACT-v2.1.0.md](contract/CONTRACT-v2.1.0.md) — clauses 3, 4, 7, 12, 13,
  17, new 19 (intrinsic fold) and 20 (connections are data), and the portable-surface rule.
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

## Quick start

`Sv` targets `net10.0` with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — the data
path is unmanaged columns by design (clause 5: the host owns storage, sv owns the slot
space and the math). Define `SV_CHECKED` in debug builds to trap stale bindings and
use-after-rebind (clauses 12, 14).

All four verbs in one listing — schema, dense slots, stat modifiers, clamped
intrinsics, sparse events, bit-captured input:

```csharp
using System;
using System.Runtime.InteropServices;
using Sv;

internal static unsafe class Program
{
    public struct Hit
    {
        public int Slot;
        public int Amount;
    }

    [UnmanagedCallersOnly]
    static Hit MakeHit(int slot, int value) => new() { Slot = slot, Amount = value };

    static int Main()
    {
        // 1. Schema: float stats (modifier-driven) + int intrinsics (direct writes).
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

        // 2. One board = one dense slot space; capacity rounds up to a 64 boundary.
        using var board = Board.Create(schema, capacity: 1024);
        var slot = board.Acquire();          // host maps entity -> slot; Release() at destroy

        // 3. Stats: stack modifiers, then Recalculate folds + broadcasts into columns.
        var speed = schema.Stat("move_speed");
        var haste = board.AddModifier(slot, speed, new Modifier(ModifierKind.Additive, 0.5f));
        board.Recalculate();
        Span<float> speeds = board.Output(speed);   // sv-owned column, no bind needed
        // speeds[slot] == 9  (6 base * (1 + 0.5) additive); clause-3 canonical fold
        board.RemoveModifier(slot, speed, haste);

        // 4. Intrinsics: host-owned unmanaged int columns, written through chunk ops.
        var mana = (int*)NativeMemory.AllocZeroed((nuint)board.Capacity * sizeof(int));
        var applied = (int*)NativeMemory.Alloc((nuint)board.Capacity * sizeof(int));
        mana[slot] = 40;

        var clamp = Clamp.StatMax(board, schema.Stat("max_mana"));  // bound = last broadcast
        board.RunIntrinsicsChunk(mana, 25, clamp, 0, board.Capacity, applied);
        // mana[slot] == 65, applied[slot] == 25

        // Masked run-walk: only set bits are touched; applied[] is 0 on skipped rows.
        board.RunEnabledChunk(mana, 7, clamp, 0b1011ul, offset: 0, applied);

        // 5. Events: sparse signals over a pooled double buffer (clause 17).
        using var streams = EventStreams.Create(writerCount: 1, bytesPerWriter: 4096);
        var hits = streams.CreateChannel<Hit>();
        hits.Write(new Hit { Slot = slot, Amount = applied[slot] });
        // ...or emit per live row off a condition column — applied != 0 -> one Hit each:
        streams.EmitFromNonZero(hits, applied, 0, board.Capacity, &MakeHit);

        streams.Swap();                          // publishes this frame's writes to readers
        while (streams.TryReadNext(out var type, out var payload, out _))
        {
            if (type == hits.Id)
            {
                var hit = *(Hit*)payload;        // drain handlers write intrinsics here
            }
        }

        // 6. Input: raw axis bits + button edges + a replay ring (clause 11).
        using var input = InputState.Create(axes: 2, buttonWords: 1, ringTicks: 128);
        var axes = stackalloc uint[2];
        var buttons = stackalloc ulong[1];
        axes[0] = BitConverter.SingleToUInt32Bits(0.5f);   // captured bit-verbatim
        buttons[0] = 1ul << 3;                           // button 3 down
        input.Capture(axes, buttons);
[... excerpt: first 120 lines; the rest is omitted ...]
`````

---

## Appendix C. sv core source (src/Sv)

### sv src/Sv/Schema.cs

Path: `sv/src/Sv/Schema.cs`  (13055 bytes)

`````csharp
using System;
using System.Collections.Generic;

namespace Sv
{
    public readonly struct IntrinsicId : IEquatable<IntrinsicId>
    {
        public int Index { get; }

        internal IntrinsicId(int index)
        {
            Index = index;
        }

        public bool Equals(IntrinsicId other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is IntrinsicId other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(IntrinsicId left, IntrinsicId right) => left.Equals(right);

        public static bool operator !=(IntrinsicId left, IntrinsicId right) => !left.Equals(right);

        public override string ToString() => $"Intrinsic({Index})";
    }

    public readonly struct StatId : IEquatable<StatId>
    {
        public int Index { get; }

        internal StatId(int index)
        {
            Index = index;
        }

        public bool Equals(StatId other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is StatId other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(StatId left, StatId right) => left.Equals(right);

        public static bool operator !=(StatId left, StatId right) => !left.Equals(right);

        public override string ToString() => $"Stat({Index})";
    }

    public readonly struct StatSource : IEquatable<StatSource>
    {
        public StatId Stat { get; }

        public float Coefficient { get; }

        public float[]? Curve { get; }

        public float CurveMin { get; }

        public float CurveStep { get; }

        public StatSource(StatId stat, float coefficient)
            : this(stat, coefficient, null, 0f, 0f)
        {
        }

        public StatSource(StatId stat, float coefficient, float[]? curve, float curveMin, float curveStep)
        {
            Stat = stat;
            Coefficient = coefficient;
            Curve = curve;
            CurveMin = curveMin;
            CurveStep = curveStep;
        }

        public bool Equals(StatSource other) => Stat == other.Stat && Coefficient.Equals(other.Coefficient) && ReferenceEquals(Curve, other.Curve) && CurveMin.Equals(other.CurveMin) && CurveStep.Equals(other.CurveStep);

        public override bool Equals(object? obj) => obj is StatSource other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Stat, Coefficient, Curve, CurveMin, CurveStep);

        public static bool operator ==(StatSource left, StatSource right) => left.Equals(right);

        public static bool operator !=(StatSource left, StatSource right) => !left.Equals(right);

        public override string ToString() => $"StatSource {{ Stat = {Stat}, Coefficient = {Coefficient} }}";
    }

    public sealed class StatDef
    {
        public string Name { get; }

        public float Base { get; }

        public IReadOnlyList<StatSource> Sources { get; }

        public StatDef(string name, float @base)
            : this(name, @base, Array.Empty<StatSource>())
        {
        }

        public StatDef(string name, float @base, IReadOnlyList<StatSource> sources)
        {
            Name = name;
            Base = @base;
            Sources = sources;
        }

        public StatDef WithSources(IReadOnlyList<StatSource> sources) => new StatDef(Name, Base, sources);
    }

    public sealed class IntrinsicDef
    {
        public string Name { get; }

        public IntrinsicDef(string name)
        {
            Name = name;
        }
    }

    public sealed unsafe class Schema
    {
        public IReadOnlyList<StatDef> Stats { get; }

        public IReadOnlyList<IntrinsicDef> Intrinsics { get; }

        private readonly Dictionary<string, StatId> statIds;

        private readonly Dictionary<string, IntrinsicId> intrinsicIds;

        public ulong ContentHash { get; }

        internal Schema(StatDef[] stats, IntrinsicDef[] intrinsics)
        {
            Stats = stats;
            Intrinsics = intrinsics;
            ContentHash = HashContent(stats, intrinsics);
            statIds = new Dictionary<string, StatId>(stats.Length, StringComparer.Ordinal);
            for (var i = 0; i < stats.Length; i++)
            {
                statIds.Add(stats[i].Name, new StatId(i));
            }

            intrinsicIds = new Dictionary<string, IntrinsicId>(intrinsics.Length, StringComparer.Ordinal);
            for (var i = 0; i < intrinsics.Length; i++)
            {
                intrinsicIds.Add(intrinsics[i].Name, new IntrinsicId(i));
            }
        }

        static ulong HashContent(StatDef[] stats, IntrinsicDef[] intrinsics)
        {
            var bytes = new List<byte>(64 * (stats.Length + intrinsics.Length));
            void AddU32(uint value)
            {
                bytes.Add((byte)value);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)(value >> 16));
                bytes.Add((byte)(value >> 24));
            }

            void AddI32(int value) => AddU32(unchecked((uint)value));

            void AddName(string name)
            {
                var utf8 = System.Text.Encoding.UTF8.GetBytes(name);
                AddI32(utf8.Length);
                bytes.AddRange(utf8);
            }

            foreach (var def in stats)
            {
                bytes.Add(1);
                AddName(def.Name);
                AddU32(Bits.OfSingle(def.Base));
                AddI32(def.Sources.Count);
                foreach (var source in def.Sources)
                {
                    AddI32(source.Stat.Index);
                    AddU32(Bits.OfSingle(source.Coefficient));
                    if (source.Curve is { } curve)
                    {
                        AddI32(curve.Length);
                        AddU32(Bits.OfSingle(source.CurveMin));
                        AddU32(Bits.OfSingle(source.CurveStep));
                        foreach (var entry in curve)
                        {
                            AddU32(Bits.OfSingle(entry));
                        }
                    }
                }
            }

            foreach (var def in intrinsics)
            {
                bytes.Add(2);
                AddName(def.Name);
            }

            var flat = bytes.ToArray();
            fixed (byte* pin = flat)
            {
                return XxHash64.Hash(pin, flat.Length);
            }
        }

        public StatId Stat(string name)
        {
            if (!statIds.TryGetValue(name, out var id))
            {
                throw new KeyNotFoundException($"sv: unknown stat '{name}'");
            }

            return id;
        }

        public bool TryStat(string name, out StatId id) => statIds.TryGetValue(name, out id);

        public IntrinsicId Intrinsic(string name)
        {
            if (!intrinsicIds.TryGetValue(name, out var id))
            {
                throw new KeyNotFoundException($"sv: unknown intrinsic '{name}'");
            }

            return id;
        }

        public bool TryIntrinsic(string name, out IntrinsicId id) => intrinsicIds.TryGetValue(name, out id);
    }

    public sealed class SchemaBuilder
    {
        private readonly List<StatDef> stats = new();

        private readonly List<IntrinsicDef> intrinsics = new();

        public static SchemaBuilder Create() => new();

        public SchemaBuilder Stat(string name, float @base)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("sv: stat name must be non-empty", nameof(name));
            }

            if (!float.IsFinite(@base))
            {
                throw new ArgumentException($"sv: stat '{name}' base must be finite");
            }

            stats.Add(new StatDef(name, @base));
            return this;
        }

        /// <summary>
        /// Declares a stat derived from earlier-declared stats: each source contributes one synthetic
        /// Added entry, coefficient * source value (one IEEE multiply), folded before every real
        /// modifier in declaration order. Sources must already be declared, so the graph is acyclic.
        /// </summary>
        public SchemaBuilder Derived(string name, float @base, params (string Source, float Coefficient)[] sources)
        {
            Stat(name, @base);
            if (sources == null || sources.Length == 0)
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: derived stat '{name}' needs at least one source");
            }

            var resolved = new StatSource[sources.Length];
            for (var i = 0; i < sources.Length; i++)
            {
                var index = stats.FindIndex(0, stats.Count - 1, def => def.Name == sources[i].Source);
                if (index < 0)
                {
                    stats.RemoveAt(stats.Count - 1);
                    throw new ArgumentException($"sv: derived stat '{name}' source '{sources[i].Source}' must be declared before it");
                }

                if (!float.IsFinite(sources[i].Coefficient))
                {
                    stats.RemoveAt(stats.Count - 1);
                    throw new ArgumentException($"sv: derived stat '{name}' coefficient for '{sources[i].Source}' must be finite");
                }

                resolved[i] = new StatSource(new StatId(index), sources[i].Coefficient);
            }

            stats[stats.Count - 1] = stats[stats.Count - 1].WithSources(resolved);
            return this;
        }

        /// <summary>
        /// Declares a single-source derived stat whose contribution is a pre-folded curve: the table
        /// entry at the bucket holding the source value, in place of coefficient * value. The source
        /// value is quantized as <c>index = clamp((int)((value - min) / step))</c> — values below
        /// <paramref name="min"/> read entry 0, values past the last bucket read the last entry, NaN
        /// reads entry 0. One table load per set resolution; identical source values intern to one set.
        /// </summary>
        public SchemaBuilder Curve(string name, float @base, string source, float[] curve, float min, float step)
        {
            Stat(name, @base);
            if (curve == null || curve.Length == 0)
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: curve stat '{name}' needs a non-empty table");
            }

            if (!float.IsFinite(min) || !float.IsFinite(step) || step <= 0f)
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: curve stat '{name}' needs a finite min and a positive step");
            }

            for (var i = 0; i < curve.Length; i++)
            {
                if (!float.IsFinite(curve[i]))
                {
                    stats.RemoveAt(stats.Count - 1);
                    throw new ArgumentException($"sv: curve stat '{name}' entry {i} must be finite");
                }
            }

            var index = stats.FindIndex(0, stats.Count - 1, def => def.Name == source);
            if (index < 0)
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: curve stat '{name}' source '{source}' must be declared before it");
            }

            var resolved = new StatSource[1]
            {
                new StatSource(new StatId(index), 0f, curve, min, step),
            };
            stats[stats.Count - 1] = stats[stats.Count - 1].WithSources(resolved);
            return this;
        }

        public SchemaBuilder Intrinsic(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("sv: intrinsic name must be non-empty", nameof(name));
            }

            intrinsics.Add(new IntrinsicDef(name));
            return this;
        }

        public Schema Build()
        {
            if (stats.Count == 0 && intrinsics.Count == 0)
            {
                throw new InvalidOperationException("sv: schema declares no stats and no intrinsics");
            }

            var statNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in stats)
            {
                if (!statNames.Add(def.Name))
                {
                    throw new InvalidOperationException($"sv: duplicate stat '{def.Name}'");
                }
            }

            var intrinsicNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in intrinsics)
            {
                if (!intrinsicNames.Add(def.Name))
                {
                    throw new InvalidOperationException($"sv: duplicate intrinsic '{def.Name}'");
                }
            }

            return new Schema(stats.ToArray(), intrinsics.ToArray());
        }
    }
}
`````

### sv src/Sv/Board.cs

Path: `sv/src/Sv/Board.cs`  (17063 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public sealed unsafe partial class Board : IDisposable
    {
        public Schema Schema { get; }

        public int Capacity { get; }

        public int RequestedCapacity { get; }

        public int LiveCount { get; private set; }

        public int LiveWordCount => (Capacity + 63) >> 6;

        public ulong LiveWord(int word)
        {
            if (word < 0 || word >= LiveWordCount)
            {
                throw new ArgumentOutOfRangeException(nameof(word), $"sv: live word {word} out of range (0..{LiveWordCount - 1})");
            }

            return live[word];
        }

        private readonly NativeArena arena;

        private readonly ulong* live;

        private int freeHint;

        private bool disposed;

        Board(Schema schema, int capacity, int requestedCapacity)
        {
            Schema = schema;
            Capacity = capacity;
            RequestedCapacity = requestedCapacity;
            arena = new NativeArena();
            var words = (capacity + 63) >> 6;
            live = arena.AllocZeroed<ulong>(words);

            InitStats(schema, capacity);
            StateShape = ComputeStateShape();
        }

        long ComputeStateShape()
        {
            var bytes = stackalloc byte[28];
            *(long*)bytes = SetTableFormatVersion;
            *(int*)(bytes + 8) = Capacity;
            *(int*)(bytes + 12) = statCount;
            *(int*)(bytes + 16) = Schema.Intrinsics.Count;
            *(ulong*)(bytes + 20) = Schema.ContentHash;
            return (long)XxHash64.Hash(bytes, 28);
        }

        public static Board Create(Schema schema, int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "sv: capacity must be positive");
            }

            return new Board(schema, (capacity + 63) & ~63, capacity);
        }

        /// <summary>
        /// Native bytes currently owned by this board — the tracked allocator's live total, so it is
        /// O(1) and exact: slot bookkeeping, per-slot state pages, the block table, the modifier record
        /// pool, the set table, dirty masks, scratch, mirrors and sv-owned output columns, all counted
        /// at their real allocation sizes. <see cref="Capacity"/> rounds up to a 64 boundary;
        /// <see cref="RequestedCapacity"/> is the pre-rounding value.
        /// </summary>
        public long MemoryBytes => arena.Live;

        const uint StateMagic = 0x44425654;

        const long SetTableFormatVersion = 11;

        internal long StateShape { get; }

        /// <summary>
        /// Saves the board's complete state into <paramref name="state"/> (overwriting it): slot space,
        /// modifier storage and insertion ids, sources and expiries, set identities and membership,
        /// interned blocks, sv-owned output columns, mirrors, changed masks and the dirty stat masks.
        /// External bindings are not state — host-owned columns are the host's to save. Warm saves are
        /// managed-allocation-free.
        /// </summary>
        public void SaveState(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            VisitState(ref codec);
        }

        /// <summary>
        /// Restores a state saved from a board of the same schema shape and capacity, bit-exactly:
        /// resimulating the same inputs reproduces the same results. Bindings stay as they are;
        /// sv-owned columns and mirrors hold the saved broadcast. A mirror that exists only locally
        /// (the snapshot predates it) is kept — never freed, so cached <see cref="Clamp.StatMirror"/>
        /// pointers stay valid — refilled from the restored output, and bound columns are densely
        /// refilled from the restored rows immediately.
        /// </summary>
        public void LoadState(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            VisitState(ref codec);
            RefillSink();
            state.EndRead();
        }

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            if (codec.Reading)
            {
                InvalidateUndo();
                RebaseKeyframes();
            }

            codec.Header(StateMagic, StateShape, "board");
            var liveCount = LiveCount;
            codec.Value(ref liveCount);
            if (codec.Reading)
            {
                LiveCount = liveCount;
            }

            codec.Bytes(live, (long)LiveWordCount * sizeof(ulong));
            if (codec.Reading)
            {
                var word = 0;
                while (word < LiveWordCount && live[word] == ulong.MaxValue)
                {
                    word++;
                }

                freeHint = word;
            }

            if (statCount > 0)
            {
                VisitStats(ref codec);
            }
        }

        public int Acquire()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Board));
            }

            var slot = TakeFreeSlot();
            if (statCount > 0)
            {
                EnsureSlotPages(slot);
                MarkSlotStatsDirty(slot);
                MarkSlotAcquired(slot);
            }

            return slot;
        }

        int TakeFreeSlot()
        {
            var word = freeHint;
            while (word < LiveWordCount && live[word] == ulong.MaxValue)
            {
                word++;
            }

            if (word == LiveWordCount)
            {
                throw new InvalidOperationException("sv: slot exhaustion");
            }

            var slot = (word << 6) + Bits.Tzcnt(~live[word]);
            undo?.Note(live + word, sizeof(ulong));
            live[word] |= 1ul << (slot & 63);
            freeHint = live[word] == ulong.MaxValue ? word + 1 : word;
            LiveCount++;
            MarkKeyframeDirty(slot);
            return slot;
        }

        internal bool IsLive(int slot)
        {
            return slot >= 0 && slot < Capacity && (live[slot >> 6] & (1ul << (slot & 63))) != 0;
        }

        public void Release(int slot)
        {
            if (slot < 0 || slot >= Capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                throw new ArgumentException($"sv: release of non-live slot {slot}");
            }

            undo?.Note(live + (slot >> 6), sizeof(ulong));
            live[slot >> 6] &= ~(1ul << (slot & 63));
            if ((slot >> 6) < freeHint)
            {
                freeHint = slot >> 6;
            }

            MarkKeyframeDirty(slot);

            if (statCount > 0)
            {
                ClearSlotStats(slot);
                ZeroSlotRows(slot);
            }

            LiveCount--;
        }

        public void RunIntrinsicsChunk(int* value, int delta, in Clamp clamp, int start, int count, int* applied = null)
        {
            Kernel.IntrinsicsChunk(value, delta, clamp, start, count, Capacity, applied);
        }

        public void RunIntrinsicsChunk(int* value, int* delta, in Clamp clamp, int start, int count, int* applied = null)
        {
            Kernel.IntrinsicsChunk(value, delta, clamp, start, count, Capacity, applied);
        }

        public void RunEnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int* applied = null, int validBits = 64)
        {
            Kernel.EnabledChunk(value, delta, clamp, enabled, offset, Capacity, applied, validBits);
        }

        public void RunEnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int* applied = null, int validBits = 64)
        {
            Kernel.EnabledChunk(value, delta, clamp, enabled, offset, Capacity, applied, validBits);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Uniform delta added to every enabled row.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(int* value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, int* applied = null)
        {
            Kernel.EnabledChunk(value, delta, clamp, words, wordCount, offset, Capacity, applied);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(int* value, int* delta, in Clamp clamp, ulong* words, int wordCount, int offset, int* applied = null)
        {
            Kernel.EnabledChunk(value, delta, clamp, words, wordCount, offset, Capacity, applied);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Uniform delta added to every enabled row.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(Span<int> value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, Span<int> applied = default)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            Guard.EnabledRange(offset, wordCount, Capacity);
            var reach = Reach(words, wordCount, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanWindow(applied.Length, (long)offset + wordCount * 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                for (var w = 0; w < wordCount; w++)
                {
                    Simd.EnabledWord(v, null, delta, clamp, words[w], offset + w * 64, a, Simd.ValidBits(value.Length, offset + w * 64));
                }
            }
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, ulong* words, int wordCount, int offset, Span<int> applied = default)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            Guard.EnabledRange(offset, wordCount, Capacity);
            var reach = Reach(words, wordCount, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanReach(delta.Length, reach, nameof(delta));
            Guard.SpanWindow(applied.Length, (long)offset + wordCount * 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                for (var w = 0; w < wordCount; w++)
                {
                    Simd.EnabledWord(v, d, 0, clamp, words[w], offset + w * 64, a, Simd.ValidBits(Math.Min(value.Length, delta.Length), offset + w * 64));
                }
            }
        }

        static int Reach(ulong* words, int wordCount, int offset)
        {
            for (var w = wordCount - 1; w >= 0; w--)
            {
                if (words[w] != 0)
                {
                    return offset + w * 64 + 64 - Bits.Lzcnt(words[w]);
                }
            }

            return 0;
        }

        static int Reach(ulong word, int offset) => word == 0 ? 0 : offset + 64 - Bits.Lzcnt(word);

        public void RunIntrinsicsChunk(Span<int> value, int delta, in Clamp clamp, int start, int count, Span<int> applied = default)
        {
            Guard.ChunkRange(start, count, Capacity);
            Guard.SpanReach(value.Length, start + count, nameof(value));
            Guard.SpanWindow(applied.Length, start + count, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                Simd.Chunk(v, null, delta, clamp, start, count, a);
            }
        }

        public void RunIntrinsicsChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, int start, int count, Span<int> applied = default)
        {
            Guard.ChunkRange(start, count, Capacity);
            Guard.SpanReach(value.Length, start + count, nameof(value));
            Guard.SpanReach(delta.Length, start + count, nameof(delta));
            Guard.SpanWindow(applied.Length, start + count, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                Simd.Chunk(v, d, 0, clamp, start, count, a);
            }
        }

        public void RunEnabledChunk(Span<int> value, int delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
        {
            Guard.EnabledRange(offset, Capacity);
            Guard.SpanReach(value.Length, Reach(enabled, offset), nameof(value));
            Guard.SpanWindow(applied.Length, offset + 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                Simd.EnabledWord(v, null, delta, clamp, enabled, offset, a, Simd.ValidBits(value.Length, offset));
            }
        }

        public void RunEnabledChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
        {
            Guard.EnabledRange(offset, Capacity);
            var reach = Reach(enabled, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanReach(delta.Length, reach, nameof(delta));
            Guard.SpanWindow(applied.Length, offset + 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                Simd.EnabledWord(v, d, 0, clamp, enabled, offset, a, Simd.ValidBits(Math.Min(value.Length, delta.Length), offset));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EndUndoWindow();
            FreeStats();
            arena.Free(live, LiveWordCount);
            if (keyframePages != null)
            {
                arena.Free(keyframePages, keyframePageWords);
            }

            arena.Dispose();
            disposed = true;
        }
    }
}
`````

### sv src/Sv/Board.Stats.cs

Path: `sv/src/Sv/Board.Stats.cs`  (74753 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
public sealed unsafe partial class Board
{
    private const int PageShift = 12;

    private const int PageSlots = 1 << PageShift;

    private struct ModRecord
    {
        public long Id;

        public ModifierKind Kind;

        public float Value;

        public int Source;

        public long Expiry;

        public int Next;

        public int Slot;

        public int Stat;

        public int Wheel;
    }

    private struct SlotState
    {
        public int Block;

        public int Head;
    }

    private int statCount;

    private float* statBase;

    private float** outputColumn;

    private int* outputStride;

    private float** owned;

    private int* boundGeneration;

    private int** mirrors;

    private int* sourceOffset;

    private int* sourceStat;

    private float* sourceCoefficient;

    private int* sourceCurve;

    private float* curveData;

    private float* curveMin;

    private float* curveStep;

    private float* curveCeiling;

    private int* curveLength;

    private int curveTotal;

    private int* dependentOffset;

    private int* dependentStat;

    private StatEntry* statScratch;

    private long* statScratchIds;

    private int statScratchCap;

    private ulong* changed;

    private ulong digest;

    private bool digestPagesDirty;

    private bool digestRecomputing;

    private ulong* pageDigests;

    private int* changedTouched;

    private int* changedTouchedCounts;

    private ulong* acquired;

    private int* acquiredTouched;

    private int acquiredTouchedCount;

    private int* boundLength;

    private int bindingGeneration;

    private SetTable? table;

    private int pageCount;

    private SlotState** slotPages;

    private BlockTable? blocks;

    private int* tupleScratch;

    private ModRecord* records;

    private int recordCount;

    private int recordCap;

    private int recordFree;

    private uint* dirty;

    private int statMaskWords;

    private int* dirtyTouched;

    private int dirtyTouchedCount;

    private int dirtyTouchedCap;

    private uint* dependentsMask;

    private int* sourceAnchor;

    private uint* allStatsMask;

    internal long nextInsertionId;

    internal int insertionGeneration;

    private StatEntry* scratch;

    private long* scratchIds;

    private int scratchCap;

    private void InitStats(Schema schema, int capacity)
    {
        statCount = schema.Stats.Count;
        if (statCount == 0)
        {
            return;
        }

        table = new SetTable(arena);
        statBase = arena.Alloc<float>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            statBase[i] = schema.Stats[i].Base;
        }

        outputColumn = (float**)arena.AllocBytes(statCount * sizeof(float*));
        Memory.Clear(outputColumn, (nuint)statCount * (nuint)sizeof(float*));
        outputStride = arena.Alloc<int>(statCount);
        owned = (float**)arena.AllocZeroedBytes(statCount * sizeof(float*));
        boundGeneration = arena.AllocZeroed<int>(statCount);

        mirrors = (int**)arena.AllocZeroedBytes(statCount * sizeof(int*));
        statMaskWords = (statCount + 31) >> 5;
        dependentsMask = arena.AllocZeroed<uint>(statCount * statMaskWords);
        allStatsMask = arena.AllocZeroed<uint>(statMaskWords);
        for (var i = 0; i < statCount; i++)
        {
            allStatsMask[i >> 5] |= 1u << (i & 31);
        }

        InitSources(schema);
        var changedWords = statCount * ((capacity + 63) >> 6);
        changed = arena.AllocZeroed<ulong>(changedWords);
        pageDigests = arena.AllocZeroed<ulong>(statCount * ((capacity + 255) >> 8));
        changedTouched = arena.Alloc<int>(changedWords);
        changedTouchedCounts = arena.AllocZeroed<int>(statCount);
        acquired = arena.AllocZeroed<ulong>(changedWords);
        acquiredTouched = arena.Alloc<int>(changedWords);
        boundLength = arena.Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundLength[i] = -1;
        }
        pageCount = (capacity + PageSlots - 1) >> PageShift;
        slotPages = (SlotState**)arena.AllocZeroedBytes(pageCount * sizeof(SlotState*));
        blocks = new BlockTable(arena, statCount);
        tupleScratch = arena.Alloc<int>(statCount);
        recordCap = 16;
        records = arena.Alloc<ModRecord>(recordCap);
        recordFree = -1;
        dirty = arena.AllocZeroed<uint>(capacity * statMaskWords);
        dirtyTouchedCap = 64;
        dirtyTouched = arena.Alloc<int>(dirtyTouchedCap);
        keyframeScratchCap = 32;
        keyframeScratch = arena.Alloc<KeyframeRecord>(keyframeScratchCap);
        scratchCap = 32;
        scratch = arena.Alloc<StatEntry>(scratchCap);
        scratchIds = arena.Alloc<long>(scratchCap);
    }

    void InitSources(Schema schema)
    {
        var total = 0;
        for (var i = 0; i < statCount; i++)
        {
            total += schema.Stats[i].Sources.Count;
        }

        sourceOffset = arena.Alloc<int>(statCount + 1);
        sourceStat = arena.Alloc<int>(Math.Max(total, 1));
        sourceCoefficient = arena.Alloc<float>(Math.Max(total, 1));
        sourceCurve = arena.Alloc<int>(Math.Max(total, 1));
        dependentOffset = arena.AllocZeroed<int>(statCount + 1);
        dependentStat = arena.Alloc<int>(Math.Max(total, 1));
        curveTotal = 0;
        for (var i = 0; i < statCount; i++)
        {
            foreach (var source in schema.Stats[i].Sources)
            {
                if (source.Curve != null)
                {
                    curveTotal += source.Curve.Length;
                }
            }
        }

        if (curveTotal > 0)
        {
            curveData = arena.Alloc<float>(curveTotal);
            curveMin = arena.Alloc<float>(Math.Max(total, 1));
            curveStep = arena.Alloc<float>(Math.Max(total, 1));
            curveCeiling = arena.Alloc<float>(Math.Max(total, 1));
            curveLength = arena.Alloc<int>(Math.Max(total, 1));
        }

        var at = 0;
        var dataAt = 0;
        for (var i = 0; i < statCount; i++)
        {
            sourceOffset[i] = at;
            foreach (var source in schema.Stats[i].Sources)
            {
                sourceStat[at] = source.Stat.Index;
                sourceCoefficient[at] = source.Coefficient;
                sourceCurve[at] = -1;
                if (source.Curve is { } curve)
                {
                    sourceCurve[at] = dataAt;
                    curveMin[at] = source.CurveMin;
                    curveStep[at] = source.CurveStep;
                    curveLength[at] = curve.Length;
                    curveCeiling[at] = StatMath.CurveCeiling(source.CurveMin, curve.Length, source.CurveStep);
                    for (var k = 0; k < curve.Length; k++)
                    {
                        curveData[dataAt + k] = curve[k];
                    }

                    dataAt += curve.Length;
                }

                dependentOffset[source.Stat.Index + 1]++;
                at++;
            }
        }

        sourceOffset[statCount] = at;
        var sourceTotal = Math.Max(at, 1);
        sourceAnchor = arena.Alloc<int>(sourceTotal);
        for (var i = 0; i < sourceTotal; i++)
        {
            sourceAnchor[i] = -1;
        }
        for (var i = 0; i < statCount; i++)
        {
            dependentOffset[i + 1] += dependentOffset[i];
        }

        var fill = arena.Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            fill[i] = dependentOffset[i];
        }

        for (var i = 0; i < statCount; i++)
        {
            for (var k = sourceOffset[i]; k < sourceOffset[i + 1]; k++)
            {
                dependentStat[fill[sourceStat[k]]++] = i;
            }
        }

        for (var stat = 0; stat < statCount; stat++)
        {
            for (var d = dependentOffset[stat]; d < dependentOffset[stat + 1]; d++)
            {
                var dependent = dependentStat[d];
                dependentsMask[stat * statMaskWords + (dependent >> 5)] |= 1u << (dependent & 31);
            }
        }

        arena.Free(fill, statCount);
    }

    void FreeStats()
    {
        FreeWheel();
        if (statCount == 0)
        {
            return;
        }

        FreePages(slotPages);
        blocks!.Dispose();
        arena.Free(tupleScratch, statCount);
        table!.Dispose();
        arena.Free(statBase, statCount);
        arena.FreeBytes(outputColumn, statCount * sizeof(float*));
        arena.Free(outputStride, statCount);
        for (var i = 0; i < statCount; i++)
        {
            if (owned[i] != null)
            {
                arena.FreeAligned(owned[i], Capacity);
            }
        }

        arena.FreeBytes(owned, statCount * sizeof(float*));
        arena.Free(boundGeneration, statCount);
        arena.Free(boundLength, statCount);
        for (var i = 0; i < statCount; i++)
        {
            if (mirrors[i] != null)
            {
                arena.FreeAligned(mirrors[i], Capacity);
            }
        }

        arena.FreeBytes(mirrors, statCount * sizeof(int*));
        arena.Free(sourceOffset, statCount + 1);
        arena.Free(sourceStat, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceAnchor, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceCoefficient, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceCurve, Math.Max(sourceOffset[statCount], 1));
        if (curveTotal > 0)
        {
            arena.Free(curveData, curveTotal);
            arena.Free(curveMin, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveStep, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveCeiling, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveLength, Math.Max(sourceOffset[statCount], 1));
        }
        arena.Free(dependentOffset, statCount + 1);
        arena.Free(dependentStat, Math.Max(sourceOffset[statCount], 1));
        var maskWords = statCount * ((Capacity + 63) >> 6);
        arena.Free(changed, maskWords);
        arena.Free(changedTouched, maskWords);
        arena.Free(changedTouchedCounts, statCount);
        arena.Free(acquired, maskWords);
        arena.Free(acquiredTouched, maskWords);
        arena.Free(records, recordCap);
        arena.Free(dirty, Capacity * statMaskWords);
        arena.Free(dirtyTouched, dirtyTouchedCap);
        arena.Free(keyframeScratch, keyframeScratchCap);
        arena.Free(dependentsMask, statCount * statMaskWords);
        arena.Free(allStatsMask, statMaskWords);
        arena.Free(scratch, scratchCap);
        arena.Free(scratchIds, scratchCap);
        arena.Free(statScratch, Math.Max(statScratchCap, 1));
        arena.Free(statScratchIds, Math.Max(statScratchCap, 1));
    }

    void FreePages(SlotState** pages)
    {
        for (var page = 0; page < pageCount; page++)
        {
            if (pages[page] != null)
            {
                arena.Free(pages[page], PageSlots);
            }
        }

        arena.FreeBytes(pages, pageCount * sizeof(SlotState*));
    }

    SlotState* SlotPage(SlotState** pages, int slot)
    {
        var page = slot >> PageShift;
        if (pages[page] == null)
        {
            var fresh = arena.Alloc<SlotState>(PageSlots);
            undo?.Note(pages + page, sizeof(SlotState*));
            pages[page] = fresh;
            undo?.QuarantineFresh(fresh, PageSlots * sizeof(SlotState));
            for (var i = 0; i < PageSlots; i++)
            {
                fresh[i] = new SlotState { Block = -1, Head = -1 };
            }
        }

        return pages[page];
    }

    int AllocRecord()
    {
        if (recordFree >= 0)
        {
            var reuse = recordFree;
            recordFree = records[reuse].Next;
            return reuse;
        }

        if (recordCount == recordCap)
        {
            var previous = recordCap;
            recordCap *= 2;
            records = UndoGrow(UndoArrayId.Records, records, previous, recordCap);
        }

        return recordCount++;
    }

    void FreeRecord(int index)
    {
        undo?.Note(&records[index].Next, sizeof(int));
        records[index].Next = recordFree;
        recordFree = index;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    ref SlotState Slot(int slot)
    {
        return ref slotPages[slot >> PageShift][slot & (PageSlots - 1)];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    SlotState* SlotPtr(int slot)
    {
        return slotPages[slot >> PageShift] + (slot & (PageSlots - 1));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    int SetOf(int slot, int stat)
    {
        var block = Slot(slot).Block;
        return block >= 0 ? blocks!.Set(block, stat) : -1;
    }

    internal int PageCount => (Capacity + 255) >> 8;

    public ulong Digest
    {
        get
        {
            EnsureDigestCurrent();
            return digest;
        }
    }

    void EnsureDigestCurrent()
    {
        if (digestPagesDirty)
        {
            RecomputeDigest();
        }
    }

    static ulong DigestKey(int stat, int slot, uint bits)
    {
        unchecked
        {
            var key = ((ulong)(uint)stat << 40) ^ ((ulong)(uint)slot << 8) ^ (bits * 0x9E3779B97F4A7C15ul);
            key ^= key >> 33;
            key *= 0xFF51AFD7ED558CCDul;
            key ^= key >> 33;
            key *= 0xC4CEB9FE1A85EC53ul;
            return key ^ (key >> 33);
        }
    }

    void DigestKeyAdd(int slot, int stat, int set)
    {
        var key = DigestKey(stat, slot, Bits.OfSingle(table!.Computed(set)));
        digest += key;
        var cell = pageDigests + (nint)stat * PageCount + (slot >> 8);
        if (!digestRecomputing)
        {
            undo?.Note(cell, sizeof(ulong));
        }

        *cell += key;
    }

    void DigestMove(int slot, int stat, int oldSet, int newSet)
    {
        if (oldSet == newSet)
        {
            return;
        }

        var oldKey = oldSet >= 0 ? DigestKey(stat, slot, Bits.OfSingle(table!.Computed(oldSet))) : 0ul;
        var newKey = newSet >= 0 ? DigestKey(stat, slot, Bits.OfSingle(table!.Computed(newSet))) : 0ul;
        if (oldKey == newKey)
        {
            return;
        }

        var delta = newKey - oldKey;
        digest += delta;
        var cell = pageDigests + (nint)stat * PageCount + (slot >> 8);
        if (!digestRecomputing)
        {
            undo?.Note(cell, sizeof(ulong));
        }

        *cell += delta;
    }

    internal ulong RecomputeDigest()
    {
        digestPagesDirty = false;
        if (undo != null && undoFrames != null)
        {
            undoFrames[(nint)(undo.Top % undoFrames.Length)].PageDigestsRewritten = 1;
            digestRecomputing = true;
        }

        Memory.Clear(pageDigests, (nuint)(statCount * PageCount) * sizeof(ulong));
        digest = 0;
        for (var word = 0; word < LiveWordCount; word++)
        {
            var bits = live[word];
            while (bits != 0)
            {
                var slot = (word << 6) + Bits.Tzcnt(bits);
                bits &= bits - 1;
                var block = Slot(slot).Block;
                if (block < 0)
                {
                    continue;
                }

                for (var stat = 0; stat < statCount; stat++)
                {
                    var set = blocks!.Set(block, stat);
                    if (set >= 0)
                    {
                        DigestKeyAdd(slot, stat, set);
                    }
                }
            }
        }

        digestRecomputing = false;
        return digest;
    }

    internal int FirstDivergentPage(Board other)
    {
        EnsureDigestCurrent();
        other.EnsureDigestCurrent();
        var total = statCount * PageCount;
        for (var i = 0; i < total; i++)
        {
            if (pageDigests[i] != other.pageDigests[i])
            {
                return i;
            }
        }

        return -1;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void EnsureSlotPages(int slot)
    {
        SlotPage(slotPages, slot);
    }

    int InternTuple(int* tuple)
    {
        var existing = blocks!.Lookup(tuple);
        if (existing >= 0)
        {
            blocks.AddRefMany(existing, 1);
            return existing;
        }

        var block = blocks.Create(tuple);
        blocks.SetRefCount(block, 1);
        for (var stat = 0; stat < statCount; stat++)
        {
            if (tuple[stat] >= 0)
            {
                table!.AddRef(tuple[stat]);
            }
        }

        return block;
    }

    void DropBlockRef(int block)
    {
        if (!blocks!.ReleaseRef(block))
        {
            return;
        }

        for (var stat = 0; stat < statCount; stat++)
        {
            var set = blocks.Set(block, stat);
            if (set >= 0)
            {
                table!.ReleaseRef(set);
            }
        }

        blocks.Free(block);
    }

    int StatIndex(StatId stat)
    {
        if (statCount == 0)
        {
            throw new InvalidOperationException("sv: schema declares no stats");
        }

        if (stat.Index < 0 || stat.Index >= statCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stat), "sv: stat id out of range");
        }

        return stat.Index;
    }

    /// <summary>
    /// Binds an external output column. The pointer is stable from this bind until the next
    /// <see cref="InvalidateBindings"/> or rebind; hosts owning growable storage must rebind at reconcile.
    /// Rows of resolved pairs are written into the column immediately (dense refill).
    /// A positive <paramref name="columnLength"/> is the column's element count; SV_CHECKED builds throw
    /// when a broadcast would write past it. -1 (the default) keeps length unchecked.
    /// </summary>
    /// <summary>
    /// Points one declared source of <paramref name="stat"/> (by declaration ordinal) at
    /// <paramref name="anchorSlot"/>'s value of that source stat — the indirect-stat form of a
    /// population-wide buff: one modifier add on the anchor reaches every slot's fold, and identical
    /// outcomes intern to one set. Anchors ride the binding doctrine: they are not state —
    /// renumbering, loading or invalidating bindings clears them (rebind after), and releasing the
    /// anchor slot reverts the source to per-slot reads.
    /// </summary>
    public void BindSourceAnchor(StatId stat, int ordinal, int anchorSlot)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        var begin = sourceOffset[index];
        var total = sourceOffset[index + 1] - begin;
        if (ordinal < 0 || ordinal >= total)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"sv: stat has {total} declared sources");
        }

        if (anchorSlot < 0 || anchorSlot >= Capacity || (live[anchorSlot >> 6] & (1ul << (anchorSlot & 63))) == 0)
        {
            throw new ArgumentException($"sv: anchor slot {anchorSlot} is not live");
        }

        sourceAnchor[begin + ordinal] = anchorSlot;
    }

    internal int SourceAnchorOf(StatId stat, int ordinal)
    {
        var index = StatIndex(stat);
        return sourceAnchor[sourceOffset[index] + ordinal];
    }

    /// <summary>Reverts a source bound by <see cref="BindSourceAnchor"/> to per-slot reads.</summary>
    public void ClearSourceAnchor(StatId stat, int ordinal)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        var begin = sourceOffset[index];
        var total = sourceOffset[index + 1] - begin;
        if (ordinal < 0 || ordinal >= total)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"sv: stat has {total} declared sources");
        }

        sourceAnchor[begin + ordinal] = -1;
    }

    public void BindOutput(StatId stat, float* column, int stride = 1, int columnLength = -1)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        if (column == null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        if (stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "sv: stride must be >= 1");
        }

        if (columnLength < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnLength), "sv: columnLength must be -1 (unchecked) or >= 0");
        }

        if (columnLength >= 0 && (long)columnLength < (long)(Capacity - 1) * stride + 1)
        {
            throw new ArgumentException($"sv: bound column of {columnLength} elements cannot cover capacity {Capacity} at stride {stride}");
        }

        outputColumn[index] = column;
        outputStride[index] = stride;
        boundLength[index] = columnLength;
        bindingGeneration++;
        boundGeneration[index] = bindingGeneration;
        RefillColumn(index);
    }

    /// <summary>
    /// Binds an external output column and returns a handle capturing the binding generation;
    /// the handle reports stale after <see cref="InvalidateBindings"/> or a rebind of the same stat.
    /// The pointer doctrine matches <see cref="BindOutput"/>.
    /// </summary>
    public BindingHandle BindExternal(StatId stat, float* column, int stride = 1, int columnLength = -1)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        if (column == null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        if (stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "sv: stride must be >= 1");
        }

        if (columnLength < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnLength), "sv: columnLength must be -1 (unchecked) or >= 0");
        }

        if (columnLength >= 0 && (long)columnLength < (long)(Capacity - 1) * stride + 1)
        {
            throw new ArgumentException($"sv: bound column of {columnLength} elements cannot cover capacity {Capacity} at stride {stride}");
        }

        outputColumn[index] = column;
        outputStride[index] = stride;
        boundLength[index] = columnLength;
        bindingGeneration++;
        boundGeneration[index] = bindingGeneration;
        RefillColumn(index);
        return new BindingHandle(stat, column, stride, bindingGeneration);
    }

    /// <summary>
    /// Declares every external binding suspect (storage moved or slot space renumbered) and parks
    /// their writes: the recorded pointers are dropped in every build, so broadcasts write the
    /// sv-owned column (or nothing) until the host rebinds. Checked builds also throw on the next
    /// stat-bounds use. Rebind to re-establish freshness.
    /// </summary>
    public void InvalidateBindings()
    {
        InvalidateUndo();
        bindingGeneration++;
        for (var stat = 0; stat < statCount; stat++)
        {
            if (outputColumn[stat] != null)
            {
                outputColumn[stat] = null;
                boundGeneration[stat] = -1;
            }
        }

        if (statCount > 0)
        {
            var sourceTotal = Math.Max(sourceOffset[statCount], 1);
            for (var i = 0; i < sourceTotal; i++)
            {
                if (sourceAnchor[i] >= 0)
                {
                    MarkDependentsEverywhere(sourceStat[i]);
                }

                sourceAnchor[i] = -1;
            }
        }
    }

    /// <summary>How many stats have parked bindings awaiting a rebind after <see cref="InvalidateBindings"/>.</summary>
    public int PendingRebind
    {
        get
        {
            var count = 0;
            for (var stat = 0; stat < statCount; stat++)
            {
                if (boundGeneration[stat] == -1)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int BindingGeneration => bindingGeneration;

    internal int BoundGenerationAt(int statIndex) => boundGeneration[statIndex];

    internal int* BoundGenerationPtrAt(int statIndex) => boundGeneration + statIndex;

    internal int OutputStrideAt(int statIndex) => outputStride[statIndex];

    /// <summary>
    /// Sv-owned output column for a stat: allocates on first read (fixed capacity, never moves
    /// after), refilled from the set table, needs no bind. <see cref="Recalculate"/> broadcasts
    /// into it while no external column is bound; a stat nobody reads or binds pays no column.
    /// </summary>
    public float* OutputPtr(StatId stat)
    {
        var index = StatIndex(stat);
        return Materialize(index);
    }

    public Span<float> Output(StatId stat) => new(OutputPtr(stat), Capacity);

    /// <summary>
    /// Sv-owned int mirror of a stat's bound: <see cref="Clamp.BoundOf"/> of each row's broadcast
    /// value (clause-16 conversion), written by every broadcast from now on and filled from the
    /// current output column on first call. Capacity-sized, never moves. Host writes to a bound
    /// float column are not seen by the mirror. <see cref="Clamp.StatMirror"/> clamps against it.
    /// </summary>
    public int* MirrorBounds(StatId stat)
    {
        var index = StatIndex(stat);
        if (mirrors[index] != null)
        {
            return mirrors[index];
        }

        var mirror = arena.AllocAlignedZeroed<int>(Capacity);
        undo?.QuarantineFreshAligned(mirror, Capacity * sizeof(int));
        FillMirrorFromColumns(index, mirror);
        undo?.Note(mirrors + index, sizeof(int*));
        mirrors[index] = mirror;
        return mirror;
    }

    void FillMirrorFromColumns(int index, int* mirror)
    {
        var external = outputColumn[index];
        var stride = outputStride[index];
        for (var word = 0; word < LiveWordCount; word++)
        {
            var bits = live[word];
            while (bits != 0)
            {
                var slot = (word << 6) + Bits.Tzcnt(bits);
                bits &= bits - 1;
                mirror[slot] = Clamp.BoundOf(external == null ? SourceValue(slot, index) : external[slot * stride]);
            }
        }
    }

    float* Materialize(int stat)
    {
        var column = owned[stat];
        if (column != null)
        {
            return column;
        }

        column = arena.AllocAlignedZeroed<float>(Capacity);
        undo?.QuarantineFreshAligned(column, Capacity * sizeof(float));
        if (outputColumn[stat] == null)
        {
            for (var word = 0; word < LiveWordCount; word++)
            {
                var bits = live[word];
                while (bits != 0)
                {
                    var slot = (word << 6) + Bits.Tzcnt(bits);
                    bits &= bits - 1;
                    if (SetOf(slot, stat) >= 0)
                    {
                        column[slot] = SourceValue(slot, stat);
                    }
                }
            }
        }

        undo?.Note(owned + stat, sizeof(float*));
        owned[stat] = column;
        return column;
    }

    public float* OutputColumn(StatId stat)
    {
        return outputColumn[StatIndex(stat)];
    }

    public int SetCount => table?.LiveSetCount ?? 0;

    internal int MemberTotal(StatId stat) => table!.MemberTotal(StatIndex(stat));

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier)
    {
        return AddModifier(slot, stat, modifier, 0, long.MaxValue);
    }

    /// <summary>
    /// Adds a modifier tagged with <paramref name="source"/> (see <see cref="RemoveSource"/>) that
    /// <see cref="ExpireModifiers"/> removes once its tick reaches <paramref name="expiresAtTick"/>
    /// (<see cref="long.MaxValue"/>: never). Like every modifier change it takes effect at the next
    /// <see cref="Recalculate"/>.
    /// </summary>
    long NextInsertionId()
    {
        if (nextInsertionId >= (1L << 40) - 1)
        {
            insertionGeneration++;
            nextInsertionId = 0;
        }

        nextInsertionId++;
        return ((long)insertionGeneration << 40) | nextInsertionId;
    }

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier, int source, long expiresAtTick = long.MaxValue)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var state = ref Slot(slot);
        var id = NextInsertionId();
        var record = AllocRecord();
        undo?.Note(records + record, sizeof(ModRecord));
        records[record] = new ModRecord { Id = id, Kind = modifier.Kind, Value = modifier.Value, Source = source, Expiry = expiresAtTick, Next = state.Head, Slot = slot, Stat = index, Wheel = -1 };
        undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
        state.Head = record;
        MarkStatDirty(slot, index);
        if (expiresAtTick != long.MaxValue)
        {
            WheelInsert(record, id, expiresAtTick);
        }

        return new ModifierHandle(id);
    }

    /// <summary>
    /// Removes every modifier on <paramref name="slot"/> (all stats) carrying <paramref name="source"/>.
    /// Returns how many were removed.
    /// </summary>
    public int RemoveSource(int slot, int source)
    {
        Guard.SlotLive(slot, live, Capacity);
        var removed = 0;
        if (slotPages[slot >> PageShift] == null)
        {
            return removed;
        }

        ref var state = ref Slot(slot);
        var previous = -1;
        for (var at = state.Head; at >= 0;)
        {
            if (records[at].Source == source)
            {
                var next = records[at].Next;
                MarkStatDirty(slot, records[at].Stat);
                WheelUnlink(at);
                FreeRecord(at);
                if (previous < 0)
                {
                    undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
                    state.Head = next;
                }
                else
                {
                    undo?.Note(&records[previous].Next, sizeof(int));
                    records[previous].Next = next;
                }

                at = next;
                removed++;
                continue;
            }

            previous = at;
            at = records[at].Next;
        }

        return removed;
    }

    public void RemoveModifier(int slot, StatId stat, ModifierHandle handle)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var state = ref Slot(slot);
        var previous = -1;
        for (var at = state.Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Id == handle.Id)
            {
                if (records[at].Stat != index)
                {
                    throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
                }

                var next = records[at].Next;
                WheelUnlink(at);
                FreeRecord(at);
                if (previous < 0)
                {
                    undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
                    state.Head = next;
                }
                else
                {
                    undo?.Note(&records[previous].Next, sizeof(int));
                    records[previous].Next = next;
                }

                MarkStatDirty(slot, index);
                return;
            }

            previous = at;
        }

        throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
    }

    internal void MarkSlotStatsDirty(int slot)
    {
        MarkKeyframeDirty(slot);
        var mask = dirty + (nint)slot * statMaskWords;
        var empty = true;
        for (var w = 0; w < statMaskWords; w++)
        {
            if (mask[w] != 0)
            {
                empty = false;
                break;
            }
        }

        undo?.Note(mask, statMaskWords * sizeof(uint));
        for (var w = 0; w < statMaskWords; w++)
        {
            mask[w] = allStatsMask[w];
        }

        if (empty)
        {
            AppendTouched(slot);
        }
    }

    internal void ZeroSlotRows(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = owned[stat];
            if (column != null)
            {
                undo?.Note(column + slot, sizeof(float));
                column[slot] = 0f;
            }

            var mirror = mirrors[stat];
            if (mirror != null)
            {
                undo?.Note(mirror + slot, sizeof(int));
                mirror[slot] = 0;
            }
        }
    }

    internal void MarkSlotAcquired(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            var index = stat * LiveWordCount + (slot >> 6);
            if (acquired[index] == 0)
            {
                undo?.Note(acquiredTouched + acquiredTouchedCount, sizeof(int));
                acquiredTouched[acquiredTouchedCount++] = index;
            }

            undo?.Note(acquired + index, sizeof(ulong));
            acquired[index] |= 1ul << (slot & 63);
        }
    }

    internal void MarkSlotsAcquired(int* slots, int count)
    {
        var words = LiveWordCount;
        var at = 0;
        while (at < count)
        {
            var word = slots[at] >> 6;
            ulong bits = 0;
            while (at < count && (slots[at] >> 6) == word)
            {
                bits |= 1ul << (slots[at] & 63);
                at++;
            }

            for (var stat = 0; stat < statCount; stat++)
            {
                var index = stat * words + word;
                if (acquired[index] == 0)
                {
                    undo?.Note(acquiredTouched + acquiredTouchedCount, sizeof(int));
                    acquiredTouched[acquiredTouchedCount++] = index;
                }

                undo?.Note(acquired + index, sizeof(ulong));
                acquired[index] |= bits;
            }
        }
    }

    /// <summary>
    /// Spawns <paramref name="count"/> entities copying <paramref name="templateSlot"/>'s resolved
    /// stat vector. Slots come from the same lowest-free-first order as <see cref="Acquire"/> and
    /// are written to <paramref name="slots"/>; every spawned slot shares the template's interned
    /// set tuple (one block refcount add), materialized columns and mirrors are filled with the
    /// template's values directly, and rows are marked in <see cref="AcquiredWords"/> — no pair
    /// resolves at the next <see cref="Recalculate"/>. The template's modifiers are folded into
    /// the shared set, not copied as records: the first re-resolve of a spawned pair (a later
    /// modifier on that entity) recomputes from the entity's own modifiers alone. A template that
    /// has never resolved falls back to plain acquire semantics. Throws at slot exhaustion before
    /// mutating anything.
    /// </summary>
    public void Spawn(int templateSlot, int count, int* slots)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(Board));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "sv: spawn count must be >= 0");
        }

        if (count == 0)
        {
            return;
        }

        if (slots == null)
        {
            throw new ArgumentNullException(nameof(slots));
        }

        Guard.SlotLive(templateSlot, live, Capacity);
        if (LiveCount + count > Capacity)
        {
            throw new InvalidOperationException("sv: slot exhaustion");
        }

        var template = statCount > 0 ? Slot(templateSlot).Block : -1;
        if (template < 0)
        {
            for (var i = 0; i < count; i++)
            {
                slots[i] = Acquire();
            }

            return;
        }

        var word = freeHint;
        var words = LiveWordCount;
        SlotState* page = null;
        var pageAt = -1;
        for (var i = 0; i < count; i++)
        {
            while (live[word] == ulong.MaxValue)
            {
                word++;
            }

            var bits = ~live[word];
            var slot = (word << 6) + Bits.Tzcnt(bits);
            undo?.Note(live + word, sizeof(ulong));
            live[word] = ~bits | (1ul << (slot & 63));
            slots[i] = slot;
            MarkKeyframeDirty(slot);
            if (slot >> PageShift != pageAt)
            {
                pageAt = slot >> PageShift;
                var freshPage = slotPages[pageAt] == null;
                page = SlotPage(slotPages, slot);
                if (freshPage)
                {
                    page[slot & (PageSlots - 1)] = new SlotState { Block = template, Head = -1 };
                    continue;
                }
            }

            undo?.Note(page + (slot & (PageSlots - 1)), sizeof(SlotState));
            page[slot & (PageSlots - 1)] = new SlotState { Block = template, Head = -1 };
        }

        if (live[word] == ulong.MaxValue)
        {
            word++;
        }

        freeHint = word;
        LiveCount += count;
        MarkSlotsAcquired(slots, count);
        blocks!.AddRefMany(template, count);
        if ((long)count * statCount > 65536)
        {
            digestPagesDirty = true;
        }
        else
        {
            for (var stat = 0; stat < statCount; stat++)
            {
                var set = blocks!.Set(template, stat);
                if (set < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    DigestKeyAdd(slots[i], stat, set);
                }
            }
        }
        var last = slots[count - 1];
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = outputColumn[stat];
            var own = owned[stat];
            var mirror = mirrors[stat];
            if (column == null && own == null && mirror == null)
            {
                continue;
            }

            var value = table!.Computed(blocks.Set(template, stat));
            var stride = outputStride[stat];
            var bounded = Clamp.BoundOf(value);
            if (column != null)
            {
                Guard.BindingFresh(boundGeneration[stat]);
                Guard.BoundColumnFit(boundLength[stat], stride, last);
            }

            for (var i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (column != null)
                {
                    undo?.Note(column + (nint)slot * stride, sizeof(float));
                    column[(nint)slot * stride] = value;
                }
                else if (own != null)
                {
                    undo?.Note(own + slot, sizeof(float));
                    own[slot] = value;
                }

                if (mirror != null)
                {
                    undo?.Note(mirror + slot, sizeof(int));
                    mirror[slot] = bounded;
                }
            }
        }
    }

    internal void ClearChangedMasks()
    {
        ResetSink();
        for (var stat = 0; stat < statCount; stat++)
        {
            var @base = stat * LiveWordCount;
            var count = changedTouchedCounts[stat];
            for (var i = 0; i < count; i++)
            {
                var word = @base + changedTouched[@base + i];
                undo?.Note(changed + word, sizeof(ulong));
                changed[word] = 0;
            }

            undo?.Note(changedTouchedCounts + stat, sizeof(int));
            changedTouchedCounts[stat] = 0;
        }

        for (var i = 0; i < acquiredTouchedCount; i++)
        {
            undo?.Note(acquired + acquiredTouched[i], sizeof(ulong));
            acquired[acquiredTouched[i]] = 0;
        }

        acquiredTouchedCount = 0;
    }

    internal void ClearSlotStats(int slot)
    {
        var sourceTotal = Math.Max(sourceOffset[statCount], 1);
        for (var i = 0; i < sourceTotal; i++)
        {
            if (sourceAnchor[i] == slot)
            {
                sourceAnchor[i] = -1;
                MarkDependentsEverywhere(sourceStat[i]);
            }
        }

        ref var state = ref Slot(slot);
        for (var at = state.Head; at >= 0;)
        {
            var next = records[at].Next;
            WheelUnlink(at);
            FreeRecord(at);
            at = next;
        }

        undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
        state.Head = -1;
        if (state.Block >= 0)
        {
            var block = state.Block;
            for (var stat = 0; stat < statCount; stat++)
            {
                DigestMove(slot, stat, blocks!.Set(block, stat), -1);
            }

            undo?.Note(&SlotPtr(slot)->Block, sizeof(int));
            state.Block = -1;
            DropBlockRef(block);
        }
    }

    void MarkStatDirty(int slot, int stat)
    {
        MarkKeyframeDirty(slot);
        var mask = dirty + (nint)slot * statMaskWords;
        var empty = true;
        for (var w = 0; w < statMaskWords; w++)
        {
            if (mask[w] != 0)
            {
                empty = false;
                break;
            }
        }

        undo?.Note(mask + (stat >> 5), sizeof(uint));
        mask[stat >> 5] |= 1u << (stat & 31);
        if (empty)
        {
            AppendTouched(slot);
        }
    }

    void AppendTouched(int slot)
    {
        if (dirtyTouchedCount == dirtyTouchedCap)
        {
            var previous = dirtyTouchedCap;
            dirtyTouchedCap *= 2;
            dirtyTouched = UndoGrow(UndoArrayId.DirtyTouched, dirtyTouched, previous, dirtyTouchedCap);
        }

        undo?.Note(dirtyTouched + dirtyTouchedCount, sizeof(int));
        dirtyTouched[dirtyTouchedCount++] = slot;
    }

    void ClearDirty()
    {
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var mask = dirty + (nint)dirtyTouched[i] * statMaskWords;
            undo?.Note(mask, statMaskWords * sizeof(uint));
            for (var w = 0; w < statMaskWords; w++)
            {
                mask[w] = 0;
            }
        }

        dirtyTouchedCount = 0;
    }

    static void SortTouched(int* items, int count)
    {
        for (var i = count / 2 - 1; i >= 0; i--)
        {
            SiftTouched(items, i, count);
        }

        for (var end = count - 1; end > 0; end--)
        {
            var top = items[0];
            items[0] = items[end];
            items[end] = top;
            SiftTouched(items, 0, end);
        }
    }

    static void SiftTouched(int* items, int at, int count)
    {
        while (true)
        {
            var child = at * 2 + 1;
            if (child >= count)
            {
                return;
            }

            if (child + 1 < count && items[child + 1] > items[child])
            {
                child++;
            }

            if (items[child] <= items[at])
            {
                return;
            }

            var swap = items[at];
            items[at] = items[child];
            items[child] = swap;
            at = child;
        }
    }

    public void Recalculate()
    {
        if (statCount == 0)
        {
            return;
        }

        ClearChangedMasks();
        table!.Tick();
        undo?.NoteRange(dirtyTouched, dirtyTouchedCount);
        ResolveAnchors();
        SortTouched(dirtyTouched, dirtyTouchedCount);
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var slot = dirtyTouched[i];
            var mask = dirty + (nint)slot * statMaskWords;
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                undo?.Note(mask, statMaskWords * sizeof(uint));
                for (var k = 0; k < statMaskWords; k++)
                {
                    mask[k] = 0;
                }

                continue;
            }

            var w = 0;
            while (w < statMaskWords)
            {
                var word = mask[w];
                if (word == 0)
                {
                    w++;
                    continue;
                }

                var stat = (w << 5) + Bits.Tzcnt(word);
                undo?.Note(mask + w, sizeof(uint));
                mask[w] = word & (word - 1);
                ResolvePair(slot, stat);
            }
        }

        dirtyTouchedCount = 0;
        ShrinkHighWater();
    }

    void ResolveAnchors()
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            for (var at = sourceOffset[stat]; at < sourceOffset[stat + 1]; at++)
            {
                var anchor = sourceAnchor[at];
                if (anchor < 0 || (live[anchor >> 6] & (1ul << (anchor & 63))) == 0)
                {
                    continue;
                }

                var source = sourceStat[at];
                var mask = dirty + (nint)anchor * statMaskWords;
                if ((mask[source >> 5] & (1u << (source & 31))) == 0)
                {
                    continue;
                }

                undo?.Note(mask + (source >> 5), sizeof(uint));
                mask[source >> 5] &= ~(1u << (source & 31));
                if (!ResolvePair(anchor, source))
                {
                    continue;
                }



                MarkDependentsEverywhere(source);
            }
        }
    }

    void MarkDependentsEverywhere(int source)
    {
        var dependents = dependentsMask + (nint)source * statMaskWords;
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            for (var w = 0; w < statMaskWords; w++)
            {
                var word = dependents[w];
                while (word != 0)
                {
                    var dependent = (w << 5) + Bits.Tzcnt(word);
                    word &= word - 1;
                    MarkStatDirty(slot, dependent);
                }
            }
        }
    }

    bool ResolvePair(int slot, int stat)
    {
        var block = Slot(slot).Block;
        var current = block >= 0 ? blocks!.Set(block, stat) : -1;
        var before = current >= 0 ? table!.Computed(current) : 0f;
        var count = Gather(slot, stat);
        var set = table!.Resolve(stat, scratch, count);
        if (!table.Folded(set))
        {
            var freshContent = table.Content(set, out var freshCount);
            table.SetComputed(set, CanonicalNaN(Fold(statBase[stat], freshContent, freshCount)));
        }

        if (current != set)
        {
            if (block >= 0)
            {
                blocks!.CopyTuple(block, tupleScratch);
            }
            else
            {
                for (var k = 0; k < statCount; k++)
                {
                    tupleScratch[k] = -1;
                }
            }

            tupleScratch[stat] = set;
            var next = InternTuple(tupleScratch);
            for (var k = 0; k < statCount; k++)
            {
                DigestMove(slot, k, k == stat ? current : tupleScratch[k], blocks!.Set(next, k));
            }

            if (block >= 0)
            {
                DropBlockRef(block);
            }

            undo?.Note(&SlotPtr(slot)->Block, sizeof(int));
            Slot(slot).Block = next;
            var dependents = dependentsMask + (nint)stat * statMaskWords;
            var mask = dirty + (nint)slot * statMaskWords;
            undo?.Note(mask, statMaskWords * sizeof(uint));
            for (var k = 0; k < statMaskWords; k++)
            {
                mask[k] |= dependents[k];
            }
        }

        var computed = table.Computed(set);
        BroadcastOne(stat, slot, computed, before);
        return before != computed;
    }

    void ShrinkHighWater()
    {
        if (recordCap > 16 && (long)recordCount * 4 <= (long)recordCap * 3)
        {
            var previous = recordCap;
            var target = recordCount + (recordCount >> 3);
            recordCap = target < 16 ? 16 : target;
            records = UndoGrow(UndoArrayId.Records, records, previous, recordCap);
        }

        if (dirtyTouchedCap > 64 && (long)dirtyTouchedCount * 4 <= (long)dirtyTouchedCap * 3)
        {
            var previous = dirtyTouchedCap;
            var target = dirtyTouchedCount + (dirtyTouchedCount >> 3);
            dirtyTouchedCap = target < 64 ? 64 : target;
            dirtyTouched = UndoGrow(UndoArrayId.DirtyTouched, dirtyTouched, previous, dirtyTouchedCap);
        }

        ShrinkWheel();
    }

    void RefillColumn(int stat)
    {
        var column = outputColumn[stat];
        if (column == null)
        {
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            if (SetOf(slot, stat) < 0)
            {
                continue;
            }

            Guard.BoundColumnFit(boundLength[stat], stride, slot);
            column[slot * stride] = SourceValue(slot, stat);
        }
    }

    int Gather(int slot, int stat)
    {
        var synthetic = sourceOffset[stat + 1] - sourceOffset[stat];
        var real = RecordCount(slot, stat);
        var count = synthetic + real;
        if (count == 0)
        {
            return 0;
        }

        if (count > scratchCap)
        {
            var previous = scratchCap;
            scratchCap = count * 2;
            scratch = arena.Realloc(scratch, previous, scratchCap);
            scratchIds = arena.Realloc(scratchIds, previous, scratchCap);
        }

        return GatherInto(slot, stat, scratch, scratchIds);
    }

    int RecordCount(int slot, int stat)
    {
        var count = 0;
        for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Stat == stat)
            {
                count++;
            }
        }

        return count;
    }

    int CurveBucket(int at, float value) => StatMath.CurveBucket(value, curveMin[at], curveCeiling[at], curveStep[at], curveLength[at]);

    int GatherInto(int slot, int stat, StatEntry* entries, long* entryIds)
    {
        var synthetic = sourceOffset[stat + 1] - sourceOffset[stat];
        var count = synthetic;
        for (var k = 0; k < synthetic; k++)
        {
            var at = sourceOffset[stat] + k;
            var anchor = sourceAnchor[at];
            var sourceValue = SourceValue(anchor >= 0 ? anchor : slot, sourceStat[at]);
            var dataAt = sourceCurve[at];
            entries[k] = new StatEntry(ModifierKind.Added, dataAt < 0 ? StatMath.Derived(sourceCoefficient[at], sourceValue) : curveData[dataAt + CurveBucket(at, sourceValue)]);
            entryIds[k] = long.MinValue + k;
        }

        for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Stat != stat)
            {
                continue;
            }

            entries[count] = new StatEntry(records[at].Kind, records[at].Value);
            entryIds[count] = records[at].Id;
            count++;
        }

        Canonicalize(entries, entryIds, count);
        return count;
    }

    float SourceValue(int slot, int stat)
    {
        var set = SetOf(slot, stat);
        return set >= 0 && table!.Folded(set) ? table.Computed(set) : statBase[stat];
    }

    void BroadcastOne(int stat, int slot, float computed, float before)
    {
        var mirror = mirrors[stat];
        if (mirror != null)
        {
            undo?.Note(mirror + slot, sizeof(int));
            mirror[slot] = Clamp.BoundOf(computed);
        }

        var column = outputColumn[stat];
        if (column == null)
        {
            var own = owned[stat];
            if (own == null)
            {
                MarkChanged(stat, slot, before, computed);
                return;
            }

            MarkChanged(stat, slot, own[slot], computed);
            undo?.Note(own + slot, sizeof(float));
            own[slot] = computed;
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        Guard.BoundColumnFit(boundLength[stat], stride, slot);
        MarkChanged(stat, slot, column[(nint)slot * stride], computed);
        undo?.Note(column + (nint)slot * stride, sizeof(float));
        column[(nint)slot * stride] = computed;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void MarkChanged(int stat, int slot, float before, float after)
    {
        if (Bits.OfSingle(before) == Bits.OfSingle(after))
        {
            return;
        }

        var word = slot >> 6;
        var @base = stat * LiveWordCount;
        if (changed[@base + word] == 0)
        {
            undo?.Note(changedTouched + @base + changedTouchedCounts[stat], sizeof(int));
            changedTouched[@base + changedTouchedCounts[stat]] = word;
            undo?.Note(changedTouchedCounts + stat, sizeof(int));
            changedTouchedCounts[stat]++;
        }

        undo?.Note(changed + @base + word, sizeof(ulong));
        changed[@base + word] |= 1ul << (slot & 63);
    }

    /// <summary>
    /// Changed mask of the last <see cref="Recalculate"/> for a stat: bit i of word w is set iff row
    /// w*64+i was broadcast a value whose bits differ from what its output column held before.
    /// <see cref="LiveWordCount"/> words, valid until the next Recalculate — the clause-5 word-span
    /// shape, so it feeds <see cref="RunEnabledChunk(int*, int, in Clamp, ulong*, int, int, int*)"/> directly.
    /// </summary>
    public ulong* ChangedWords(StatId stat)
    {
        return changed + (nint)StatIndex(stat) * LiveWordCount;
    }

    /// <summary>
    /// Sparse twin of <see cref="ChangedWords(StatId)"/>: the indices of that stat's nonzero changed
    /// words, <see cref="ChangedWordCount(StatId)"/> of them, each at most once in arrival order.
    /// A consumer walks O(changed words) instead of <see cref="LiveWordCount"/>; values are the same
    /// bits the dense mask holds, valid until the next <see cref="Recalculate"/>.
    /// </summary>
    public int* ChangedWordIndices(StatId stat)
    {
        return changedTouched + (nint)StatIndex(stat) * LiveWordCount;
    }

    /// <summary>
    /// Count of <see cref="ChangedWordIndices(StatId)"/> entries for the stat.
    /// </summary>
    public int ChangedWordCount(StatId stat)
    {
        return changedTouchedCounts[StatIndex(stat)];
    }

    /// <summary>
    /// Acquired mask of the slots acquired since the last <see cref="Recalculate"/>: bit i of word w
    /// marks row w*64+i. Set by <see cref="Acquire"/>, cleared when the next Recalculate begins —
    /// read it in the spawn-to-recalc window (clause-7 step 2 to step 3). Changed words carry value
    /// changes only, so a stat whose base is 0 spawns visibly here and never there.
    /// </summary>
    public ulong* AcquiredWords(StatId stat)
    {
        return acquired + (nint)StatIndex(stat) * LiveWordCount;
    }

    static void Canonicalize(StatEntry* entries, long* ids, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var entry = entries[i];
            var id = ids[i];
            var bits = Bits.OfSingle(entry.Value);
            var j = i - 1;
            while (j >= 0 && (entries[j].Kind > entry.Kind || (entries[j].Kind == entry.Kind && Bits.OfSingle(entries[j].Value) > bits)))
            {
                entries[j + 1] = entries[j];
                ids[j + 1] = ids[j];
                j--;
            }

            entries[j + 1] = entry;
            ids[j + 1] = id;
        }
    }

    internal static float CanonicalNaN(float value)
    {
        return float.IsNaN(value) ? Bits.ToSingle(0x7FC00000u) : value;
    }

    internal static float Fold(float @base, StatEntry* entries, int count) => StatMath.Fold(@base, entries, count);

    public float Stat(int slot, StatId stat)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var synthetic = sourceOffset[index + 1] - sourceOffset[index];
        var need = synthetic + RecordCount(slot, index);
        if (statScratchCap < need)
        {
            var previous = statScratchCap;
            statScratchCap = need * 2;
            statScratch = arena.Realloc(statScratch, previous, statScratchCap);
            statScratchIds = arena.Realloc(statScratchIds, previous, statScratchCap);
        }

        var count = GatherInto(slot, index, statScratch, statScratchIds);
        return CanonicalNaN(Fold(statBase[index], statScratch, count));
    }

    /// <summary>
    /// Compacts the slot space: live slots map through <paramref name="oldToNew"/> (negative entries
    /// are dead slots) to distinct targets in [0, capacity) — gaps are legal (64-alignment padding).
    /// Rewrites pair states and sv-owned columns; external bindings are invalidated per the
    /// clause-12 doctrine. Pending dirty state is remapped through <paramref name="oldToNew"/> —
    /// clean pairs stay clean (set ids are slot-independent, modifier records are pool-global) —
    /// so the next <see cref="Recalculate"/> resolves only the pairs that were already dirty.
    /// Modifier insertion ids ride along, so stat folds are preserved bit-exactly.
    /// </summary>
    public void RenumberSlots(ReadOnlySpan<int> oldToNew, int count)
    {
        InvalidateUndo();
        MarkAllKeyframesDirty();
        if (count < 0 || count > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"sv: renumber count must be in [0, {Capacity}]");
        }

        if (count != LiveCount)
        {
            throw new InvalidOperationException($"sv: renumber count {count} != live count {LiveCount}");
        }

        if (oldToNew.Length != Capacity)
        {
            throw new ArgumentException($"sv: renumber map must cover capacity ({Capacity} entries)");
        }

        var words = (Capacity + 63) >> 6;
        var newLive = arena.Alloc<ulong>(words);
        Memory.Clear(newLive, (nuint)words * sizeof(ulong));
        var seen = arena.Alloc<int>(Capacity);
        for (var i = 0; i < Capacity; i++)
        {
            seen[i] = -1;
        }

        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            if (next < 0 || next >= Capacity || seen[next] >= 0)
            {
                arena.Free(newLive, words);
                arena.Free(seen, Capacity);
                throw new ArgumentException("sv: renumber map does not map live slots to distinct targets");
            }

            seen[next] = slot;
            newLive[next >> 6] |= 1ul << (next & 63);
        }

        if (statCount > 0)
        {
            RenumberStats(oldToNew, newLive);
            RemapDirty(oldToNew);
        }

        new ReadOnlySpan<ulong>(newLive, words).CopyTo(new Span<ulong>(live, words));
        arena.Free(newLive, words);
        var hint = 0;
        while (hint < words && live[hint] == ulong.MaxValue)
        {
            hint++;
        }

        freeHint = hint;
        arena.Free(seen, Capacity);

        if (statCount > 0)
        {
            ClearChangedMasks();
        }

        InvalidateBindings();
    }

    void VisitStats<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
    {
        var words = LiveWordCount;
        codec.Value(ref nextInsertionId);
        codec.Value(ref insertionGeneration);
        var pending = dirtyTouchedCount;
        codec.Value(ref pending);
        if (codec.Reading)
        {
            if (pending < 0 || pending > Capacity)
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            if (pending > dirtyTouchedCap)
            {
                var previous = dirtyTouchedCap;
                dirtyTouchedCap = pending;
                dirtyTouched = arena.Realloc(dirtyTouched, previous, dirtyTouchedCap);
            }

            ClearDirty();
            dirtyTouchedCount = pending;
        }

        for (var i = 0; i < pending; i++)
        {
            var slot = codec.Reading ? 0 : dirtyTouched[i];
            codec.Value(ref slot);
            if (codec.Reading)
            {
                if (slot < 0 || slot >= Capacity)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt board state");
                }

                dirtyTouched[i] = slot;
            }

            codec.Bytes(dirty + (nint)slot * statMaskWords, statMaskWords * sizeof(uint));
        }

        codec.Bytes(changedTouchedCounts, (long)statCount * sizeof(int));
        for (var stat = 0; stat < statCount; stat++)
        {
            var touched = changedTouchedCounts[stat];
            if (codec.Reading && (touched < 0 || touched > words))
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            codec.Bytes(changedTouched + (nint)stat * words, (long)touched * sizeof(int));
        }

        codec.Bytes(changed, (long)statCount * words * sizeof(ulong));
        var acquiredCount = acquiredTouchedCount;
        codec.Value(ref acquiredCount);
        if (codec.Reading)
        {
            acquiredTouchedCount = acquiredCount;
        }

        codec.Bytes(acquiredTouched, (long)acquiredCount * sizeof(int));
        codec.Bytes(acquired, (long)statCount * words * sizeof(ulong));
        var staleColumns = codec.Reading ? arena.Alloc<int>(statCount) : null;
        var staleCount = 0;
        for (var stat = 0; stat < statCount; stat++)
        {
            var hasOwned = (byte)(owned[stat] != null ? 1 : 0);
            codec.Value(ref hasOwned);
            if (hasOwned != 0)
            {
                if (codec.Reading && owned[stat] == null)
                {
                    owned[stat] = arena.AllocAlignedZeroed<float>(Capacity);
                }

                codec.Bytes(owned[stat], (long)Capacity * sizeof(float));
            }
            else if (codec.Reading && owned[stat] != null)
            {
                staleColumns![staleCount++] = stat;
            }
        }

        var staleMirrors = codec.Reading ? arena.Alloc<int>(statCount) : null;
        var staleMirrorCount = 0;
        for (var stat = 0; stat < statCount; stat++)
        {
            var hasMirror = (byte)(mirrors[stat] != null ? 1 : 0);
            codec.Value(ref hasMirror);
            if (hasMirror == 0)
            {
                if (codec.Reading && mirrors[stat] != null && outputColumn[stat] == null)
                {
                    staleMirrors![staleMirrorCount++] = stat;
                }

                continue;
            }

            if (codec.Reading && mirrors[stat] == null)
            {
                mirrors[stat] = arena.AllocAlignedZeroed<int>(Capacity);
            }

            codec.Bytes(mirrors[stat], (long)Capacity * sizeof(int));
        }

        var poolCount = recordCount;
        codec.Value(ref poolCount);
        if (codec.Reading)
        {
            if (poolCount < 0 || poolCount > (1 << 24))
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            if (poolCount > recordCap)
            {
                var previous = recordCap;
                recordCap = poolCount;
                records = arena.Realloc(records, previous, recordCap);
            }

            recordCount = poolCount;
        }

        codec.Bytes(records, (long)poolCount * sizeof(ModRecord));
        codec.Value(ref recordFree);
        if (codec.Reading && (recordFree < -1 || recordFree >= poolCount))
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        for (var page = 0; page < pageCount; page++)
        {
            var states = slotPages[page];
            var hasPage = (byte)(states == null ? 0 : 1);
            codec.Value(ref hasPage);
            if (hasPage == 0)
            {
                if (codec.Reading && slotPages[page] != null)
                {
                    arena.Free(slotPages[page], PageSlots);
                    slotPages[page] = null;
                }

                continue;
            }

            if (codec.Reading && slotPages[page] == null)
            {
                slotPages[page] = arena.Alloc<SlotState>(PageSlots);
                for (var i = 0; i < PageSlots; i++)
                {
                    slotPages[page][i] = new SlotState { Block = -1, Head = -1 };
                }
            }

            codec.Bytes(slotPages[page], (long)PageSlots * sizeof(SlotState));
        }

        blocks!.VisitState(ref codec);
        table!.VisitState(ref codec);
        if (codec.Reading)
        {
            for (var i = 0; i < staleCount; i++)
            {
                var stat = staleColumns![i];
                Memory.Clear(owned[stat], (nuint)Capacity * sizeof(float));
                if (outputColumn[stat] != null)
                {
                    continue;
                }

                for (var word = 0; word < LiveWordCount; word++)
                {
                    var bits = live[word];
                    while (bits != 0)
                    {
                        var slot = (word << 6) + Bits.Tzcnt(bits);
                        bits &= bits - 1;
                        if (SetOf(slot, stat) >= 0)
                        {
                            owned[stat][slot] = SourceValue(slot, stat);
                        }
                    }
                }
            }

            for (var i = 0; i < staleMirrorCount; i++)
            {
                var stat = staleMirrors![i];
                Memory.Clear(mirrors[stat], (nuint)Capacity * sizeof(int));
                FillMirrorFromColumns(stat, mirrors[stat]);
            }

            arena.Free(staleColumns!, statCount);
            arena.Free(staleMirrors!, statCount);
            RefillRestoredBindings();
            wheelTick = 0;
            RebuildWheel();
            RecomputeDigest();
        }
    }

    void RefillRestoredBindings()
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            RefillColumn(stat);
        }
    }

    void RemapDirty(ReadOnlySpan<int> oldToNew)
    {
        var total = dirtyTouchedCount;
        if (total == 0)
        {
            return;
        }

        var masks = arena.Alloc<uint>(total * statMaskWords);
        for (var i = 0; i < total; i++)
        {
            var source = dirty + (nint)dirtyTouched[i] * statMaskWords;
            new ReadOnlySpan<uint>(source, statMaskWords).CopyTo(new Span<uint>(masks + (nint)i * statMaskWords, statMaskWords));
            Memory.Clear(source, (nuint)statMaskWords * sizeof(uint));
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            var slot = dirtyTouched[i];
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            dirtyTouched[count++] = next;
            new ReadOnlySpan<uint>(masks + (nint)i * statMaskWords, statMaskWords).CopyTo(new Span<uint>(dirty + (nint)next * statMaskWords, statMaskWords));
        }

        dirtyTouchedCount = count;
        arena.Free(masks, total * statMaskWords);
    }

    void RenumberStats(ReadOnlySpan<int> oldToNew, ulong* newLive)
    {
        EnsureDigestCurrent();
        var movedCap = LiveCount * 2;
        var moved = LiveCount > 0 ? arena.Alloc<int>(movedCap) : null;
        var movedCount = 0;
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            if (next != slot)
            {
                moved![movedCount * 2] = slot;
                moved[movedCount * 2 + 1] = next;
                movedCount++;
            }
        }

        if (movedCount == 0)
        {
            if (moved != null)
            {
                arena.Free(moved, movedCap);
            }

            return;
        }

        var touchedWords = pageCount + 63 >> 6;
        var touched = arena.AllocZeroed<ulong>(touchedWords);
        for (var i = 0; i < movedCount; i++)
        {
            touched[moved![i * 2] >> PageShift >> 6] |= 1ul << ((moved[i * 2] >> PageShift) & 63);
            touched[moved[i * 2 + 1] >> PageShift >> 6] |= 1ul << ((moved[i * 2 + 1] >> PageShift) & 63);
        }

        var nextPages = (SlotState**)arena.AllocZeroedBytes(pageCount * sizeof(SlotState*));
        for (var page = 0; page < pageCount; page++)
        {
            var old = slotPages[page];
            if (old == null)
            {
                continue;
            }

            var bit = 1ul << (page & 63);
            if ((touched[page >> 6] & bit) == 0)
            {
                nextPages[page] = old;
            }
            else
            {
                var copy = arena.Alloc<SlotState>(PageSlots);
                new ReadOnlySpan<SlotState>(old, PageSlots).CopyTo(new Span<SlotState>(copy, PageSlots));
                nextPages[page] = copy;
            }
        }

        for (var i = 0; i < movedCount; i++)
        {
            var slot = moved![i * 2];
            var next = moved[i * 2 + 1];
            var block = Slot(slot).Block;
            SlotPage(nextPages, next)[next & (PageSlots - 1)] = Slot(slot);
            for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
            {
                records[at].Slot = next;
            }

            if (block < 0)
            {
                continue;
            }

            for (var stat = 0; stat < statCount; stat++)
            {
                var set = blocks!.Set(block, stat);
                if (set < 0)
                {
                    continue;
                }

                var bits = Bits.OfSingle(table!.Computed(set));
                var oldKey = DigestKey(stat, slot, bits);
                var newKey = DigestKey(stat, next, bits);
                digest += newKey - oldKey;
                pageDigests[(nint)stat * PageCount + (slot >> 8)] -= oldKey;
                pageDigests[(nint)stat * PageCount + (next >> 8)] += newKey;
            }
        }

        var clearWords = LiveWordCount;
        var clearTail = Capacity & 63;
        for (var word = 0; word < clearWords; word++)
        {
            var bits = ~newLive[word];
            if (word == clearWords - 1 && clearTail != 0)
            {
                bits &= (1ul << clearTail) - 1;
            }

            while (bits != 0)
            {
                var deadSlot = (word << 6) + Bits.Tzcnt(bits);
                bits &= bits - 1;
                if (nextPages[deadSlot >> PageShift] != null)
                {
                    nextPages[deadSlot >> PageShift][deadSlot & (PageSlots - 1)] = new SlotState { Block = -1, Head = -1 };
                }
            }
        }

        for (var page = 0; page < pageCount; page++)
        {
            if (slotPages[page] != null && slotPages[page] != nextPages[page])
            {
                arena.Free(slotPages[page], PageSlots);
            }
        }

        arena.FreeBytes(slotPages, pageCount * sizeof(SlotState*));
        slotPages = nextPages;
        arena.Free(touched, touchedWords);

        var deadCount = Capacity - LiveCount;
        var dead = deadCount > 0 ? arena.Alloc<int>(deadCount) : null;
        var found = 0;
        var maskWords = LiveWordCount;
        var tail = Capacity & 63;
        for (var word = 0; word < maskWords; word++)
        {
            var bits = ~newLive[word];
            if (word == maskWords - 1 && tail != 0)
            {
                bits &= (1ul << tail) - 1;
            }

            while (bits != 0)
            {
                var slot = (word << 6) + Bits.Tzcnt(bits);
                bits &= bits - 1;
                dead![found++] = slot;
            }
        }

        var tempRows = arena.Alloc<float>(Capacity);
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = owned[stat];
            if (column == null)
            {
                continue;
            }

            Memory.Copy(column, tempRows, (nuint)Capacity * sizeof(float));
            for (var j = 0; j < movedCount; j++)
            {
                tempRows[moved![j * 2 + 1]] = column[moved[j * 2]];
            }

            for (var j = 0; j < found; j++)
            {
                tempRows[dead![j]] = 0f;
            }

            new ReadOnlySpan<float>(tempRows, Capacity).CopyTo(new Span<float>(column, Capacity));
            var mirror = mirrors[stat];
            if (mirror == null)
            {
                continue;
            }

            var mirrorRows = (int*)tempRows;
            Memory.Copy(mirror, mirrorRows, (nuint)Capacity * sizeof(int));
            for (var j = 0; j < movedCount; j++)
            {
                mirrorRows[moved![j * 2 + 1]] = mirror[moved[j * 2]];
            }

            for (var j = 0; j < found; j++)
            {
                mirrorRows[dead![j]] = 0;
            }

            new ReadOnlySpan<int>(mirrorRows, Capacity).CopyTo(new Span<int>(mirror, Capacity));
        }

        arena.Free(tempRows, Capacity);
        if (dead != null)
        {
            arena.Free(dead, deadCount);
        }

        arena.Free(moved!, movedCap);
    }
}
}
`````

### sv src/Sv/SetTable.cs

Path: `sv/src/Sv/SetTable.cs`  (22888 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
    internal readonly struct StatEntry
    {
        public readonly ModifierKind Kind;

        public readonly float Value;

        public StatEntry(ModifierKind kind, float value)
        {
            Kind = kind;
            Value = value;
        }
    }

    internal sealed unsafe class SetTable : IDisposable
    {
        private const int ClassCount = 32;

        internal long ReclaimHorizon;

        internal UndoLog? Undo;

        private readonly NativeArena memory;

        private int setCount;

        private int setCapacity;

        private int bucketMask;

        private int* buckets;

        private int* setStat;

        private ulong* setHash;

        private float* setComputed;

        private int* setContentOffset;

        private int* setContentCount;

        private int* setContentSpan;

        private int* setRefCount;

        private sbyte* setFolded;

        private int* setFreeNext;

        private long* setFreedAt;

        private int setFreeHead;

        private int freeWalkPrev = -1;

        private long freeWalkGeneration = -1;

        private long freeWalkHorizon = -1;

        private int liveSets;

        private long generation;

        private StatEntry* contentArena;

        private int contentUsed;

        private int contentCapacity;

        private int* contentClassHead;

        private bool disposed;

        public int LiveSetCount => liveSets;

        public SetTable(NativeArena memory)
        {
            this.memory = memory;
            setCapacity = 16;
            bucketMask = 31;
            buckets = memory.AllocZeroed<int>(32);
            setStat = memory.Alloc<int>(setCapacity);
            setHash = memory.Alloc<ulong>(setCapacity);
            setComputed = memory.Alloc<float>(setCapacity);
            setContentOffset = memory.Alloc<int>(setCapacity);
            setContentCount = memory.Alloc<int>(setCapacity);
            setContentSpan = memory.Alloc<int>(setCapacity);
            setRefCount = memory.AllocZeroed<int>(setCapacity);
            setFolded = memory.AllocZeroed<sbyte>(setCapacity);
            setFreeNext = memory.AllocZeroed<int>(setCapacity);
            setFreedAt = memory.AllocZeroed<long>(setCapacity);
            contentArena = memory.Alloc<StatEntry>(64);
            contentCapacity = 64;
            contentClassHead = memory.Alloc<int>(ClassCount);
            for (var i = 0; i < ClassCount; i++)
            {
                contentClassHead[i] = -1;
            }

            setFreeHead = -1;
        }

        static int ClassSize(int count)
        {
            var n = count < 1 ? 1 : count;
            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }

        static ulong ContentHash(int stat, StatEntry* entries, int count)
        {
            unchecked
            {
                var hash = 14695981039346656037ul;
                void Mix(ulong value)
                {
                    hash ^= value;
                    hash *= 1099511628211ul;
                }

                Mix((ulong)stat);
                for (var i = 0; i < count; i++)
                {
                    Mix((ulong)entries[i].Kind | ((ulong)Bits.OfSingle(entries[i].Value) << 8));
                }

                hash ^= hash >> 33;
                hash *= 0xFF51AFD7ED558CCDul;
                hash ^= hash >> 33;
                hash *= 0xC4CEB9FE1A85EC53ul;
                hash ^= hash >> 33;
                return hash;
            }
        }

        internal ulong HashOf(int stat, StatEntry* entries, int count) => ContentHash(stat, entries, count);

        internal int BucketCount => bucketMask + 1;

        public void Tick()
        {
            generation++;
        }

        public int Resolve(int stat, StatEntry* canonical, int count)
        {
            var hash = ContentHash(stat, canonical, count);
            var bucket = (int)(hash & (ulong)bucketMask);
            for (var probe = 0; probe <= bucketMask; probe++)
            {
                var slot = (bucket + probe) & bucketMask;
                var existing = buckets[slot];
                if (existing == 0)
                {
                    if ((liveSets + 1) * 2 > bucketMask + 1)
                    {
                        GrowBuckets();
                        return Resolve(stat, canonical, count);
                    }

                    var set = CreateSet(stat, canonical, count, hash);
                    Undo?.Note(buckets + slot, sizeof(int));
                    buckets[slot] = set + 1;
                    return set;
                }

                var index = existing - 1;
                if (setHash[index] == hash && setStat[index] == stat && setContentCount[index] == count &&
                    ContentEquals(setContentOffset[index], canonical, count))
                {
                    return index;
                }
            }

            GrowBuckets();
            return Resolve(stat, canonical, count);
        }

        bool ContentEquals(int offset, StatEntry* canonical, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (contentArena[offset + i].Kind != canonical[i].Kind ||
                    Bits.OfSingle(contentArena[offset + i].Value) != Bits.OfSingle(canonical[i].Value))
                {
                    return false;
                }
            }

            return true;
        }

        int CreateSet(int stat, StatEntry* canonical, int count, ulong hash)
        {
            if (setCount == setCapacity)
            {
                GrowSetCapacity();
            }

            int index;
            var prev = ReclaimablePrev();
            var reclaim = prev < 0 ? setFreeHead : setFreeNext[prev];
            if (reclaim >= 0)
            {
                index = reclaim;
                if (prev < 0)
                {
                    setFreeHead = setFreeNext[index];
                }
                else
                {
                    Undo?.Note(setFreeNext + prev, sizeof(int));
                    setFreeNext[prev] = setFreeNext[index];
                }

                liveSets++;
            }
            else
            {
                index = setCount++;
                liveSets++;
            }

            var undo = Undo;
            undo?.Note(setStat + index, sizeof(int));
            setStat[index] = stat;
            undo?.Note(setHash + index, sizeof(ulong));
            setHash[index] = hash;
            var span = ClassSize(count);
            int offset;
            var cls = Bits.Tzcnt(span);
            if (contentClassHead[cls] >= 0)
            {
                offset = contentClassHead[cls];
                undo?.Note(contentClassHead + cls, sizeof(int));
                contentClassHead[cls] = *(int*)(contentArena + offset);
            }
            else
            {
                while (contentUsed + span > contentCapacity)
                {
                    var previousContent = contentCapacity;
                    contentCapacity *= 2;
                    contentArena = GrowContent(contentArena, previousContent, contentCapacity);
                }

                offset = contentUsed;
                contentUsed += span;
            }

            undo?.Note(setContentOffset + index, sizeof(int));
            setContentOffset[index] = offset;
            undo?.Note(setContentSpan + index, sizeof(int));
            setContentSpan[index] = span;
            undo?.Note(setContentCount + index, sizeof(int));
            setContentCount[index] = count;
            undo?.NoteRange(contentArena + offset, count);
            for (var i = 0; i < count; i++)
            {
                contentArena[offset + i] = canonical[i];
            }

            undo?.Note(contentArena + offset + count, (span - count) * sizeof(StatEntry));
            Memory.Clear(contentArena + offset + count, (nuint)(span - count) * (nuint)sizeof(StatEntry));
            undo?.Note(setRefCount + index, sizeof(int));
            setRefCount[index] = 0;
            undo?.Note(setComputed + index, sizeof(float));
            setComputed[index] = 0f;
            undo?.Note(setFolded + index, sizeof(sbyte));
            setFolded[index] = 0;
            undo?.Note(setFreedAt + index, sizeof(long));
            setFreedAt[index] = 0;
            undo?.Note(setFreeNext + index, sizeof(int));
            setFreeNext[index] = 0;
            return index;
        }

        StatEntry* GrowContent(StatEntry* old, int oldCount, int newCount)
        {
            return Undo == null ? memory.Realloc(old, oldCount, newCount) : Undo.ReallocTracked(memory, UndoArrayId.ContentArena, old, oldCount, newCount);
        }

        void GrowSetCapacity()
        {
            var previous = setCapacity;
            setCapacity *= 2;
            setStat = GrowArray(UndoArrayId.SetStat, setStat, previous, setCapacity);
            setHash = GrowArray(UndoArrayId.SetHash, setHash, previous, setCapacity);
            setComputed = GrowArray(UndoArrayId.SetComputed, setComputed, previous, setCapacity);
            setContentOffset = GrowArray(UndoArrayId.SetContentOffset, setContentOffset, previous, setCapacity);
            setContentCount = GrowArray(UndoArrayId.SetContentCount, setContentCount, previous, setCapacity);
            setContentSpan = GrowArray(UndoArrayId.SetContentSpan, setContentSpan, previous, setCapacity);
            setRefCount = GrowArray(UndoArrayId.SetRefCount, setRefCount, previous, setCapacity);
            setFolded = GrowArray(UndoArrayId.SetFolded, setFolded, previous, setCapacity);
            setFreeNext = GrowArray(UndoArrayId.SetFreeNext, setFreeNext, previous, setCapacity);
            setFreedAt = GrowArray(UndoArrayId.SetFreedAt, setFreedAt, previous, setCapacity);
            Memory.Clear(setFreedAt + previous, (nuint)(setCapacity - previous) * (nuint)sizeof(long));
        }

        T* GrowArray<T>(UndoArrayId id, T* old, int oldCount, int newCount) where T : unmanaged
        {
            return Undo == null ? memory.Realloc(old, oldCount, newCount) : Undo.ReallocTracked(memory, id, old, oldCount, newCount);
        }

        public void AddRef(int set)
        {
            Undo?.Note(setRefCount + set, sizeof(int));
            setRefCount[set]++;
        }

        public void ReleaseRef(int set)
        {
            Undo?.Note(setRefCount + set, sizeof(int));
            var count = --setRefCount[set];
            if (count == 0)
            {
                FreeSet(set);
            }
        }

        void FreeSet(int set)
        {
            var undo = Undo;
            var span = setContentSpan[set];
            var cls = Bits.Tzcnt(span);
            var node = (int*)(contentArena + setContentOffset[set]);
            undo?.Note(node, sizeof(int));
            *node = contentClassHead[cls];
            undo?.Note(contentClassHead + cls, sizeof(int));
            contentClassHead[cls] = setContentOffset[set];
            undo?.Note(setRefCount + set, sizeof(int));
            setRefCount[set] = 0;
            undo?.Note(setComputed + set, sizeof(float));
            setComputed[set] = 0f;
            undo?.Note(setFolded + set, sizeof(sbyte));
            setFolded[set] = 0;
            DeleteFromBuckets(set);
            undo?.Note(setFreedAt + set, sizeof(long));
            setFreedAt[set] = generation;
            undo?.Note(setFreeNext + set, sizeof(int));
            setFreeNext[set] = setFreeHead;
            if (freeWalkPrev < 0 && freeWalkGeneration == generation && freeWalkHorizon == ReclaimHorizon)
            {
                freeWalkPrev = set;
            }

            setFreeHead = set;
            liveSets--;
        }

        int ReclaimablePrev()
        {
            if (freeWalkGeneration != generation || freeWalkHorizon != ReclaimHorizon)
            {
                var prev = -1;
                var node = setFreeHead;
                while (node >= 0 && generation - setFreedAt[node] <= ReclaimHorizon)
                {
                    prev = node;
                    node = setFreeNext[node];
                }

                freeWalkPrev = prev;
                freeWalkGeneration = generation;
                freeWalkHorizon = ReclaimHorizon;
            }

            return freeWalkPrev;
        }

        void DeleteFromBuckets(int set)
        {
            var undo = Undo;
            var hole = (int)(setHash[set] & (ulong)bucketMask);
            while (buckets[hole] != set + 1)
            {
                hole = (hole + 1) & bucketMask;
            }

            while (true)
            {
                var j = (hole + 1) & bucketMask;
                while (buckets[j] != 0)
                {
                    var home = (int)(setHash[buckets[j] - 1] & (ulong)bucketMask);
                    if (((j - home) & bucketMask) >= ((j - hole) & bucketMask))
                    {
                        break;
                    }

                    j = (j + 1) & bucketMask;
                }

                if (buckets[j] == 0)
                {
                    undo?.Note(buckets + hole, sizeof(int));
                    buckets[hole] = 0;
                    return;
                }

                undo?.Note(buckets + hole, sizeof(int));
                buckets[hole] = buckets[j];
                hole = j;
            }
        }

        void GrowBuckets()
        {
            var previousBuckets = bucketMask + 1;
            bucketMask = bucketMask * 2 + 1;
            buckets = GrowArray(UndoArrayId.SetBuckets, buckets, previousBuckets, bucketMask + 1);
            for (var i = 0; i <= bucketMask; i++)
            {
                buckets[i] = 0;
            }

            for (var set = 0; set < setCount; set++)
            {
                if (setFreedAt[set] != 0)
                {
                    continue;
                }

                var slot = (int)(setHash[set] & (ulong)bucketMask);
                while (buckets[slot] != 0)
                {
                    slot = (slot + 1) & bucketMask;
                }

                buckets[slot] = set + 1;
            }
        }

        internal int MemberTotal(int stat)
        {
            var total = 0;
            for (var set = 0; set < setCount; set++)
            {
                if (setStat[set] == stat)
                {
                    total += setRefCount[set];
                }
            }

            return total;
        }

        public StatEntry* Content(int set, out int count)
        {
            count = setContentCount[set];
            return contentArena + setContentOffset[set];
        }

        public int SetStat(int set) => setStat[set];

        public float Computed(int set) => setComputed[set];

        public void SetComputed(int set, float value)
        {
            Undo?.Note(setComputed + set, sizeof(float));
            setComputed[set] = value;
            Undo?.Note(setFolded + set, sizeof(sbyte));
            setFolded[set] = 1;
        }

        public bool Folded(int set) => setFolded[set] != 0;

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            var count = setCount;
            var capacity = setCapacity;
            var mask = bucketMask;
            codec.Value(ref count);
            codec.Value(ref capacity);
            codec.Value(ref mask);
            if (codec.Reading)
            {
                if (count < 0 || capacity < count || mask < 0)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt set table state");
                }

                if (capacity != setCapacity)
                {
                    var previousCapacity = setCapacity;
                    setStat = memory.Realloc(setStat, previousCapacity, capacity);
                    setHash = memory.Realloc(setHash, previousCapacity, capacity);
                    setComputed = memory.Realloc(setComputed, previousCapacity, capacity);
                    setContentOffset = memory.Realloc(setContentOffset, previousCapacity, capacity);
                    setContentCount = memory.Realloc(setContentCount, previousCapacity, capacity);
                    setContentSpan = memory.Realloc(setContentSpan, previousCapacity, capacity);
                    setRefCount = memory.Realloc(setRefCount, previousCapacity, capacity);
                    setFolded = memory.Realloc(setFolded, previousCapacity, capacity);
                    setFreeNext = memory.Realloc(setFreeNext, previousCapacity, capacity);
                    setFreedAt = memory.Realloc(setFreedAt, previousCapacity, capacity);
                    if (capacity > previousCapacity)
                    {
                        Memory.Clear(setFreedAt + previousCapacity, (nuint)(capacity - previousCapacity) * (nuint)sizeof(long));
                    }
                }

                if (mask != bucketMask)
                {
                    var previousBuckets = bucketMask + 1;
                    buckets = memory.Realloc(buckets, previousBuckets, mask + 1);
                    bucketMask = mask;
                }

                setCount = count;
                setCapacity = capacity;
            }

            codec.Bytes(buckets, (long)(bucketMask + 1) * sizeof(int));
            codec.Bytes(setStat, (long)count * sizeof(int));
            codec.Bytes(setHash, (long)count * sizeof(ulong));
            codec.Bytes(setComputed, (long)count * sizeof(float));
            codec.Bytes(setContentOffset, (long)count * sizeof(int));
            codec.Bytes(setContentCount, (long)count * sizeof(int));
            codec.Bytes(setContentSpan, (long)count * sizeof(int));
            codec.Bytes(setRefCount, (long)count * sizeof(int));
            codec.Bytes(setFolded, count);
            codec.Value(ref contentUsed);
            var arenaCapacity = contentCapacity;
            codec.Value(ref arenaCapacity);
            if (codec.Reading && arenaCapacity != contentCapacity)
            {
                contentArena = memory.Realloc(contentArena, contentCapacity, arenaCapacity);
                contentCapacity = arenaCapacity;
            }

            codec.Bytes(contentArena, (long)contentUsed * sizeof(StatEntry));
            codec.Bytes(contentClassHead, (long)ClassCount * sizeof(int));
            codec.Value(ref liveSets);
            codec.Value(ref setFreeHead);
            freeWalkGeneration = -1;
            codec.Value(ref generation);
            codec.Bytes(setFreeNext, (long)count * sizeof(int));
            codec.Bytes(setFreedAt, (long)count * sizeof(long));
            if (codec.Reading && (liveSets < 0 || liveSets > count || setFreeHead < -1 || setFreeHead >= count))
            {
                throw new System.IO.InvalidDataException("sv: corrupt set table state");
            }
        }

        internal void CaptureUndo(ref Board.SetUndoFrame frame)
        {
            frame.SetCount = setCount;
            frame.SetCapacity = setCapacity;
            frame.BucketMask = bucketMask;
            frame.SetFreeHead = setFreeHead;
            frame.LiveSets = liveSets;
            frame.ContentUsed = contentUsed;
            frame.ContentCapacity = contentCapacity;
            frame.Generation = generation;
            frame.Buckets = buckets;
            frame.SetStat = setStat;
            frame.SetHash = setHash;
            frame.SetComputed = setComputed;
            frame.SetContentOffset = setContentOffset;
            frame.SetContentCount = setContentCount;
            frame.SetContentSpan = setContentSpan;
            frame.SetRefCount = setRefCount;
            frame.SetFolded = setFolded;
            frame.SetFreeNext = setFreeNext;
            frame.SetFreedAt = setFreedAt;
            frame.ContentArena = contentArena;
        }

        internal void RestoreUndo(ref Board.SetUndoFrame frame)
        {
            setCount = frame.SetCount;
            setCapacity = frame.SetCapacity;
            bucketMask = frame.BucketMask;
            setFreeHead = frame.SetFreeHead;
            liveSets = frame.LiveSets;
            contentUsed = frame.ContentUsed;
            contentCapacity = frame.ContentCapacity;
            generation = frame.Generation;
            buckets = (int*)frame.Buckets;
            setStat = (int*)frame.SetStat;
            setHash = (ulong*)frame.SetHash;
            setComputed = (float*)frame.SetComputed;
            setContentOffset = (int*)frame.SetContentOffset;
            setContentCount = (int*)frame.SetContentCount;
            setContentSpan = (int*)frame.SetContentSpan;
            setRefCount = (int*)frame.SetRefCount;
            setFolded = (sbyte*)frame.SetFolded;
            setFreeNext = (int*)frame.SetFreeNext;
            setFreedAt = (long*)frame.SetFreedAt;
            contentArena = (StatEntry*)frame.ContentArena;
        }

        internal void* UndoPointerOf(UndoArrayId id)
        {
            return id switch
            {
                UndoArrayId.SetBuckets => buckets,
                UndoArrayId.SetStat => setStat,
                UndoArrayId.SetHash => setHash,
                UndoArrayId.SetComputed => setComputed,
                UndoArrayId.SetContentOffset => setContentOffset,
                UndoArrayId.SetContentCount => setContentCount,
                UndoArrayId.SetContentSpan => setContentSpan,
                UndoArrayId.SetRefCount => setRefCount,
                UndoArrayId.SetFolded => setFolded,
                UndoArrayId.SetFreeNext => setFreeNext,
                UndoArrayId.SetFreedAt => setFreedAt,
                UndoArrayId.ContentArena => contentArena,
                _ => null,
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            memory.Free(buckets, bucketMask + 1);
            memory.Free(setStat, setCapacity);
            memory.Free(setHash, setCapacity);
            memory.Free(setComputed, setCapacity);
            memory.Free(setContentOffset, setCapacity);
            memory.Free(setContentCount, setCapacity);
            memory.Free(setContentSpan, setCapacity);
            memory.Free(setRefCount, setCapacity);
            memory.Free(setFolded, setCapacity);
            memory.Free(setFreeNext, setCapacity);
            memory.Free(setFreedAt, setCapacity);
            memory.Free(contentArena, contentCapacity);
            memory.Free(contentClassHead, ClassCount);
            disposed = true;
        }
    }
}
`````

### sv src/Sv/Modifier.cs

Path: `sv/src/Sv/Modifier.cs`  (1276 bytes)

`````csharp
using System;

namespace Sv
{
    public enum ModifierKind : byte
    {
        Added = 0,
        Additive = 1,
        Multi = 2,
        Cap = 3,
    }

    public readonly struct Modifier
    {
        public ModifierKind Kind { get; }

        public float Value { get; }

        public Modifier(ModifierKind kind, float value)
        {
            if (kind != ModifierKind.Added && kind != ModifierKind.Additive && kind != ModifierKind.Multi && kind != ModifierKind.Cap)
            {
                throw new ArgumentException($"sv: {kind} is not a modifier kind");
            }

            if (!float.IsFinite(value))
            {
                throw new ArgumentException("sv: modifier value must be finite");
            }

            Kind = kind;
            Value = value;
        }
    }

    /// <summary>
    /// A live modifier's identity: the full 64-bit insertion id — generation in the high bits,
    /// monotone counter in the low 40. Unique for the board's lifetime across state loads and
    /// keyframe folds; sort order is insertion order (clause 3).
    /// </summary>
    public readonly struct ModifierHandle
    {
        public long Id { get; }

        public ModifierHandle(long id)
        {
            Id = id;
        }
    }
}
`````

### sv src/Sv/StatOp.cs

Path: `sv/src/Sv/StatOp.cs`  (6822 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public enum StatOpKind : byte
    {
        RemoveSource = 0,
        Add = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct StatOp
    {
        public readonly long ExpiresAt;
        public readonly int Slot;
        public readonly int Source;
        public readonly uint ValueBits;
        public readonly ushort Stat;
        public readonly ModifierKind Kind;
        public readonly StatOpKind Op;

        public StatOp(StatOpKind op, int slot, int source, ushort stat, ModifierKind kind, uint valueBits, long expiresAt)
        {
            Op = op;
            Slot = slot;
            Source = source;
            Stat = stat;
            Kind = kind;
            ValueBits = valueBits;
            ExpiresAt = expiresAt;
        }

        public static unsafe StatOp Add(int slot, StatId stat, ModifierKind kind, float value, int source, long expiresAt = long.MaxValue)
        {
            if ((uint)stat.Index > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(stat), $"sv: stat {stat.Index} does not fit a StatOp");
            }

            return new StatOp(StatOpKind.Add, slot, source, (ushort)stat.Index, kind, *(uint*)&value, expiresAt);
        }

        public static StatOp RemoveSource(int slot, int source) => new StatOp(StatOpKind.RemoveSource, slot, source, 0, ModifierKind.Added, 0u, 0L);
    }

    public static unsafe class StatOps
    {
        const int SmallRun = 24;

        public static void Canonicalize(Span<StatOp> ops, Span<StatOp> scratch)
        {
            var count = ops.Length;
            if (count < 2)
            {
                return;
            }

            if (scratch.Length < count)
            {
                throw new ArgumentException($"sv: scratch of {scratch.Length} ops cannot hold {count}", nameof(scratch));
            }

            fixed (StatOp* opsPtr = ops)
            fixed (StatOp* scratchPtr = scratch)
            {
                SortBySlot(opsPtr, scratchPtr, count);
                var at = 0;
                while (at < count)
                {
                    var slot = opsPtr[at].Slot;
                    var end = at + 1;
                    while (end < count && opsPtr[end].Slot == slot)
                    {
                        end++;
                    }

                    var length = end - at;
                    if (length <= SmallRun)
                    {
                        InsertionSort(opsPtr + at, length);
                    }
                    else
                    {
                        HeapSort(opsPtr + at, length);
                    }

                    at = end;
                }
            }
        }

        static void SortBySlot(StatOp* ops, StatOp* scratch, int count)
        {
            var counts = stackalloc int[256];
            var source = ops;
            var target = scratch;
            for (var pass = 0; pass < 4; pass++)
            {
                var shift = pass * 8;
                for (var b = 0; b < 256; b++)
                {
                    counts[b] = 0;
                }

                for (var i = 0; i < count; i++)
                {
                    counts[(int)((SlotKey(source[i].Slot) >> shift) & 255u)]++;
                }

                if (counts[(int)((SlotKey(source[0].Slot) >> shift) & 255u)] == count)
                {
                    continue;
                }

                var total = 0;
                for (var b = 0; b < 256; b++)
                {
                    var size = counts[b];
                    counts[b] = total;
                    total += size;
                }

                for (var i = 0; i < count; i++)
                {
                    var op = source[i];
                    target[counts[(int)((SlotKey(op.Slot) >> shift) & 255u)]++] = op;
                }

                var swap = source;
                source = target;
                target = swap;
            }

            if (source != ops)
            {
                Buffer.MemoryCopy(source, ops, (long)count * sizeof(StatOp), (long)count * sizeof(StatOp));
            }
        }

        static uint SlotKey(int slot) => (uint)slot ^ 0x80000000u;

        internal static int Compare(in StatOp a, in StatOp b)
        {
            if (a.Slot != b.Slot)
            {
                return a.Slot < b.Slot ? -1 : 1;
            }

            if (a.Op != b.Op)
            {
                return a.Op < b.Op ? -1 : 1;
            }

            if (a.Source != b.Source)
            {
                return a.Source < b.Source ? -1 : 1;
            }

            if (a.Stat != b.Stat)
            {
                return a.Stat < b.Stat ? -1 : 1;
            }

            if (a.Kind != b.Kind)
            {
                return a.Kind < b.Kind ? -1 : 1;
            }

            if (a.ValueBits != b.ValueBits)
            {
                return a.ValueBits < b.ValueBits ? -1 : 1;
            }

            if (a.ExpiresAt != b.ExpiresAt)
            {
                return a.ExpiresAt < b.ExpiresAt ? -1 : 1;
            }

            return 0;
        }

        static void InsertionSort(StatOp* ops, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var item = ops[i];
                var j = i - 1;
                while (j >= 0 && Compare(ops[j], item) > 0)
                {
                    ops[j + 1] = ops[j];
                    j--;
                }

                ops[j + 1] = item;
            }
        }

        static void HeapSort(StatOp* ops, int count)
        {
            for (var start = count / 2 - 1; start >= 0; start--)
            {
                SiftDown(ops, start, count);
            }

            for (var end = count - 1; end > 0; end--)
            {
                var top = ops[0];
                ops[0] = ops[end];
                ops[end] = top;
                SiftDown(ops, 0, end);
            }
        }

        static void SiftDown(StatOp* ops, int root, int count)
        {
            while (true)
            {
                var child = root * 2 + 1;
                if (child >= count)
                {
                    return;
                }

                if (child + 1 < count && Compare(ops[child], ops[child + 1]) < 0)
                {
                    child++;
                }

                if (Compare(ops[root], ops[child]) >= 0)
                {
                    return;
                }

                var swap = ops[root];
                ops[root] = ops[child];
                ops[child] = swap;
                root = child;
            }
        }
    }
}
`````

### sv src/Sv/Link.cs

Path: `sv/src/Sv/Link.cs`  (6649 bytes)

`````csharp
using System;

namespace Sv
{
    public enum LinkMode : byte
    {
        Read = 0,
        Check = 1,
        Accumulate = 2,
        Delta = 3,
        Modifier = 4,
        Event = 5,
    }

    public enum LinkScope : byte
    {
        Self = 0,
        Remote = 1,
    }

    public enum ColumnRole : byte
    {
        StatOutput = 0,
        Intrinsic = 1,
        Delta = 2,
        Inbox = 3,
        Effect = 4,
        Signal = 5,
        Condition = 6,
        Sense = 7,
    }

    public enum LinkFault : byte
    {
        None = 0,
        UnknownColumn = 1,
        ReadOfAccumulate = 2,
        WriteOfStore = 3,
        RemoteFloat = 4,
        SecondConsumer = 5,
        RemoteWithoutInbox = 6,
    }

    public readonly struct ColumnDef
    {
        public readonly ulong Name;
        public readonly ushort Schema;
        public readonly ushort Consumer;
        public readonly ColumnRole Role;

        public ColumnDef(ulong name, ColumnRole role, ushort schema, ushort consumer)
        {
            Name = name;
            Role = role;
            Schema = schema;
            Consumer = consumer;
        }
    }

    public readonly struct Link
    {
        public readonly ushort Kind;
        public readonly ushort Column;
        public readonly LinkMode Mode;
        public readonly LinkScope Scope;

        public Link(ushort kind, ushort column, LinkMode mode, LinkScope scope = LinkScope.Self)
        {
            Kind = kind;
            Column = column;
            Mode = mode;
            Scope = scope;
        }
    }

    public readonly struct StatWrite
    {
        public readonly int Slot;
        public readonly uint Bits;
        public readonly ushort Stat;

        public StatWrite(int slot, ushort stat, uint bits)
        {
            Slot = slot;
            Stat = stat;
            Bits = bits;
        }
    }

    public static class Links
    {
        public static bool IsAccumulate(ColumnRole role) => role == ColumnRole.Delta || role == ColumnRole.Inbox || role == ColumnRole.Effect || role == ColumnRole.Signal;

        public static bool IsStore(ColumnRole role) => !IsAccumulate(role);

        public static LinkFault Validate(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (!IsAccumulate(columns[i].Role))
                {
                    continue;
                }

                for (var j = i + 1; j < columns.Length; j++)
                {
                    if (columns[j].Name == columns[i].Name && IsAccumulate(columns[j].Role))
                    {
                        return LinkFault.SecondConsumer;
                    }

                    if (columns[i].Role == ColumnRole.Inbox && columns[j].Role == ColumnRole.Inbox && columns[i].Consumer == columns[j].Consumer)
                    {
                        return LinkFault.SecondConsumer;
                    }
                }
            }

            for (var i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (link.Column >= columns.Length || (byte)link.Mode > (byte)LinkMode.Event || (byte)link.Scope > (byte)LinkScope.Remote)
                {
                    return LinkFault.UnknownColumn;
                }

                var column = columns[link.Column];
                var fault = Check(link, column, columns);
                if (fault != LinkFault.None)
                {
                    return fault;
                }
            }

            return LinkFault.None;
        }

        static LinkFault Check(in Link link, in ColumnDef column, ReadOnlySpan<ColumnDef> columns)
        {
            switch (link.Mode)
            {
                case LinkMode.Read:
                case LinkMode.Check:
                    return IsAccumulate(column.Role) ? LinkFault.ReadOfAccumulate : LinkFault.None;
                case LinkMode.Modifier:
                    return column.Role == ColumnRole.StatOutput ? LinkFault.None : LinkFault.WriteOfStore;
                case LinkMode.Event:
                    if (column.Role != ColumnRole.Signal && column.Role != ColumnRole.Condition)
                    {
                        return LinkFault.WriteOfStore;
                    }

                    return link.Scope == LinkScope.Remote ? LinkFault.RemoteFloat : LinkFault.None;
                default:
                    if (IsStore(column.Role))
                    {
                        return LinkFault.WriteOfStore;
                    }

                    if (link.Scope == LinkScope.Self)
                    {
                        return LinkFault.None;
                    }

                    if (column.Role == ColumnRole.Inbox)
                    {
                        return LinkFault.None;
                    }

                    if (column.Role != ColumnRole.Delta)
                    {
                        return LinkFault.RemoteFloat;
                    }

                    for (var i = 0; i < columns.Length; i++)
                    {
                        if (columns[i].Role == ColumnRole.Inbox && columns[i].Consumer == column.Consumer)
                        {
                            return LinkFault.None;
                        }
                    }

                    return LinkFault.RemoteWithoutInbox;
            }
        }

        public static ulong Hash(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns)
        {
            var hash = 0x6C696E6B73763231ul;
            hash = Mix(hash, (ulong)columns.Length);
            for (var i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                hash = Mix(hash, c.Name);
                hash = Mix(hash, ((ulong)c.Schema << 24) | ((ulong)c.Consumer << 8) | (byte)c.Role);
            }

            hash = Mix(hash, (ulong)links.Length);
            for (var i = 0; i < links.Length; i++)
            {
                var l = links[i];
                hash = Mix(hash, ((ulong)l.Kind << 32) | ((ulong)l.Column << 16) | ((ulong)(byte)l.Mode << 8) | (byte)l.Scope);
            }

            return hash;
        }

        static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                var key = hash ^ (value * 0x9E3779B97F4A7C15ul);
                key ^= key >> 33;
                key *= 0xFF51AFD7ED558CCDul;
                key ^= key >> 33;
                key *= 0xC4CEB9FE1A85EC53ul;
                return key ^ (key >> 33);
            }
        }
    }
}
`````

### sv src/Sv/Fold.cs

Path: `sv/src/Sv/Fold.cs`  (2596 bytes)

`````csharp
using System.Runtime.CompilerServices;

namespace Sv
{
    public static class IntrinsicLaw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Applied(int before, int after) => Saturate((long)after - before);
    }

    public unsafe struct FoldWord
    {
        public int* Value;
        public int* Delta;
        public float* Floor;
        public float* Ceiling;
        public int FloorConstant;
        public int CeilingConstant;
    }

    public static unsafe class Fold
    {
        public static ulong Word(in FoldWord word, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var floor = word.Floor == null ? word.FloorConstant : Clamp.BoundOf(word.Floor[row]);
                var ceiling = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var before = word.Value[row];
                var after = IntrinsicLaw.Step(before, word.Delta[row], floor, ceiling);
                word.Value[row] = after;
                word.Delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

        public static ulong Word(in FoldWord word, long* inbox, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var floor = word.Floor == null ? word.FloorConstant : Clamp.BoundOf(word.Floor[row]);
                var ceiling = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var before = word.Value[row];
                var delta = inbox == null ? word.Delta[row] : Inbox.Take(inbox, row, word.Delta[row]);
                var after = IntrinsicLaw.Step(before, delta, floor, ceiling);
                word.Value[row] = after;
                word.Delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }
    }
}
`````

### sv src/Sv/Kernel.cs

Path: `sv/src/Sv/Kernel.cs`  (8216 bytes)

`````csharp
using System;

namespace Sv
{
/// <summary>
/// The chunk verbs as static kernels: the caller supplies the capacity the guards validate
/// against, so a context that holds only pointers, masks and an unmanaged <see cref="Clamp"/>
/// runs the whole surface — no <see cref="Board"/> instance, nothing managed on the path.
/// <see cref="Run(Column{int}, Window, Delta, Bounds, Column{int})"/> is the one verb over the
/// vocabulary; the clause-5 <c>Run*</c> names survive as aliases.
/// </summary>
public static unsafe partial class Kernel
{
    /// <summary>
    /// The one kernel verb: adds <paramref name="delta"/> to every row of <paramref name="window"/> inside
    /// <paramref name="value"/>, clamps with <paramref name="bounds"/>, and writes the applied delta to
    /// <paramref name="applied"/> (indexed by absolute slot — masked windows write their full span,
    /// disabled rows get zero; an empty applied column skips the report).
    /// </summary>
    public static void Run(Column<int> value, Window window, Delta delta, Bounds bounds, Column<int> applied = default)
    {
        var clamp = bounds.Clamp;
        var appliedPtr = applied.Pointer;
        if (window.Words == null)
        {
            Guard.ChunkRange(window.Start, window.Count, value.Length);
            if (appliedPtr != null && applied.Length < (long)window.Start + window.Count)
            {
                throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + window.Count}");
            }

            if (delta.PerRow == null)
            {
                IntrinsicsChunk(value.Pointer, delta.Uniform, clamp, window.Start, window.Count, value.Length, appliedPtr);
            }
            else
            {
                IntrinsicsChunk(value.Pointer, delta.PerRow, clamp, window.Start, window.Count, value.Length, appliedPtr);
            }

            return;
        }

        Guard.EnabledRange(window.Start, window.WordCount, value.Length);
        if (appliedPtr != null && applied.Length < (long)window.Start + (long)window.WordCount * 64)
        {
            throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + (long)window.WordCount * 64}");
        }

        if (delta.PerRow == null)
        {
            EnabledChunk(value.Pointer, delta.Uniform, clamp, window.Words, window.WordCount, window.Start, value.Length, appliedPtr);
        }
        else
        {
            EnabledChunk(value.Pointer, delta.PerRow, clamp, window.Words, window.WordCount, window.Start, value.Length, appliedPtr);
        }
    }

    public static void IntrinsicsChunk(int* value, int delta, in Clamp clamp, int start, int count, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.ChunkRange(start, count, capacity);
        Simd.Chunk(value, null, delta, clamp, start, count, applied);
    }

    public static void IntrinsicsChunk(int* value, int* delta, in Clamp clamp, int start, int count, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.ChunkRange(start, count, capacity);
        Simd.Chunk(value, delta, 0, clamp, start, count, applied);
    }

    /// <param name="applied">Delta column indexed by absolute slot; written for all 64 rows of the word — disabled rows get zero.</param>
    /// <param name="validBits">Rows of the word the columns are sized for; set mask bits past them are rejected. Full 64 keeps the word-complete contract.</param>
    public static void EnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null, int validBits = 64)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.EnabledRange(offset, capacity);
        Guard.EnabledTail(enabled, validBits);
        Simd.EnabledWord(value, null, delta, clamp, enabled, offset, applied, validBits);
    }

    /// <param name="applied">Delta column indexed by absolute slot; written for all 64 rows of the word — disabled rows get zero.</param>
    /// <param name="validBits">Rows of the word the columns are sized for; set mask bits past them are rejected. Full 64 keeps the word-complete contract.</param>
    public static void EnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null, int validBits = 64)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.EnabledRange(offset, capacity);
        Guard.EnabledTail(enabled, validBits);
        Simd.EnabledWord(value, delta, 0, clamp, enabled, offset, applied, validBits);
    }

    /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
    /// <param name="delta">Uniform delta added to every enabled row.</param>
    /// <param name="clamp">Bounds applied after the add.</param>
    /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
    /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
    /// <param name="offset">Absolute slot index of the window's first row.</param>
    /// <param name="capacity">Capacity the range guards validate against.</param>
    /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
    public static void EnabledChunk(int* value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (words == null)
        {
            throw new ArgumentNullException(nameof(words));
        }

        Guard.EnabledRange(offset, wordCount, capacity);
        for (var w = 0; w < wordCount; w++)
        {
            Simd.EnabledWord(value, null, delta, clamp, words[w], offset + w * 64, applied, 64);
        }
    }

    /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
    /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
    /// <param name="clamp">Bounds applied after the add.</param>
    /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
    /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
    /// <param name="offset">Absolute slot index of the window's first row.</param>
    /// <param name="capacity">Capacity the range guards validate against.</param>
    /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
    public static void EnabledChunk(int* value, int* delta, in Clamp clamp, ulong* words, int wordCount, int offset, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (words == null)
        {
            throw new ArgumentNullException(nameof(words));
        }

        Guard.EnabledRange(offset, wordCount, capacity);
        for (var w = 0; w < wordCount; w++)
        {
            Simd.EnabledWord(value, delta, 0, clamp, words[w], offset + w * 64, applied, 64);
        }
    }
}
}
`````

### sv src/Sv/Kernel.Fold.cs

Path: `sv/src/Sv/Kernel.Fold.cs`  (14038 bytes)

`````csharp
using System;
using System.Runtime.CompilerServices;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv
{
    internal enum FoldWidth : byte
    {
        Auto = 0,
        Scalar = 1,
        Width128 = 2,
        Width256 = 3,
    }

    internal interface IFoldBound
    {
        int Floor(int slot);

        int Ceiling(int slot);
#if NET
        Vector128<int> Floor128(int slot);

        Vector128<int> Ceiling128(int slot);

        Vector256<int> Floor256(int slot);

        Vector256<int> Ceiling256(int slot);
#endif
    }

    internal readonly struct ConstantFold : IFoldBound
    {
        readonly int floor;

        readonly int ceiling;

        public ConstantFold(int floor, int ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceiling;
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Vector128.Create(ceiling);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Vector256.Create(ceiling);
#endif
    }

    internal readonly unsafe struct RowsFold : IFoldBound
    {
        readonly int* floor;

        readonly int* ceiling;

        public RowsFold(int* floor, int* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor[slot];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceiling[slot];
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Load(floor + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Vector128.Load(ceiling + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Load(floor + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Vector256.Load(ceiling + slot);
#endif
    }

    internal readonly unsafe struct StatsFold : IFoldBound
    {
        readonly float* floor;

        readonly float* ceiling;

        public StatsFold(float* floor, float* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => Clamp.BoundOf(floor[slot]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => Clamp.BoundOf(ceiling[slot]);
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Simd.BoundOf128(Vector128.Load(floor + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Simd.BoundOf128(Vector128.Load(ceiling + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Simd.BoundOf256(Vector256.Load(floor + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Simd.BoundOf256(Vector256.Load(ceiling + slot));
#endif
    }

    internal readonly unsafe struct StatCeilingFold : IFoldBound
    {
        readonly int floor;

        readonly float* ceiling;

        public StatCeilingFold(int floor, float* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => Clamp.BoundOf(ceiling[slot]);
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Simd.BoundOf128(Vector128.Load(ceiling + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Simd.BoundOf256(Vector256.Load(ceiling + slot));
#endif
    }

    internal readonly unsafe struct AnyFold : IFoldBound
    {
        readonly int* floorRows;

        readonly int* ceilingRows;

        readonly float* floorStats;

        readonly float* ceilingStats;

        public AnyFold(int* floorRows, int* ceilingRows, float* floorStats, float* ceilingStats)
        {
            this.floorRows = floorRows;
            this.ceilingRows = ceilingRows;
            this.floorStats = floorStats;
            this.ceilingStats = ceilingStats;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floorStats != null ? Clamp.BoundOf(floorStats[slot]) : floorRows != null ? floorRows[slot] : int.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceilingStats != null ? Clamp.BoundOf(ceilingStats[slot]) : ceilingRows != null ? ceilingRows[slot] : int.MaxValue;
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => floorStats != null ? Simd.BoundOf128(Vector128.Load(floorStats + slot)) : floorRows != null ? Vector128.Load(floorRows + slot) : Vector128.Create(int.MinValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => ceilingStats != null ? Simd.BoundOf128(Vector128.Load(ceilingStats + slot)) : ceilingRows != null ? Vector128.Load(ceilingRows + slot) : Vector128.Create(int.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => floorStats != null ? Simd.BoundOf256(Vector256.Load(floorStats + slot)) : floorRows != null ? Vector256.Load(floorRows + slot) : Vector256.Create(int.MinValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => ceilingStats != null ? Simd.BoundOf256(Vector256.Load(ceilingStats + slot)) : ceilingRows != null ? Vector256.Load(ceilingRows + slot) : Vector256.Create(int.MaxValue);
#endif
    }

    internal readonly unsafe struct FoldSpan
    {
        public readonly int* Value;
        public readonly int* Delta;
        public readonly long* Inbox;
        public readonly ulong* Words;
        public readonly ulong* Changed;
        public readonly int Start;
        public readonly int Count;
        public readonly int WordCount;

        public FoldSpan(int* value, int* delta, long* inbox, ulong* words, ulong* changed, int start, int count, int wordCount)
        {
            Value = value;
            Delta = delta;
            Inbox = inbox;
            Words = words;
            Changed = changed;
            Start = start;
            Count = count;
            WordCount = wordCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Rows(int w) => Words != null ? 64 : Math.Min(64, Count - (w << 6));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Mask(int w, int rows) => Words != null ? Words[w] : rows == 64 ? ulong.MaxValue : (1ul << rows) - 1;
    }

    public static unsafe partial class Kernel
    {
        public static void Fold(Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed = default)
        {
            FoldWith(FoldWidth.Auto, value, window, delta, bounds, changed, default);
        }

        public static void Fold(Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed, Column<long> inbox)
        {
            FoldWith(FoldWidth.Auto, value, window, delta, bounds, changed, inbox);
        }

        internal static void FoldWith(FoldWidth width, Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed, Column<long> inbox)
        {
            var masked = window.Words != null;
            var wordCount = masked ? window.WordCount : (window.Count + 63) >> 6;
            var reach = masked ? (long)window.Start + (long)window.WordCount * 64 : (long)window.Start + window.Count;
            if (masked)
            {
                Guard.EnabledRange(window.Start, window.WordCount, value.Length);
            }
            else
            {
                Guard.ChunkRange(window.Start, window.Count, value.Length);
            }

            if (delta.Length < reach)
            {
                throw new ArgumentException($"sv: delta column of {delta.Length} rows does not cover the window's {reach}");
            }

            if (inbox.Pointer != null && inbox.Length < reach)
            {
                throw new ArgumentException($"sv: inbox of {inbox.Length} cells does not cover the window's {reach}");
            }

            if (changed.Pointer != null && changed.Length < wordCount)
            {
                throw new ArgumentException($"sv: changed column of {changed.Length} words does not cover the window's {wordCount}");
            }

            if (wordCount == 0)
            {
                return;
            }

            var clamp = bounds.Clamp;
            clamp.CheckStatBounds();
            var span = new FoldSpan(value.Pointer, delta.Pointer, inbox.Pointer, window.Words, changed.Pointer, window.Start, window.Count, wordCount);
            var resolved = Resolve(width);
            switch (clamp.Kind)
            {
                case ClampKind.Constant:
                    FoldBound(resolved, span, new ConstantFold(clamp.Min, clamp.Max));
                    return;
                case ClampKind.Columns:
                    if (clamp.MinColumn != null && clamp.MaxColumn != null)
                    {
                        FoldBound(resolved, span, new RowsFold(clamp.MinColumn, clamp.MaxColumn));
                    }
                    else
                    {
                        FoldBound(resolved, span, new AnyFold(clamp.MinColumn, clamp.MaxColumn, null, null));
                    }

                    return;
                case ClampKind.StatBounds:
                    if (clamp.MinStatColumn != null && clamp.MaxStatColumn != null)
                    {
                        FoldBound(resolved, span, new StatsFold(clamp.MinStatColumn, clamp.MaxStatColumn));
                    }
                    else if (clamp.MaxStatColumn != null)
                    {
                        FoldBound(resolved, span, new StatCeilingFold(int.MinValue, clamp.MaxStatColumn));
                    }
                    else
                    {
                        FoldBound(resolved, span, new AnyFold(null, null, clamp.MinStatColumn, null));
                    }

                    return;
                default:
                    FoldBound(resolved, span, new ConstantFold(int.MinValue, int.MaxValue));
                    return;
            }
        }

        static FoldWidth Resolve(FoldWidth width)
        {
#if NET
            if (width == FoldWidth.Auto)
            {
                return Simd.Has256 ? FoldWidth.Width256 : Simd.Has128 ? FoldWidth.Width128 : FoldWidth.Scalar;
            }

            return width;
#else
            return FoldWidth.Scalar;
#endif
        }

        static void FoldBound<TB>(FoldWidth width, in FoldSpan span, TB bound)
            where TB : struct, IFoldBound
        {
#if NET
            if (width == FoldWidth.Width256)
            {
                Simd.FoldWindow256(span, bound);
                return;
            }

            if (width == FoldWidth.Width128)
            {
                Simd.FoldWindow128(span, bound);
                return;
            }
#endif
            var value = span.Value;
            var delta = span.Delta;
            var inbox = span.Inbox;
            var changed = span.Changed;
            for (var w = 0; w < span.WordCount; w++)
            {
                var rows = span.Rows(w);
                var bits = FoldScalar(value, delta, inbox, bound, span.Start + (w << 6), rows, span.Mask(w, rows));
                if (changed != null)
                {
                    changed[w] = bits;
                }
            }
        }

        internal static ulong FoldScalar<TB>(int* value, int* delta, long* inbox, TB bound, int start, int rows, ulong mask)
            where TB : struct, IFoldBound
        {
            var changed = 0ul;
            var live = rows >= 64 ? mask : mask & ((1ul << rows) - 1);
            while (live != 0)
            {
                var bit = CellDigest.Tzcnt(live);
                live &= live - 1;
                var slot = start + bit;
                var before = value[slot];
                var moved = inbox == null ? delta[slot] : Inbox.Take(inbox, slot, delta[slot]);
                var after = IntrinsicLaw.Step(before, moved, bound.Floor(slot), bound.Ceiling(slot));
                value[slot] = after;
                delta[slot] = 0;
                changed |= (after != before ? 1ul : 0ul) << bit;
            }

            return changed;
        }
    }
}
`````

### sv src/Sv/Inbox.cs

Path: `sv/src/Sv/Inbox.cs`  (1858 bytes)

`````csharp
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sv
{
    public static unsafe class Inbox
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Send(long* inbox, int targetSlot, int amount) => Interlocked.Add(ref inbox[targetSlot], amount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Take(long* inbox, int slot, int local)
        {
            var total = IntrinsicLaw.Saturate(local + inbox[slot]);
            inbox[slot] = 0;
            return total;
        }
    }

    public sealed unsafe class InboxBuffer : IDisposable
    {
        readonly NativeArena arena = new NativeArena();

        long* cells;

        public InboxBuffer(int length)
        {
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"sv: inbox of {length} cells is empty");
            }

            Length = length;
            cells = arena.AllocAlignedZeroed<long>(length);
        }

        public int Length { get; }

        public long* Pointer
        {
            get
            {
                if (cells == null)
                {
                    throw new ObjectDisposedException(nameof(InboxBuffer));
                }

                return cells;
            }
        }

        public Column<long> Column => new Column<long>(Pointer, Length);

        public void Clear()
        {
            var at = Pointer;
            for (var i = 0; i < Length; i++)
            {
                at[i] = 0;
            }
        }

        public void Dispose()
        {
            if (cells == null)
            {
                return;
            }

            arena.FreeAligned(cells, Length);
            cells = null;
            arena.Dispose();
        }
    }
}
`````

### sv src/Sv/InboxLanes.cs

Path: `sv/src/Sv/InboxLanes.cs`  (5175 bytes)

`````csharp
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sv
{
    public unsafe struct InboxLane
    {
        long* inbox;
        int* slots;
        long* sums;
        int* used;
        int usedCount;
        int mask;
        int shift;
        int sent;
        int evicted;
        int bypass;

        public const int ProbeSends = 4096;

        public const int BypassSends = 61440;

        internal InboxLane(long* inbox, int* slots, long* sums, int* used, int cells)
        {
            this.inbox = inbox;
            this.slots = slots;
            this.sums = sums;
            this.used = used;
            usedCount = 0;
            sent = 0;
            evicted = 0;
            bypass = 0;
            mask = cells - 1;
            shift = 32 - Bits.Tzcnt((uint)cells);
            for (var i = 0; i < cells; i++)
            {
                slots[i] = -1;
            }
        }

        public int Pending => usedCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send(int targetSlot, int amount)
        {
            if (bypass > 0)
            {
                bypass--;
                Interlocked.Add(ref inbox[targetSlot], amount);
                return;
            }

            if (++sent == ProbeSends)
            {
                bypass = evicted > ProbeSends / 2 ? BypassSends : 0;
                sent = 0;
                evicted = 0;
            }

            var at = (int)(((uint)targetSlot * 0x9E3779B1u) >> shift) & mask;
            var held = slots[at];
            if (held == targetSlot)
            {
                sums[at] += amount;
                return;
            }

            if (held < 0)
            {
                slots[at] = targetSlot;
                sums[at] = amount;
                used[usedCount++] = at;
                return;
            }

            evicted++;
            Interlocked.Add(ref inbox[held], sums[at]);
            slots[at] = targetSlot;
            sums[at] = amount;
        }

        public bool Bypassing => bypass > 0;

        public void Flush()
        {
            for (var i = 0; i < usedCount; i++)
            {
                var at = used[i];
                Interlocked.Add(ref inbox[slots[at]], sums[at]);
                slots[at] = -1;
            }

            usedCount = 0;
            sent = 0;
            evicted = 0;
            bypass = 0;
        }
    }

    public sealed unsafe class InboxLanes : IDisposable
    {
        public const int DefaultCells = 1024;

        readonly NativeArena arena = new NativeArena();

        InboxLane* lanes;

        readonly int* slots;

        readonly long* sums;

        readonly int* used;

        readonly int stride;

        public InboxLanes(InboxBuffer inbox, int workers, int cells = DefaultCells)
        {
            if (inbox == null)
            {
                throw new ArgumentNullException(nameof(inbox));
            }

            if (workers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workers), $"sv: inbox lanes need at least one worker, got {workers}");
            }

            if (cells < 16 || cells > 1 << 20 || (cells & (cells - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cells), $"sv: inbox lane cells must be a power of two in [16, 1048576], got {cells}");
            }

            Workers = workers;
            Cells = cells;
            stride = cells + 16;
            var total = checked(workers * stride);
            lanes = arena.AllocAlignedZeroed<InboxLane>(workers * 2);
            slots = arena.AllocAlignedZeroed<int>(total);
            sums = arena.AllocAlignedZeroed<long>(total);
            used = arena.AllocAlignedZeroed<int>(total);
            var cellsPointer = inbox.Pointer;
            for (var w = 0; w < workers; w++)
            {
                lanes[w * 2] = new InboxLane(cellsPointer, slots + (nint)w * stride, sums + (nint)w * stride, used + (nint)w * stride, cells);
            }
        }

        public int Workers { get; }

        public int Cells { get; }

        public InboxLane* Lane(int worker)
        {
            if (lanes == null)
            {
                throw new ObjectDisposedException(nameof(InboxLanes));
            }

            if ((uint)worker >= (uint)Workers)
            {
                throw new ArgumentOutOfRangeException(nameof(worker), $"sv: worker {worker} outside [0, {Workers})");
            }

            return lanes + worker * 2;
        }

        public void FlushAll()
        {
            for (var w = 0; w < Workers; w++)
            {
                Lane(w)->Flush();
            }
        }

        public void Dispose()
        {
            if (lanes == null)
            {
                return;
            }

            var total = Workers * stride;
            arena.FreeAligned(lanes, Workers * 2);
            arena.FreeAligned(slots, total);
            arena.FreeAligned(sums, total);
            arena.FreeAligned(used, total);
            lanes = null;
            arena.Dispose();
        }
    }
}
`````

### sv src/Sv/Clamp.cs

Path: `sv/src/Sv/Clamp.cs`  (7364 bytes)

`````csharp
using System;

namespace Sv
{
    public enum ClampKind : byte
    {
        None = 0,
        Constant = 1,
        Columns = 2,
        StatBounds = 3,
    }

    public readonly unsafe struct Clamp
    {
        public ClampKind Kind { get; }

        public int Min { get; }

        public int Max { get; }

        public int* MinColumn { get; }

        public int* MaxColumn { get; }

        public float* MinStatColumn { get; }

        public float* MaxStatColumn { get; }

        readonly int* minGenerationSlot;

        readonly int* maxGenerationSlot;

        readonly int minGeneration;

        readonly int maxGeneration;

        Clamp(ClampKind kind, int min, int max, int* minColumn, int* maxColumn, float* minStat = null, float* maxStat = null, int* minGenerationSlot = null, int* maxGenerationSlot = null, int minGeneration = -1, int maxGeneration = -1)
        {
            Kind = kind;
            Min = min;
            Max = max;
            MinColumn = minColumn;
            MaxColumn = maxColumn;
            MinStatColumn = minStat;
            MaxStatColumn = maxStat;
            this.minGenerationSlot = minGenerationSlot;
            this.maxGenerationSlot = maxGenerationSlot;
            this.minGeneration = minGeneration;
            this.maxGeneration = maxGeneration;
        }

        public static Clamp Constant(int min, int max)
        {
            if (min > max)
            {
                throw new ArgumentException($"sv: clamp constant min {min} > max {max}");
            }

            return new Clamp(ClampKind.Constant, min, max, null, null);
        }

        public static Clamp Columns(int* min, int* max)
        {
            if (min == null && max == null)
            {
                throw new ArgumentException("sv: clamp columns require at least one bound");
            }

            return new Clamp(ClampKind.Columns, 0, 0, min, max);
        }

        public static Clamp StatColumns(float* min, float* max)
        {
            if (min == null && max == null)
            {
                throw new ArgumentException("sv: clamp stat columns require at least one bound");
            }

            return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, max);
        }

        public static Clamp Stat(Board board, StatId minStat, StatId maxStat)
        {
            var min = ResolveColumn(board, minStat, out var minSlot, out var minGeneration);
            var max = ResolveColumn(board, maxStat, out var maxSlot, out var maxGeneration);
            return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, max, minSlot, maxSlot, minGeneration, maxGeneration);
        }

        public static Clamp StatMax(Board board, StatId maxStat)
        {
            var max = ResolveColumn(board, maxStat, out var maxSlot, out var maxGeneration);
            return new Clamp(ClampKind.StatBounds, 0, 0, null, null, null, max, null, maxSlot, -1, maxGeneration);
        }

        public static Clamp StatMin(Board board, StatId minStat)
        {
            var min = ResolveColumn(board, minStat, out var minSlot, out var minGeneration);
            return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, null, minSlot, null, minGeneration, -1);
        }

        /// <summary>
        /// Clamps against the stats' sv-owned int mirrors (<see cref="Board.MirrorBounds"/>): the
        /// clause-16 conversion runs once per broadcast instead of once per row per op. Same results
        /// as <see cref="Stat"/> whenever the bound columns are written only by broadcast.
        /// </summary>
        public static Clamp StatMirror(Board board, StatId minStat, StatId maxStat)
        {
            return Columns(board.MirrorBounds(minStat), board.MirrorBounds(maxStat));
        }

        public static Clamp StatMirrorMax(Board board, StatId maxStat)
        {
            return Columns(null, board.MirrorBounds(maxStat));
        }

        public static Clamp StatMirrorMin(Board board, StatId minStat)
        {
            return Columns(board.MirrorBounds(minStat), null);
        }

        static float* ResolveColumn(Board board, StatId stat, out int* generationSlot, out int generation)
        {
            var external = board.OutputColumn(stat);
            if (external != null)
            {
                if (board.OutputStrideAt(stat.Index) != 1)
                {
                    throw new ArgumentException($"sv: Clamp.Stat reads its bound column contiguously; stat {stat.Index} is bound with stride {board.OutputStrideAt(stat.Index)}");
                }

                generationSlot = board.BoundGenerationPtrAt(stat.Index);
                generation = *generationSlot;
                Guard.BindingFresh(generation);
                return external;
            }

            generationSlot = null;
            generation = -1;
            return board.OutputPtr(stat);
        }

        [System.Diagnostics.Conditional("SV_CHECKED")]
        internal void CheckStatBounds()
        {
            if (Kind != ClampKind.StatBounds)
            {
                return;
            }

            if (minGeneration >= 0 && *minGenerationSlot != minGeneration)
            {
                throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
            }

            if (maxGeneration >= 0 && *maxGenerationSlot != maxGeneration)
            {
                throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int Apply(int value, int slot)
        {
            CheckStatBounds();
            switch (Kind)
            {
                case ClampKind.Constant:
                    return value < Min ? Min : value > Max ? Max : value;
                case ClampKind.Columns:
                    {
                        var lo = MinColumn != null ? MinColumn[slot] : int.MinValue;
                        var hi = MaxColumn != null ? MaxColumn[slot] : int.MaxValue;
                        if (lo > hi)
                        {
                            lo = hi;
                        }

                        return value < lo ? lo : value > hi ? hi : value;
                    }
                case ClampKind.StatBounds:
                    {
                        var lo = MinStatColumn != null ? BoundOf(MinStatColumn[slot]) : int.MinValue;
                        var hi = MaxStatColumn != null ? BoundOf(MaxStatColumn[slot]) : int.MaxValue;
                        if (lo > hi)
                        {
                            lo = hi;
                        }

                        return value < lo ? lo : value > hi ? hi : value;
                    }
                default:
                    return value;
            }
        }

        public static int BoundOf(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            if (value >= 2147483648.0f)
            {
                return int.MaxValue;
            }

            if (value < -2147483648.0f)
            {
                return int.MinValue;
            }

            return unchecked((int)value);
        }
    }
}
`````

### sv src/Sv/Bounds.cs

Path: `sv/src/Sv/Bounds.cs`  (1473 bytes)

`````csharp
namespace Sv
{
    /// <summary>
    /// The bounds a kernel run clamps with, as factories over the unmanaged <see cref="Clamp"/>:
    /// <see langword="default"/> is none. <see cref="Rows(int*, int*)"/> takes at least one bound column
    /// indexed by absolute slot; <see cref="Stats(Board, StatId?, StatId?)"/> captures stat bounds at
    /// construction (validated, generation-checked — atom 77's shape).
    /// </summary>
    public readonly unsafe struct Bounds
    {
        public Clamp Clamp { get; }

        Bounds(Clamp clamp)
        {
            Clamp = clamp;
        }

        public static Bounds None => default;

        public static Bounds Constant(int min, int max) => new(Clamp.Constant(min, max));

        public static Bounds Rows(int* min, int* max) => new(Clamp.Columns(min, max));

        public static Bounds StatRows(float* min, float* max) => new(Clamp.StatColumns(min, max));

        public static Bounds Stats(Board board, StatId? minStat, StatId? maxStat)
        {
            if (minStat.HasValue && maxStat.HasValue)
            {
                return new(Clamp.Stat(board, minStat.Value, maxStat.Value));
            }

            if (maxStat.HasValue)
            {
                return new(Clamp.StatMax(board, maxStat.Value));
            }

            if (minStat.HasValue)
            {
                return new(Clamp.StatMin(board, minStat.Value));
            }

            return default;
        }
    }
}
`````

### sv src/Sv/Condition.cs

Path: `sv/src/Sv/Condition.cs`  (610 bytes)

`````csharp
using System.Runtime.CompilerServices;

namespace Sv
{
    public static class Condition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Below(int value, int whole, int numerator, int denominator) => (long)value * denominator < (long)whole * numerator;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Next(ulong state, ulong enter, ulong stay) => (~state & enter) | (state & stay);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Edges(ulong state, ulong next) => state ^ next;
    }
}
`````

### sv src/Sv/CellDigest.cs

Path: `sv/src/Sv/CellDigest.cs`  (2731 bytes)

`````csharp
using System;
using System.Runtime.CompilerServices;

namespace Sv
{
    public static unsafe class CellDigest
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Key(int column, int slot, uint bits)
        {
            unchecked
            {
                var key = ((ulong)(ushort)column << 48) ^ ((ulong)(uint)slot << 12) ^ (bits * 0x9E3779B97F4A7C15ul) ^ ((ulong)(ushort)column * 0xD6E8FEB86659FD93ul);
                key ^= key >> 33;
                key *= 0xFF51AFD7ED558CCDul;
                key ^= key >> 33;
                key *= 0xC4CEB9FE1A85EC53ul;
                return key ^ (key >> 33);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Move(ulong digest, int column, int slot, uint before, uint after)
        {
            unchecked
            {
                return before == after ? digest : digest - Key(column, slot, before) + Key(column, slot, after);
            }
        }

        public static ulong Sum(int column, int* values, int start, int count)
        {
            if (values == null && count != 0)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var sum = 0ul;
            unchecked
            {
                for (var row = 0; row < count; row++)
                {
                    sum += Key(column, start + row, (uint)values[start + row]);
                }
            }

            return sum;
        }

        public static ulong Sum(int column, int* values, int start, ulong* words, int wordCount)
        {
            if (values == null || words == null)
            {
                throw new ArgumentNullException(values == null ? nameof(values) : nameof(words));
            }

            var sum = 0ul;
            unchecked
            {
                for (var w = 0; w < wordCount; w++)
                {
                    var bits = words[w];
                    while (bits != 0)
                    {
                        var row = start + (w << 6) + Tzcnt(bits);
                        bits &= bits - 1;
                        sum += Key(column, row, (uint)values[row]);
                    }
                }
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Tzcnt(ulong value)
        {
#if NET
            return System.Numerics.BitOperations.TrailingZeroCount(value);
#else
            var count = 0;
            while ((value & 1ul) == 0ul && count < 64)
            {
                value >>= 1;
                count++;
            }

            return count;
#endif
        }
    }
}
`````

### sv src/Sv/EventStreams.cs

Path: `sv/src/Sv/EventStreams.cs`  (23010 bytes)

`````csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv
{
    /// <summary>
    /// Struct event maker: an instance interface (C# 9 / Unity portable) the JIT devirtualizes and
    /// inlines into the emit loop, so it pays no <c>UnmanagedCallersOnly</c> entry gate. Passed by
    /// <c>ref</c>, so a maker may carry state (counters, lookup pointers).
    /// </summary>
    public interface IEventMaker<T> where T : unmanaged
    {
        T Make(int row, int value);
    }

    public sealed unsafe class Channel<T> where T : unmanaged
    {
        internal EventStreams Owner = null!;

        public ushort Id;

        internal int Writer;

        public void Write(T evt)
        {
            Owner.Append(Id, Writer, &evt);
        }
    }

    public sealed unsafe class EventStreams : IDisposable
    {
        private const int HeaderBytes = 8;

        private readonly int writerCount;

        private readonly byte** arenas;

        private readonly int* arenaCapacity;

        private readonly int* arenaPeak;

        private readonly int* writeOffset;

        private readonly int* readOffset;

        private readonly int* readCursorOffset;

        private int active;

        private const int LockStride = 8;

        private const int BatchBytes = 256;

        private const int OffsetStride = LockStride * 2;

        private readonly long* locks;

        private int nextTypeId;

        private bool disposed;

        internal UndoLog? Undo;

        int[]? undoActiveHalves;

        readonly List<ArenaReplace> arenaReplaces = new();

        readonly struct ArenaReplace
        {
            public readonly int Index;

            public readonly IntPtr Old;

            public readonly int OldCapacity;

            public readonly IntPtr New;

            public readonly int Frame;

            public ArenaReplace(int index, IntPtr old, int oldCapacity, IntPtr @new, int frame)
            {
                Index = index;
                Old = old;
                OldCapacity = oldCapacity;
                New = @new;
                Frame = frame;
            }
        }

        EventStreams(int writerCount, int bytesPerWriter)
        {
            this.writerCount = writerCount;
            arenas = (byte**)Memory.Alloc((nuint)writerCount * 2 * (nuint)sizeof(byte*));
            arenaCapacity = (int*)Memory.AllocZeroed((nuint)writerCount * 2 * sizeof(int));
            arenaPeak = (int*)Memory.AllocZeroed((nuint)writerCount * 2 * sizeof(int));
            readOffset = (int*)Memory.AllocZeroed((nuint)writerCount * sizeof(int));
            readCursorOffset = (int*)Memory.AllocZeroed((nuint)writerCount * sizeof(int));
            locks = (long*)Memory.AllocAligned((nuint)(writerCount * LockStride * sizeof(long)), 64);
            Memory.Clear(locks, (nuint)(writerCount * LockStride * sizeof(long)));
            writeOffset = (int*)(locks + 1);
            var rounded = Round(bytesPerWriter);
            for (var w = 0; w < writerCount; w++)
            {
                for (var b = 0; b < 2; b++)
                {
                    var index = w * 2 + b;
                    arenas[index] = (byte*)Memory.Alloc((nuint)rounded);
                    arenaCapacity[index] = rounded;
                }
            }
        }

        public static EventStreams Create(int writerCount = 1, int bytesPerWriter = 4096)
        {
            if (writerCount < 1 || writerCount > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(writerCount), "sv: 1..64 writers");
            }

            if (bytesPerWriter < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerWriter), "sv: bytesPerWriter must be >= 1 (0 rounds to 0 and the growth loop never terminates)");
            }

            return new EventStreams(writerCount, bytesPerWriter);
        }

        static int Round(int bytes) => (bytes + 7) & ~7;

        public Channel<T> CreateChannel<T>(int writer = 0) where T : unmanaged
        {
            if (writer < 0 || writer >= writerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(writer));
            }

            if (nextTypeId == ushort.MaxValue)
            {
                throw new InvalidOperationException("sv: channel type id exhaustion");
            }

            return new Channel<T> { Owner = this, Id = (ushort)nextTypeId++, Writer = writer };
        }

        internal void Append<T>(ushort id, int writer, T* payload) where T : unmanaged
        {
            AppendBatch(id, writer, payload, 1);
        }

        internal void AppendBatch<T>(ushort id, int writer, T* payload, int count) where T : unmanaged
        {
            var size = sizeof(T);
            var stride = HeaderBytes + Round(size);
            var need = stride * count;
            ref var gate = ref locks[writer * LockStride];
            while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
            {
                Thread.SpinWait(1);
            }

            try
            {
                var index = writer * 2 + active;
                var at = writeOffset[writer * OffsetStride];
                var required = (long)at + need;
                if (required > int.MaxValue / 2)
                {
                    throw new InvalidOperationException("sv: event arena exceeded 1 GiB per writer");
                }

                if (required > arenaCapacity[index])
                {
                    var capacity = arenaCapacity[index];
                    while (capacity < required)
                    {
                        capacity *= 2;
                    }

                    if (Undo != null)
                    {
                        var previous = arenas[index];
                        var fresh = (byte*)Memory.Alloc((nuint)capacity);
                        Buffer.MemoryCopy(previous, fresh, capacity, arenaCapacity[index]);
                        arenaReplaces.Add(new ArenaReplace(index, (IntPtr)previous, arenaCapacity[index], (IntPtr)fresh, Undo.Top));
                        arenas[index] = fresh;
                    }
                    else
                    {
                        arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)capacity);
                    }

                    arenaCapacity[index] = capacity;
                }

                var dst = arenas[index] + at;
                Undo?.Note(dst, need);
                for (var i = 0; i < count; i++)
                {
                    *(ushort*)dst = id;
                    *(ushort*)(dst + 2) = 0;
                    *(int*)(dst + 4) = size;
                    *(T*)(dst + HeaderBytes) = payload[i];
                    dst += stride;
                }

                Undo?.Note(writeOffset + writer * OffsetStride, sizeof(int));
                writeOffset[writer * OffsetStride] = at + need;
            }
            finally
            {
                Volatile.Write(ref gate, 0);
            }
        }

        void EmitWord<T, TMaker>(Channel<T> channel, ulong mask, int rowBase, int* values, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (mask == 0)
            {
                return;
            }

            if (sizeof(T) > BatchBytes)
            {
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    var row = rowBase + bit;
                    var evt = maker.Make(row, values == null ? 1 : values[row]);
                    Append(channel.Id, channel.Writer, &evt);
                }

                return;
            }

            var batch = stackalloc T[64];
            var count = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                var row = rowBase + bit;
                batch[count++] = maker.Make(row, values == null ? 1 : values[row]);
            }

            AppendBatch(channel.Id, channel.Writer, batch, count);
        }

#if NET
        readonly struct PointerMaker<T> : IEventMaker<T> where T : unmanaged
        {
            readonly delegate* unmanaged<int, int, T> make;

            public PointerMaker(delegate* unmanaged<int, int, T> make)
            {
                this.make = make;
            }

            public T Make(int row, int value) => make(row, value);
        }
#endif

        internal long ArenaBytes
        {
            get
            {
                long bytes = 0;
                for (var index = 0; index < writerCount * 2; index++)
                {
                    bytes += arenaCapacity[index];
                }

                return bytes;
            }
        }

        public unsafe void Swap()
        {
            var undo = Undo;
            for (var w = 0; w < writerCount; w++)
            {
                var used = writeOffset[w * OffsetStride];
                if (undo != null)
                {
                    undo.Note(readOffset + w, sizeof(int));
                    undo.Note(writeOffset + w * OffsetStride, sizeof(int));
                    undo.Note(readCursorOffset + w, sizeof(int));
                    undo.Note(arenaPeak + (w * 2 + active), sizeof(int));
                    undo.Note(arenaPeak + (w * 2 + (1 - active)), sizeof(int));
                }

                readOffset[w] = used;
                writeOffset[w * OffsetStride] = 0;
                readCursorOffset[w] = 0;
                DecayPeak(w * 2 + active, used);
                DecayPeak(w * 2 + (1 - active), used);
                if (undo == null)
                {
                    ShrinkArena(w * 2 + active);
                    ShrinkArena(w * 2 + (1 - active));
                }
            }

            active ^= 1;
        }

        void DecayPeak(int index, int used)
        {
            var decayed = arenaPeak[index] - (arenaPeak[index] >> 4);
            arenaPeak[index] = used > decayed ? used : decayed;
        }

        void ShrinkArena(int index)
        {
            var capacity = arenaCapacity[index];
            var peak = arenaPeak[index];
            if (capacity <= 4096 || (long)peak * 4 > (long)capacity * 3)
            {
                return;
            }

            var target = Round(Math.Max(4096, peak + (peak >> 3)));
            arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)target);
            arenaCapacity[index] = target;
        }

        public bool TryReadNext(out ushort typeId, out void* payload, out int size)
        {
            var buffer = 1 - active;
            var writer = 0;
            while (writer < writerCount)
            {
                var offset = readCursorOffset[writer];
                if (offset < readOffset[writer])
                {
                    var src = arenas[writer * 2 + buffer] + offset;
                    typeId = *(ushort*)src;
                    size = *(int*)(src + 4);
                    payload = src + HeaderBytes;
                    Undo?.Note(readCursorOffset + writer, sizeof(int));
                    readCursorOffset[writer] = offset + HeaderBytes + Round(size);
                    return true;
                }

                writer++;
            }

            typeId = 0;
            size = 0;
            payload = null;
            return false;
        }

#if NET
        public void EmitFromMask<T>(Channel<T> channel, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromMask(channel, enabled, offset, ref maker);
        }
#endif

        public void EmitFromMask<T, TMaker>(Channel<T> channel, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            EmitWord(channel, enabled, offset, null, ref maker);
        }

#if NET
        public void EmitFromBools<T>(Channel<T> channel, byte* cond, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromBools(channel, cond, start, count, ref maker);
        }
#endif

        public void EmitFromBools<T, TMaker>(Channel<T> channel, byte* cond, int start, int count, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var row = start;
            var remaining = count;
            while (remaining >= 64)
            {
                EmitWord(channel, Simd.BoolNonZeroWord(cond, row), row, null, ref maker);
                row += 64;
                remaining -= 64;
            }

            ulong tail = 0;
            for (var i = 0; i < remaining; i++)
            {
                if (cond[row + i] != 0)
                {
                    tail |= 1ul << i;
                }
            }

            EmitWord(channel, tail, row, null, ref maker);
        }

#if NET
        public void EmitFromBools<T>(Channel<T> channel, byte* cond, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromBools(channel, cond, enabled, offset, ref maker);
        }
#endif

        public void EmitFromBools<T, TMaker>(Channel<T> channel, byte* cond, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var mask = enabled;
            ulong hits = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                if (cond[offset + bit] != 0)
                {
                    hits |= 1ul << bit;
                }
            }

            EmitWord(channel, hits, offset, null, ref maker);
        }

#if NET
        public void EmitFromNonZero<T>(Channel<T> channel, int* values, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromNonZero(channel, values, start, count, ref maker);
        }
#endif

        public void EmitFromNonZero<T, TMaker>(Channel<T> channel, int* values, int start, int count, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var row = start;
            var remaining = count;
            while (remaining >= 64)
            {
                EmitWord(channel, Simd.IntNonZeroWord(values, row), row, values, ref maker);
                row += 64;
                remaining -= 64;
            }

            ulong tail = 0;
            for (var i = 0; i < remaining; i++)
            {
                if (values[row + i] != 0)
                {
                    tail |= 1ul << i;
                }
            }

            EmitWord(channel, tail, row, values, ref maker);
        }

#if NET
        public void EmitFromNonZero<T>(Channel<T> channel, int* values, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromNonZero(channel, values, enabled, offset, ref maker);
        }
#endif

        public void EmitFromNonZero<T, TMaker>(Channel<T> channel, int* values, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var mask = enabled;
            ulong hits = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                if (values[offset + bit] != 0)
                {
                    hits |= 1ul << bit;
                }
            }

            EmitWord(channel, hits, offset, values, ref maker);
        }

#if NET
        public static void WalkNonZero(int* values, ulong enabled, int offset, delegate* unmanaged<int, int, void> visit)
        {
            var mask = enabled;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                var row = offset + bit;
                var value = values[row];
                if (value != 0)
                {
                    visit(row, value);
                }
            }
        }
#endif

        const uint StateMagic = 0x56455653;

        /// <summary>
        /// Saves both halves of the double buffer (this frame's writes and the published frame being
        /// drained) with every cursor. Channels are cold setup, not state: the loading streams must
        /// have created the same channels. Warm saves are managed-allocation-free.
        /// </summary>
        public void SaveState(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            VisitState(ref codec);
        }

        public void LoadState(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            VisitState(ref codec);
            state.EndRead();
        }

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            if (codec.Reading)
            {
                EndUndoWindow();
            }

            codec.Header(StateMagic, ((long)writerCount << 32) | (uint)nextTypeId, "event streams");
            codec.Value(ref active);
            for (var w = 0; w < writerCount; w++)
            {
                var written = writeOffset[w * OffsetStride];
                var published = readOffset[w];
                var cursor = readCursorOffset[w];
                codec.Value(ref written);
                codec.Value(ref published);
                codec.Value(ref cursor);
                if (codec.Reading)
                {
                    if (written < 0 || published < 0)
                    {
                        throw new System.IO.InvalidDataException("sv: event stream state carries negative offsets");
                    }

                    readCursorOffset[w] = cursor;
                    EnsureArena(w * 2 + active, written);
                    EnsureArena(w * 2 + (1 - active), published);
                    writeOffset[w * OffsetStride] = written;
                    readOffset[w] = published;
                }

                codec.Bytes(arenas[w * 2 + active], written);
                codec.Bytes(arenas[w * 2 + (1 - active)], published);
            }
        }

        void EnsureArena(int index, int bytes)
        {
            if (arenaCapacity[index] >= bytes)
            {
                return;
            }

            if (bytes > int.MaxValue / 2)
            {
                throw new System.IO.InvalidDataException("sv: event stream state exceeds 1 GiB per writer");
            }

            var capacity = arenaCapacity[index];
            while (capacity < bytes)
            {
                capacity *= 2;
            }

            arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)capacity);
            arenaCapacity[index] = capacity;
            arenaPeak[index] = capacity;
        }

        internal void BeginUndoWindow(int frames)
        {
            EndUndoWindow();
            Undo = new UndoLog(frames);
            undoActiveHalves = new int[frames];
            Undo.OpenFrame();
            CaptureUndoScalars();
        }

        internal void BeginUndoFrame()
        {
            var undo = Undo;
            if (undo == null)
            {
                return;
            }

            var previousBottom = undo.Bottom;
            undo.OpenFrame();
            CaptureUndoScalars();
            if (undo.Bottom > previousBottom)
            {
                for (var i = arenaReplaces.Count - 1; i >= 0; i--)
                {
                    if (arenaReplaces[i].Frame < undo.Bottom)
                    {
                        Memory.Free((void*)arenaReplaces[i].Old);
                        arenaReplaces.RemoveAt(i);
                    }
                }
            }
        }

        void CaptureUndoScalars()
        {
            undoActiveHalves![Undo!.Top % Undo.Window] = active;
        }

        internal void EndUndoWindow()
        {
            Undo?.Dispose();
            Undo = null;
            undoActiveHalves = null;
            for (var i = arenaReplaces.Count - 1; i >= 0; i--)
            {
                Memory.Free((void*)arenaReplaces[i].Old);
            }

            arenaReplaces.Clear();
        }

        internal void RollbackTo(int target)
        {
            var undo = Undo;
            if (undo == null)
            {
                throw new InvalidOperationException("sv: no undo window is open");
            }

            undo.UndoFrames(undo.Top, target);
            undo.TruncateTo(target);
            active = undoActiveHalves![target % undo.Window];
            for (var i = arenaReplaces.Count - 1; i >= 0; i--)
            {
                var replace = arenaReplaces[i];
                if (replace.Frame < target)
                {
                    continue;
                }

                Memory.Free((void*)replace.New);
                arenas[replace.Index] = (byte*)replace.Old;
                arenaCapacity[replace.Index] = replace.OldCapacity;
                arenaReplaces.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EndUndoWindow();
            for (var w = 0; w < writerCount; w++)
            {
                Memory.Free(arenas[w * 2]);
                Memory.Free(arenas[w * 2 + 1]);
            }

            Memory.Free(arenas);
            Memory.Free(arenaCapacity);
            Memory.Free(arenaPeak);
            Memory.Free(readOffset);
            Memory.Free(readCursorOffset);
            Memory.FreeAligned(locks);
            disposed = true;
        }
    }
}
`````

### sv src/Sv/EventStreams.Derive.cs

Path: `sv/src/Sv/EventStreams.Derive.cs`  (8777 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DenseEvent
    {
        public long Tick;

        public int Count;

        public ushort Channel;

        public DenseEvent(ushort channel, long tick, int count)
        {
            Channel = channel;
            Tick = tick;
            Count = count;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct EdgeUnit
    {
        public int* Slots;

        public ulong Edges;

        public ulong State;

        public EdgeUnit(int* slots, ulong edges, ulong state)
        {
            Slots = slots;
            Edges = edges;
            State = state;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EdgeRecord
    {
        public int Slot;

        public int Value;
    }

    public sealed unsafe class EventDerive : IDisposable
    {
        public const int DefaultCapacity = 16384;

        public const int MaxCapacity = 1 << 24;

        readonly EdgeRecord* records;

        readonly EdgeRecord* scratch;

        readonly int* counts;

        long edges;

        bool disposed;

        public EventDerive(ushort channel, int capacity = DefaultCapacity)
        {
            if (capacity < 1 || capacity > MaxCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"sv: event capacity per channel must be in [1, {MaxCapacity}], got {capacity}");
            }

            Channel = channel;
            Capacity = capacity;
            records = (EdgeRecord*)Memory.Alloc((nuint)capacity, (nuint)sizeof(EdgeRecord));
            scratch = (EdgeRecord*)Memory.Alloc((nuint)capacity, (nuint)sizeof(EdgeRecord));
            counts = (int*)Memory.Alloc(256, sizeof(int));
        }

        public ushort Channel { get; }

        public int Capacity { get; }

        public long Edges => Volatile.Read(ref edges);

        public bool IsDense => Edges > Capacity;

        public int Delivered { get; private set; }

        public bool LastDense { get; private set; }

        public void Begin()
        {
            Volatile.Write(ref edges, 0);
        }

        public void AddCount(long count)
        {
            Interlocked.Add(ref edges, count);
        }

        public static long CountEdges(ulong* edgeWords, int words)
        {
            long total = 0;
            for (var w = 0; w < words; w++)
            {
                total += Bits.PopCount(edgeWords[w]);
            }

            return total;
        }

        public static long CountEdges(ReadOnlySpan<EdgeUnit> units)
        {
            long total = 0;
            for (var u = 0; u < units.Length; u++)
            {
                total += Bits.PopCount(units[u].Edges);
            }

            return total;
        }

        public int Derive<T, TMaker>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense, long tick, ulong* edgeWords, ulong* stateWords, int words, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            Check(streams, channel, dense);
            if (Dense(streams, dense, tick))
            {
                return 0;
            }

            var batch = stackalloc T[64];
            var delivered = 0;
            for (var w = 0; w < words; w++)
            {
                var mask = edgeWords[w];
                if (mask == 0)
                {
                    continue;
                }

                var state = stateWords == null ? ulong.MaxValue : stateWords[w];
                var count = 0;
                var rowBase = w << 6;
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    batch[count++] = maker.Make(rowBase + bit, (int)((state >> bit) & 1ul));
                }

                streams.AppendBatch(channel.Id, channel.Writer, batch, count);
                delivered += count;
            }

            return Finish(delivered);
        }

        public int Derive<T, TMaker>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense, long tick, ReadOnlySpan<EdgeUnit> units, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            Check(streams, channel, dense);
            if (Dense(streams, dense, tick))
            {
                return 0;
            }

            var count = 0;
            var ordered = true;
            var last = -1;
            for (var u = 0; u < units.Length; u++)
            {
                var unit = units[u];
                var mask = unit.Edges;
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    if (count == Capacity)
                    {
                        throw new InvalidOperationException("sv: edge count passed to the derive is below the edges present");
                    }

                    var slot = unit.Slots[bit];
                    ordered &= slot > last;
                    last = slot;
                    records[count].Slot = slot;
                    records[count].Value = (int)((unit.State >> bit) & 1ul);
                    count++;
                }
            }

            var sorted = ordered ? records : SortBySlot(count);
            var batch = stackalloc T[64];
            var at = 0;
            while (at < count)
            {
                var take = count - at < 64 ? count - at : 64;
                for (var i = 0; i < take; i++)
                {
                    var record = sorted[at + i];
                    batch[i] = maker.Make(record.Slot, record.Value);
                }

                streams.AppendBatch(channel.Id, channel.Writer, batch, take);
                at += take;
            }

            return Finish(count);
        }

        EdgeRecord* SortBySlot(int count)
        {
            var source = records;
            var target = scratch;
            for (var pass = 0; pass < 4; pass++)
            {
                var shift = pass * 8;
                for (var b = 0; b < 256; b++)
                {
                    counts[b] = 0;
                }

                for (var i = 0; i < count; i++)
                {
                    counts[(int)(((uint)source[i].Slot >> shift) & 255u)]++;
                }

                if (counts[(int)(((uint)source[0].Slot >> shift) & 255u)] == count)
                {
                    continue;
                }

                var total = 0;
                for (var b = 0; b < 256; b++)
                {
                    var size = counts[b];
                    counts[b] = total;
                    total += size;
                }

                for (var i = 0; i < count; i++)
                {
                    var record = source[i];
                    target[counts[(int)(((uint)record.Slot >> shift) & 255u)]++] = record;
                }

                var swap = source;
                source = target;
                target = swap;
            }

            return source;
        }

        bool Dense(EventStreams streams, Channel<DenseEvent> dense, long tick)
        {
            var total = Edges;
            if (total <= Capacity)
            {
                LastDense = false;
                return false;
            }

            var record = new DenseEvent(Channel, tick, total > int.MaxValue ? int.MaxValue : (int)total);
            streams.AppendBatch(dense.Id, dense.Writer, &record, 1);
            LastDense = true;
            Delivered = 0;
            return true;
        }

        int Finish(int delivered)
        {
            Delivered = delivered;
            return delivered;
        }

        void Check<T>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense) where T : unmanaged
        {
            Guard.NotDisposed(disposed, this);
            if (streams == null)
            {
                throw new ArgumentNullException(nameof(streams));
            }

            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            if (dense == null)
            {
                throw new ArgumentNullException(nameof(dense));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Memory.Free(records);
            Memory.Free(scratch);
            Memory.Free(counts);
        }
    }
}
`````

### sv src/Sv/Board.Keyframe.cs

Path: `sv/src/Sv/Board.Keyframe.cs`  (14753 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public sealed unsafe partial class Board
    {
        internal struct KeyframeRecord
        {
            public long Id;

            public int Stat;

            public ModifierKind Kind;

            public float Value;

            public int Source;

            public long Expiry;
        }

        const uint KeyframeMagic = 0x4B46564C;

        ulong* keyframePages;

        int keyframePageWords;

        int keyframeDirtyCount;

        KeyframeRecord* keyframeScratch;

        int keyframeScratchCap;

        /// <summary>
        /// Digest pages (256 slots) dirtied since the last <see cref="SaveKeyframe"/> — the next
        /// keyframe's cost is proportional to this count, never to <see cref="Capacity"/>. Reset by
        /// <see cref="SaveKeyframe"/> and re-based by loading state or a keyframe; renumbering slots
        /// or rolling an undo window back conservatively dirties every page. Lazily allocated on the
        /// first slot mutation, freed when a load re-bases the epoch.
        /// </summary>
        public int KeyframeDirtyPages => keyframeDirtyCount;

        internal void InitKeyframes()
        {
            keyframePageWords = (PageCount + 63) >> 6;
            keyframePages = arena.AllocZeroed<ulong>(keyframePageWords);
        }

        internal void MarkKeyframeDirty(int slot)
        {
            if (keyframePages == null)
            {
                InitKeyframes();
            }

            var page = slot >> 8;
            var bit = 1ul << (page & 63);
            if ((keyframePages[page >> 6] & bit) != 0)
            {
                return;
            }

            keyframePages[page >> 6] |= bit;
            keyframeDirtyCount++;
        }

        internal void MarkAllKeyframesDirty()
        {
            if (keyframePages == null)
            {
                InitKeyframes();
            }

            Memory.Clear(keyframePages, (nuint)keyframePageWords * sizeof(ulong));
            var full = PageCount >> 6;
            for (var word = 0; word < full; word++)
            {
                keyframePages[word] = ulong.MaxValue;
            }

            var tail = PageCount & 63;
            if (tail != 0)
            {
                keyframePages[full] = (1ul << tail) - 1;
            }

            keyframeDirtyCount = PageCount;
        }

        void ResetKeyframeDirty()
        {
            if (keyframePages == null)
            {
                return;
            }

            Memory.Clear(keyframePages, (nuint)keyframePageWords * sizeof(ulong));
            keyframeDirtyCount = 0;
        }

        void RebaseKeyframes()
        {
            if (keyframePages == null)
            {
                return;
            }

            arena.Free(keyframePages, keyframePageWords);
            keyframePages = null;
            keyframePageWords = 0;
            keyframeDirtyCount = 0;
        }

        void EnsureKeyframeScratch(int count)
        {
            if (count <= keyframeScratchCap)
            {
                return;
            }

            var previous = keyframeScratchCap;
            keyframeScratchCap = count * 2;
            keyframeScratch = arena.Realloc(keyframeScratch, previous, keyframeScratchCap);
        }

        int ChainLength(int slot)
        {
            var count = 0;
            for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// Writes an incremental keyframe into <paramref name="state"/> (overwriting it): the state of
        /// every digest page dirtied since the previous <see cref="SaveKeyframe"/> — live bits plus each
        /// live slot's modifier records — followed by the digest, and resets the dirty-page tracking.
        /// Cost is O(dirty pages), never O(<see cref="Capacity"/>). Take keyframes at frame boundaries
        /// (after <see cref="Recalculate"/>). Warm saves are managed-allocation-free; an open undo
        /// window stays open.
        /// </summary>
        public void SaveKeyframe(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            codec.Header(KeyframeMagic, StateShape, "keyframe");
            var insertion = nextInsertionId;
            codec.Value(ref insertion);
            var generation = insertionGeneration;
            codec.Value(ref generation);
            var digest = Digest;
            codec.Value(ref digest);
            var pages = keyframeDirtyCount;
            codec.Value(ref pages);
            var written = 0;
            for (var word = 0; word < keyframePageWords && written < pages; word++)
            {
                var bits = keyframePages[word];
                while (bits != 0 && written < pages)
                {
                    var page = (word << 6) + Bits.Tzcnt(bits);
                    bits &= bits - 1;
                    written++;
                    codec.Value(ref page);
                    var first = page << 2;
                    var wordsInPage = Math.Min(4, LiveWordCount - first);
                    codec.Bytes(live + first, (long)wordsInPage * sizeof(ulong));
                    var slotBase = page << 8;
                    var slotsInPage = Math.Min(256, Capacity - slotBase);
                    for (var local = 0; local < slotsInPage; local++)
                    {
                        var slot = slotBase + local;
                        var count = 0;
                        if (statCount > 0 && (live[slot >> 6] & (1ul << (slot & 63))) != 0)
                        {
                            count = ChainLength(slot);
                        }

                        codec.Value(ref count);
                        if (count == 0)
                        {
                            continue;
                        }

                        EnsureKeyframeScratch(count);
                        var at = 0;
                        for (var record = Slot(slot).Head; record >= 0; record = records[record].Next)
                        {
                            keyframeScratch[at++] = new KeyframeRecord
                            {
                                Id = records[record].Id,
                                Stat = records[record].Stat,
                                Kind = records[record].Kind,
                                Value = records[record].Value,
                                Source = records[record].Source,
                                Expiry = records[record].Expiry,
                            };
                        }

                        codec.Bytes(keyframeScratch, (long)count * sizeof(KeyframeRecord));
                    }
                }
            }

            ResetKeyframeDirty();
        }

        /// <summary>
        /// Folds one <see cref="SaveKeyframe"/> onto this board: the base must be the source board's
        /// state when that keyframe's epoch began. Per dirty page, acquired slots acquire, released
        /// slots release and live slots have their modifier records replaced (original insertion ids
        /// preserved); the board then recalculates the touched slots and verifies the digest, throwing
        /// <see cref="System.IO.InvalidDataException"/> when the keyframe does not fold onto this base.
        /// The restored board continues simulation: interned sets and blocks are re-derived
        /// content-addressed, so digests, columns and folds match the source board exactly while the
        /// saved-state bytes of the two tables may differ. Closes any open undo window.
        /// </summary>
        public void LoadKeyframe(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            codec.Header(KeyframeMagic, StateShape, "keyframe");
            var insertion = 0L;
            codec.Value(ref insertion);
            var generation = 0;
            codec.Value(ref generation);
            if (generation > insertionGeneration || (generation == insertionGeneration && insertion > nextInsertionId))
            {
                insertionGeneration = generation;
                nextInsertionId = insertion;
            }

            InvalidateUndo();
            var expected = 0ul;
            codec.Value(ref expected);
            var pages = 0;
            codec.Value(ref pages);
            if (pages < 0 || pages > PageCount)
            {
                throw new System.IO.InvalidDataException("sv: corrupt keyframe");
            }

            var pageLive = stackalloc ulong[4];
            for (var p = 0; p < pages; p++)
            {
                var page = 0;
                codec.Value(ref page);
                if (page < 0 || page >= PageCount)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                }

                var first = page << 2;
                var wordsInPage = Math.Min(4, LiveWordCount - first);
                Memory.Clear(pageLive, 4 * sizeof(ulong));
                codec.Bytes(pageLive, (long)wordsInPage * sizeof(ulong));
                var slotBase = page << 8;
                var slotsInPage = Math.Min(256, Capacity - slotBase);
                for (var local = 0; local < slotsInPage; local++)
                {
                    var slot = slotBase + local;
                    var bit = 1ul << (slot & 63);
                    var shippedLive = (pageLive[local >> 6] & bit) != 0;
                    var wasLive = (live[slot >> 6] & bit) != 0;
                    var count = 0;
                    codec.Value(ref count);
                    if (count < 0 || (count > 0 && (statCount == 0 || !shippedLive)))
                    {
                        throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                    }

                    if (count > 0)
                    {
                        EnsureKeyframeScratch(count);
                        codec.Bytes(keyframeScratch, (long)count * sizeof(KeyframeRecord));
                    }

                    if (!shippedLive)
                    {
                        if (!wasLive)
                        {
                            continue;
                        }

                        live[slot >> 6] &= ~bit;
                        LiveCount--;
                        if ((slot >> 6) < freeHint)
                        {
                            freeHint = slot >> 6;
                        }

                        if (statCount > 0)
                        {
                            ClearSlotStats(slot);
                            ZeroSlotRows(slot);
                        }

                        continue;
                    }

                    if (!wasLive)
                    {
                        live[slot >> 6] |= bit;
                        LiveCount++;
                        if (statCount > 0)
                        {
                            EnsureSlotPages(slot);
                            MarkSlotAcquired(slot);
                        }
                    }

                    if (statCount == 0)
                    {
                        continue;
                    }

                    ref var slotState = ref Slot(slot);
                    var identical = wasLive && ChainLength(slot) == count;
                    if (identical)
                    {
                        var at = slotState.Head;
                        for (var r = 0; r < count; r++, at = records[at].Next)
                        {
                            var shipped = keyframeScratch[r];
                            if (records[at].Id == shipped.Id && records[at].Stat == shipped.Stat && records[at].Kind == shipped.Kind &&
                                Bits.OfSingle(records[at].Value) == Bits.OfSingle(shipped.Value) &&
                                records[at].Source == shipped.Source && records[at].Expiry == shipped.Expiry)
                            {
                                continue;
                            }

                            identical = false;
                            break;
                        }
                    }

                    if (identical)
                    {
                        continue;
                    }

                    for (var at = slotState.Head; at >= 0;)
                    {
                        var next = records[at].Next;
                        WheelUnlink(at);
                        FreeRecord(at);
                        at = next;
                    }

                    slotState.Head = -1;
                    for (var r = count - 1; r >= 0; r--)
                    {
                        var shipped = keyframeScratch[r];
                        if (shipped.Stat < 0 || shipped.Stat >= statCount)
                        {
                            throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                        }

                        var record = AllocRecord();
                        records[record] = new ModRecord
                        {
                            Id = shipped.Id,
                            Kind = shipped.Kind,
                            Value = shipped.Value,
                            Source = shipped.Source,
                            Expiry = shipped.Expiry,
                            Next = slotState.Head,
                            Slot = slot,
                            Stat = shipped.Stat,
                            Wheel = -1
                        };
                        slotState.Head = record;
                        if (shipped.Expiry != long.MaxValue)
                        {
                            WheelInsert(record, shipped.Id, shipped.Expiry);
                        }
                    }

                    MarkSlotStatsDirty(slot);
                }
            }

            var word = 0;
            while (word < LiveWordCount && live[word] == ulong.MaxValue)
            {
                word++;
            }

            freeHint = word;
            if (statCount > 0)
            {
                Recalculate();
                EnsureDigestCurrent();
                if (Digest != expected)
                {
                    throw new System.IO.InvalidDataException($"sv: keyframe does not fold onto this base — digest {Digest:X16} != {expected:X16}");
                }
            }

            RebaseKeyframes();
            RefillSink();
            state.EndRead();
        }}
}
`````

### sv src/Sv/Board.Undo.cs

Path: `sv/src/Sv/Board.Undo.cs`  (13301 bytes)

`````csharp
using System;

namespace Sv
{
    public sealed unsafe partial class Board
    {
        internal struct SetUndoFrame
        {
            public int SetCount;

            public int SetCapacity;

            public int BucketMask;

            public int SetFreeHead;

            public int LiveSets;

            public int ContentUsed;

            public int ContentCapacity;

            public long Generation;

            public void* Buckets;

            public void* SetStat;

            public void* SetHash;

            public void* SetComputed;

            public void* SetContentOffset;

            public void* SetContentCount;

            public void* SetContentSpan;

            public void* SetRefCount;

            public void* SetFolded;

            public void* SetFreeNext;

            public void* SetFreedAt;

            public void* ContentArena;
        }

        internal struct BlockUndoFrame
        {
            public int BlockCount;

            public int BlockCapacity;

            public int BucketMask;

            public int BlockFreeHead;

            public void* Buckets;

            public void* BlockSets;

            public void* BlockHash;

            public void* BlockRefCount;

            public void* BlockFreeNext;
        }

        internal struct BoardUndoFrame
        {
            public int LiveCount;

            public int FreeHint;

            public long NextInsertionId;

            public int RecordCount;

            public int RecordCap;

            public int RecordFree;

            public void* Records;

            public int WheelEntryCount;

            public int WheelEntryCap;

            public int WheelEntryFree;

            public int WheelEntryLive;

            public long WheelTick;

            public int WheelOverdue;

            public void* WheelEntries;

            public int DirtyTouchedCount;

            public int DirtyTouchedCap;

            public void* DirtyTouched;

            public int AcquiredTouchedCount;

            public ulong Digest;

            public byte DigestPagesDirty;

            public byte PageDigestsRewritten;

            public SetUndoFrame Set;

            public BlockUndoFrame Block;
        }

        UndoLog? undo;

        BoardUndoFrame[]? undoFrames;

        /// <summary>
        /// Undo frames currently retained by the open window (0 when no window is open). Rolling back
        /// is limited to this many frames.
        /// </summary>
        public int UndoFrames => undo?.Frames ?? 0;

        internal UndoLog? ActiveUndo => undo;

        internal int UndoTop => undo?.Top ?? -1;

        /// <summary>
        /// Opens an undo window retaining the last <paramref name="frames"/> frame marks and returns
        /// the board to O(Δ) rollback: every mutation after the window opens logs its prior bytes, set
        /// reclamation waits out the window, and growth reallocations quarantine their old blocks
        /// instead of freeing them. Call <see cref="BeginUndoFrame"/> once per frame boundary;
        /// <see cref="Rollback(int)"/> restores the state of any boundary still inside the window.
        /// Opening a window discards any previous one. Loading state, renumbering slots, rebinding or
        /// invalidating bindings closes the window — bindings are not state, and a rolled-back write
        /// must never target a host column that moved.
        /// </summary>
        public void BeginUndoWindow(int frames)
        {
            EndUndoWindow();
            undo = new UndoLog(frames);
            undoFrames = new BoardUndoFrame[frames];
            if (table != null)
            {
                table.Undo = undo;
                table.ReclaimHorizon = frames;
            }

            if (blocks != null)
            {
                blocks.Undo = undo;
            }

            BeginUndoFrame();
        }

        /// <summary>
        /// Closes the undo window and releases its log; the current state becomes permanent.
        /// </summary>
        public void EndUndoWindow()
        {
            if (undo == null)
            {
                return;
            }

            SweepUndo();
            undo.Dispose();
            undo = null;
            undoFrames = null;
            if (table != null)
            {
                table.Undo = null;
                table.ReclaimHorizon = 0;
            }

            if (blocks != null)
            {
                blocks.Undo = null;
            }
        }

        /// <summary>
        /// Marks a frame boundary: snapshots the mutating scalars and opens the entry frame that the
        /// mutations until the next mark roll into. When the window is full the oldest mark is
        /// committed — its mutations become permanent and can no longer be rolled back.
        /// </summary>
        public void BeginUndoFrame()
        {
            if (undo == null)
            {
                throw new InvalidOperationException("sv: no undo window is open");
            }

            var previousBottom = undo.Bottom;
            undo.OpenFrame();
            if (undo.Bottom > previousBottom)
            {
                undo.CommitFramesBelow(undo.Bottom);
            }

            CaptureUndoFrame(ref undoFrames![(nint)(undo.Top % undoFrames.Length)]);
            SweepUndo();
        }

        /// <summary>
        /// Rolls the board back to the boundary <paramref name="frames"/> marks ago, bit-exactly:
        /// logged writes replay backwards, frame scalars (counts, free heads, table pointers, digest)
        /// restore from the mark's snapshot, and blocks allocated inside the rolled-back frames free.
        /// Equivalent to loading a snapshot saved at that boundary.
        /// </summary>
        public void Rollback(int frames)
        {
            var log = undo;
            if (log == null)
            {
                throw new InvalidOperationException("sv: no undo window is open");
            }

            if (frames < 1 || frames > log.Frames)
            {
                throw new ArgumentOutOfRangeException(nameof(frames), $"sv: rollback depth must be in [1, {log.Frames}]");
            }

            RollbackTo(log.Top - frames + 1);
        }

        internal void RollbackTo(int target)
        {
            var log = undo!;
            if (target < log.Bottom || target > log.Top)
            {
                throw new ArgumentOutOfRangeException(nameof(target), $"sv: rollback target must be in [{log.Bottom}, {log.Top}]");
            }

            var suspectPages = false;
            for (var ordinal = log.Top; ordinal >= target && !suspectPages; ordinal--)
            {
                suspectPages = undoFrames![(nint)(ordinal % undoFrames.Length)].PageDigestsRewritten != 0;
            }

            log.UndoFrames(log.Top, target);
            RestoreUndoFrame(ref undoFrames![(nint)(target % undoFrames.Length)]);
            if (suspectPages)
            {
                digestPagesDirty = true;
            }

            log.DropFreshFrom(target, arena);
            log.TruncateTo(target);
            MarkAllKeyframesDirty();
            SweepUndo();
            RefillSink();
        }

        internal void InvalidateUndo()
        {
            EndUndoWindow();
        }

        internal T* UndoGrow<T>(UndoArrayId id, T* old, int oldCount, int newCount) where T : unmanaged
        {
            return undo == null ? arena.Realloc(old, oldCount, newCount) : undo.ReallocTracked(arena, id, old, oldCount, newCount);
        }

        void CaptureUndoFrame(ref BoardUndoFrame frame)
        {
            frame.LiveCount = LiveCount;
            frame.FreeHint = freeHint;
            frame.NextInsertionId = nextInsertionId;
            frame.RecordCount = recordCount;
            frame.RecordCap = recordCap;
            frame.RecordFree = recordFree;
            frame.Records = records;
            frame.WheelEntryCount = wheelEntryCount;
            frame.WheelEntryCap = wheelEntryCap;
            frame.WheelEntryFree = wheelEntryFree;
            frame.WheelEntryLive = wheelEntryLive;
            frame.WheelTick = wheelTick;
            frame.WheelOverdue = wheelOverdue;
            frame.WheelEntries = wheelEntries;
            frame.DirtyTouchedCount = dirtyTouchedCount;
            frame.DirtyTouchedCap = dirtyTouchedCap;
            frame.DirtyTouched = dirtyTouched;
            frame.AcquiredTouchedCount = acquiredTouchedCount;
            frame.Digest = digest;
            frame.DigestPagesDirty = (byte)(digestPagesDirty ? 1 : 0);
            frame.PageDigestsRewritten = 0;
            table?.CaptureUndo(ref frame.Set);
            blocks?.CaptureUndo(ref frame.Block);
        }

        void RestoreUndoFrame(ref BoardUndoFrame frame)
        {
            LiveCount = frame.LiveCount;
            freeHint = frame.FreeHint;
            nextInsertionId = frame.NextInsertionId;
            recordCount = frame.RecordCount;
            recordCap = frame.RecordCap;
            recordFree = frame.RecordFree;
            records = (ModRecord*)frame.Records;
            wheelEntryCount = frame.WheelEntryCount;
            wheelEntryCap = frame.WheelEntryCap;
            wheelEntryFree = frame.WheelEntryFree;
            wheelEntryLive = frame.WheelEntryLive;
            wheelTick = frame.WheelTick;
            wheelOverdue = frame.WheelOverdue;
            wheelEntries = (WheelEntry*)frame.WheelEntries;
            dirtyTouchedCount = frame.DirtyTouchedCount;
            dirtyTouchedCap = frame.DirtyTouchedCap;
            dirtyTouched = (int*)frame.DirtyTouched;
            acquiredTouchedCount = frame.AcquiredTouchedCount;
            digest = frame.Digest;
            digestPagesDirty = frame.DigestPagesDirty != 0;
            table?.RestoreUndo(ref frame.Set);
            blocks?.RestoreUndo(ref frame.Block);
        }

        void SweepUndo()
        {
            var log = undo!;
            for (var i = 0; i < log.QuarantineCount; i++)
            {
                ref var entry = ref log.QuarantineAt(i);
                if (entry.Fresh)
                {
                    continue;
                }

                var live = UndoPointerOf(entry.Id) == entry.Pointer;
                if (!live)
                {
                    for (var ordinal = log.Bottom; ordinal <= log.Top && !live; ordinal++)
                    {
                        live = ordinal >= entry.First && ordinal <= entry.Last;
                    }
                }

                if (live)
                {
                    continue;
                }

                arena.FreeBytes(entry.Pointer, entry.Bytes);
                log.RemoveQuarantineAt(i);
                i--;
            }
        }

        void* UndoPointerOf(UndoArrayId id)
        {
            if (id == UndoArrayId.Records)
            {
                return records;
            }

            if (id == UndoArrayId.WheelEntries)
            {
                return wheelEntries;
            }

            if (id == UndoArrayId.DirtyTouched)
            {
                return dirtyTouched;
            }

            var pointer = table?.UndoPointerOf(id);
            if (pointer != null)
            {
                return pointer;
            }

            return blocks!.UndoPointerOf(id);
        }

        static void* UndoFramePointer(ref BoardUndoFrame frame, UndoArrayId id)
        {
            if (id == UndoArrayId.Records)
            {
                return frame.Records;
            }

            if (id == UndoArrayId.WheelEntries)
            {
                return frame.WheelEntries;
            }

            if (id == UndoArrayId.DirtyTouched)
            {
                return frame.DirtyTouched;
            }

            if (id >= UndoArrayId.BlockBuckets)
            {
                return id switch
                {
                    UndoArrayId.BlockBuckets => frame.Block.Buckets,
                    UndoArrayId.BlockSets => frame.Block.BlockSets,
                    UndoArrayId.BlockHash => frame.Block.BlockHash,
                    UndoArrayId.BlockRefCount => frame.Block.BlockRefCount,
                    UndoArrayId.BlockFreeNext => frame.Block.BlockFreeNext,
                    _ => null,
                };
            }

            return id switch
            {
                UndoArrayId.SetBuckets => frame.Set.Buckets,
                UndoArrayId.SetStat => frame.Set.SetStat,
                UndoArrayId.SetHash => frame.Set.SetHash,
                UndoArrayId.SetComputed => frame.Set.SetComputed,
                UndoArrayId.SetContentOffset => frame.Set.SetContentOffset,
                UndoArrayId.SetContentCount => frame.Set.SetContentCount,
                UndoArrayId.SetContentSpan => frame.Set.SetContentSpan,
                UndoArrayId.SetRefCount => frame.Set.SetRefCount,
                UndoArrayId.SetFolded => frame.Set.SetFolded,
                UndoArrayId.SetFreeNext => frame.Set.SetFreeNext,
                UndoArrayId.SetFreedAt => frame.Set.SetFreedAt,
                UndoArrayId.ContentArena => frame.Set.ContentArena,
                _ => null,
            };
        }
    }
}
`````

### sv src/Sv/StateCodec.cs

Path: `sv/src/Sv/StateCodec.cs`  (3351 bytes)

`````csharp
using System;

namespace Sv
{
    internal unsafe interface IStateCodec
    {
        bool Reading { get; }

        void Header(uint magic, long shape, string what);

        void Value<T>(ref T value) where T : unmanaged;

        void Bytes(void* pointer, long bytes);
    }

    internal unsafe struct SaveCodec : IStateCodec
    {
        private readonly SvState state;

        public SaveCodec(SvState state)
        {
            this.state = state;
        }

        public readonly bool Reading => false;

        public void Header(uint magic, long shape, string what)
        {
            state.Header(magic, shape);
        }

        public void Value<T>(ref T value) where T : unmanaged
        {
            fixed (T* pointer = &value)
            {
                state.Write(pointer, sizeof(T));
            }
        }

        public void Bytes(void* pointer, long bytes)
        {
            state.Write(pointer, bytes);
        }
    }

    internal unsafe struct LoadCodec : IStateCodec
    {
        private readonly SvState state;

        public LoadCodec(SvState state)
        {
            this.state = state;
        }

        public readonly bool Reading => true;

        public void Header(uint magic, long shape, string what)
        {
            if (state.Read<uint>() != magic || state.Read<long>() != shape)
            {
                throw new System.IO.InvalidDataException($"sv: state does not hold a {what} of this shape");
            }
        }

        public void Value<T>(ref T value) where T : unmanaged
        {
            fixed (T* pointer = &value)
            {
                state.Read(pointer, sizeof(T));
            }
        }

        public void Bytes(void* pointer, long bytes)
        {
            state.Read(pointer, bytes);
        }
    }

    internal unsafe struct SizeCodec : IStateCodec
    {
        public long Total;

        public readonly bool Reading => false;

        public void Header(uint magic, long shape, string what)
        {
            Total += sizeof(uint) + sizeof(long);
        }

        public void Value<T>(ref T value) where T : unmanaged
        {
            Total += sizeof(T);
        }

        public void Bytes(void* pointer, long bytes)
        {
            Total += bytes;
        }
    }

    internal unsafe struct HashCodec : IStateCodec
    {
        private ulong hash;

        public HashCodec(ulong seed)
        {
            hash = seed;
        }

        public readonly bool Reading => false;

        public readonly ulong Digest => hash;

        public void Header(uint magic, long shape, string what)
        {
            Mix((byte*)&magic, sizeof(uint));
            Mix((byte*)&shape, sizeof(long));
        }

        public void Value<T>(ref T value) where T : unmanaged
        {
            fixed (T* pointer = &value)
            {
                Mix((byte*)pointer, sizeof(T));
            }
        }

        public void Bytes(void* pointer, long bytes)
        {
            Mix((byte*)pointer, bytes);
        }

        void Mix(byte* bytes, long count)
        {
            unchecked
            {
                for (var i = 0; i < count; i++)
                {
                    hash ^= bytes[i];
                    hash *= 1099511628211ul;
                }
            }
        }
    }
}
`````

### sv src/Sv/SvState.cs

Path: `sv/src/Sv/SvState.cs`  (2922 bytes)

`````csharp
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sv
{
    /// <summary>
    /// Reusable native buffer holding a saved state (<see cref="Board.SaveState"/>,
    /// <see cref="InputState.SaveState"/>, <see cref="EventStreams.SaveState"/>). Grows on demand and
    /// never shrinks, so warm save/load is managed-allocation-free. Owned by the caller.
    /// </summary>
    public sealed unsafe class SvState : IDisposable
    {
        byte* data;

        int capacity;

        int length;

        int cursor;

        bool disposed;

        public int Length => length;

        public ReadOnlySpan<byte> Bytes => new(data, length);

        internal void BeginWrite()
        {
            length = 0;
        }

        internal void BeginRead()
        {
            cursor = 0;
        }

        internal void EndRead()
        {
            if (cursor != length)
            {
                throw new InvalidDataException($"sv: state has {length - cursor} unread bytes — shape mismatch");
            }
        }

        internal void Write(void* source, long bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            var need = length + bytes;
            if (need > int.MaxValue)
            {
                throw new InvalidOperationException("sv: state exceeds 2 GiB");
            }

            if (need > capacity)
            {
                var next = Math.Max(capacity * 2L, Math.Max(need, 4096));
                capacity = (int)Math.Min(next, int.MaxValue);
                data = data == null ? (byte*)Memory.Alloc((nuint)capacity) : (byte*)Memory.Realloc(data, (nuint)capacity);
            }

            Buffer.MemoryCopy(source, data + length, capacity - length, bytes);
            length += (int)bytes;
        }

        internal void Write<T>(T value) where T : unmanaged
        {
            Write(&value, sizeof(T));
        }

        internal void Read(void* destination, long bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            if (cursor + bytes > length)
            {
                throw new InvalidDataException("sv: state truncated — shape mismatch");
            }

            Buffer.MemoryCopy(data + cursor, destination, bytes, bytes);
            cursor += (int)bytes;
        }

        internal T Read<T>() where T : unmanaged
        {
            T value;
            Read(&value, sizeof(T));
            return value;
        }

        internal void Header(uint magic, long shape)
        {
            Write(magic);
            Write(shape);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (data != null)
            {
                Memory.Free(data);
            }

            disposed = true;
        }
    }
}
`````

### sv src/Sv/Column.cs

Path: `sv/src/Sv/Column.cs`  (897 bytes)

`````csharp
using System;

namespace Sv
{
    /// <summary>
    /// A (pointer, length) pair over unmanaged storage, validated at construction — the window guards
    /// validate against <see cref="Length"/>, so a kernel needs no separate capacity. A null pointer is
    /// only legal with length zero (the empty column).
    /// </summary>
    public readonly unsafe struct Column<T> where T : unmanaged
    {
        public T* Pointer { get; }

        public int Length { get; }

        public Column(T* pointer, int length)
        {
            if (length < 0 || (pointer == null && length != 0))
            {
                throw new ArgumentException($"sv: column of {length} rows cannot wrap {(pointer == null ? "null" : "non-null")} storage");
            }

            Pointer = pointer;
            Length = length;
        }

        public static Column<T> Empty => new(null, 0);
    }
}
`````

### sv src/Sv/Delta.cs

Path: `sv/src/Sv/Delta.cs`  (822 bytes)

`````csharp
using System;

namespace Sv
{
    /// <summary>
    /// What a kernel run adds to each row: <see cref="Delta(int)"/> for one value everywhere, or
    /// <see cref="Delta(int*)"/> for a column indexed by absolute slot. <see langword="default"/> is uniform zero.
    /// </summary>
    public readonly unsafe struct Delta
    {
        public int Uniform { get; }

        public int* PerRow { get; }

        Delta(int uniform, int* perRow)
        {
            Uniform = uniform;
            PerRow = perRow;
        }

        public Delta(int uniform) => this = new Delta(uniform, null);

        public Delta(int* perRow)
        {
            if (perRow == null)
            {
                throw new ArgumentNullException(nameof(perRow));
            }

            this = new Delta(0, perRow);
        }
    }
}
`````

### sv src/Sv/Memory.cs

Path: `sv/src/Sv/Memory.cs`  (3993 bytes)

`````csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    public static unsafe class Memory
    {
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(nuint bytes) => NativeMemory.Alloc(bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(nuint count, nuint size) => NativeMemory.Alloc(count, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocZeroed(nuint bytes) => NativeMemory.AllocZeroed(bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocZeroed(nuint count, nuint size) => NativeMemory.AllocZeroed(count, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Realloc(void* block, nuint bytes) => NativeMemory.Realloc(block, bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* block) => NativeMemory.Free(block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocAligned(nuint bytes, nuint alignment) => NativeMemory.AlignedAlloc(bytes, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FreeAligned(void* block) => NativeMemory.AlignedFree(block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear(void* block, nuint bytes) => NativeMemory.Clear(block, bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(void* source, void* destination, nuint bytes) => NativeMemory.Copy(source, destination, bytes);
#else
        public static void* Alloc(nuint bytes) => (void*)Marshal.AllocHGlobal((IntPtr)(long)(bytes == 0 ? 1 : bytes));

        public static void* Alloc(nuint count, nuint size) => Alloc(checked(count * size));

        public static void* AllocZeroed(nuint bytes)
        {
            var block = Alloc(bytes);
            Clear(block, bytes);
            return block;
        }

        public static void* AllocZeroed(nuint count, nuint size) => AllocZeroed(checked(count * size));

        public static void* Realloc(void* block, nuint bytes)
        {
            if (block == null)
            {
                return Alloc(bytes);
            }

            return (void*)Marshal.ReAllocHGlobal((IntPtr)block, (IntPtr)(long)(bytes == 0 ? 1 : bytes));
        }

        public static void Free(void* block)
        {
            if (block != null)
            {
                Marshal.FreeHGlobal((IntPtr)block);
            }
        }

        public static void* AllocAligned(nuint bytes, nuint alignment)
        {
            if (alignment < (nuint)sizeof(void*) || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentException("sv: alignment must be a power of two of at least pointer size", nameof(alignment));
            }

            var raw = (byte*)Alloc(checked(bytes + alignment + (nuint)sizeof(void*)));
            var aligned = (byte*)(((nuint)raw + (nuint)sizeof(void*) + alignment - 1) & ~(alignment - 1));
            ((void**)aligned)[-1] = raw;
            return aligned;
        }

        public static void FreeAligned(void* block)
        {
            if (block != null)
            {
                Free(((void**)block)[-1]);
            }
        }

        public static void Clear(void* block, nuint bytes)
        {
            var at = (byte*)block;
            while (bytes > 0)
            {
                var part = bytes > int.MaxValue ? int.MaxValue : (int)bytes;
                new Span<byte>(at, part).Clear();
                at += part;
                bytes -= (nuint)part;
            }
        }

        public static void Copy(void* source, void* destination, nuint bytes) => Buffer.MemoryCopy(source, destination, (long)bytes, (long)bytes);
#endif
    }
}
`````

### sv src/Sv/NativeArena.cs

Path: `sv/src/Sv/NativeArena.cs`  (3218 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;

namespace Sv
{
    internal sealed unsafe class NativeArena : IDisposable
    {
        private long live;

        private bool disposed;

        public long Live => live;

        public T* Alloc<T>(int count) where T : unmanaged
        {
            live += (long)count * sizeof(T);
            return (T*)Memory.Alloc((nuint)count * (nuint)sizeof(T));
        }

        public void* AllocBytes(int bytes)
        {
            live += bytes;
            return Memory.Alloc((nuint)bytes);
        }

        public void* AllocZeroedBytes(int bytes)
        {
            live += bytes;
            return Memory.AllocZeroed((nuint)bytes);
        }

        public T* AllocZeroed<T>(int count) where T : unmanaged
        {
            live += (long)count * sizeof(T);
            return (T*)Memory.AllocZeroed((nuint)count * (nuint)sizeof(T));
        }

        public T* AllocAlignedZeroed<T>(int count) where T : unmanaged
        {
            var bytes = (nuint)count * (nuint)sizeof(T);
            live += (long)bytes;
#if NET
            var alignment = bytes >= (nuint)2 * 1024 * 1024 ? (nuint)2 * 1024 * 1024 : 64;
            var block = (T*)Memory.AllocAligned(bytes, alignment);
            Memory.Clear(block, bytes);
            AdviseHugePages(block, bytes);
#else
            var block = (T*)Memory.AllocAligned(bytes, 64);
            Memory.Clear(block, bytes);
#endif
            return block;
        }

        public void FreeAligned<T>(T* old, int count) where T : unmanaged
        {
            if (old == null)
            {
                return;
            }

            live -= (long)count * sizeof(T);
            Memory.FreeAligned(old);
        }

#if NET
        [DllImport("libc", SetLastError = true)]
        static extern int madvise(void* address, nuint length, int advice);

        static void AdviseHugePages(void* block, nuint bytes)
        {
            if (OperatingSystem.IsLinux() && bytes >= (nuint)2 * 1024 * 1024)
            {
                _ = madvise(block, bytes, 14);
            }
        }
#endif

        public T* Realloc<T>(T* old, int oldCount, int newCount) where T : unmanaged
        {
            live += (long)(newCount - oldCount) * sizeof(T);
            return (T*)Memory.Realloc(old, (nuint)newCount * (nuint)sizeof(T));
        }

        public void Free<T>(T* old, int count) where T : unmanaged
        {
            if (old == null)
            {
                return;
            }

            live -= (long)count * sizeof(T);
            Memory.Free(old);
        }

        public void FreeBytes(void* old, int bytes)
        {
            if (old == null)
            {
                return;
            }

            live -= bytes;
            Memory.Free(old);
        }

        public void FreeBytesAligned(void* old, int bytes)
        {
            if (old == null)
            {
                return;
            }

            live -= bytes;
            Memory.FreeAligned(old);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }
    }
}
`````

---

## Appendix D. sv Unity adapter (packages/com.sv.unity)

### sv packages/com.sv.unity/SvHost.cs

Path: `sv/packages/com.sv.unity/SvHost.cs`  (15665 bytes)

`````csharp
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity
{
    public struct SvHostRef : IComponentData
    {
        public IntPtr Handle;
    }

    public sealed unsafe class SvHost : IDisposable
    {
        const int Line = 64;

        StatOp[] ops;
        StatOp[] scratch;
        int opCount;
        GCHandle self;
        Entity hostEntity;
        World world;

        public Board Board { get; }

        public SvLinkRegistry Links { get; }

        public int Capacity { get; }

        public int Words { get; }

        public long Tick { get; internal set; }

        public int DigestEvery { get; set; } = 8;

        public long DigestTick { get; internal set; } = -1;

        public bool Filter { get; set; } = true;

        public ulong Digest { get; internal set; }

        public ulong CellDigestValue { get; internal set; }

        public int LastApplied { get; private set; }

        public int LastExpired { get; private set; }

        public ulong LinkHash { get; }

        public long NativeBytes { get; private set; }

        internal Entity* SlotEntity;
        internal StatWrite* Writes;
        internal int WriteCapacity;
        internal int* WriteCount;
        internal sbyte* HandleOfStat;
        internal long*[] Inboxes = new long*[SvLinkRegistry.MaxIntrinsics];
        internal int* Sent;
        internal ulong*[] States = new ulong*[SvLinkRegistry.MaxIntrinsics];
        internal ulong*[] Edges = new ulong*[SvLinkRegistry.MaxIntrinsics];
        internal int* EdgeCount;
        internal SvEvent*[] EventBuffers = new SvEvent*[SvLinkRegistry.MaxIntrinsics];
        internal SvChannel* Channels;
        internal ulong* Partial;
        internal int PartialCount;
        internal NativeList<Entity> PendingEntities;
        internal int* PendingSlots;
        internal int* PendingIndex;
        internal NativeList<Entity> PendingRemovals;
        internal JobHandle FoldHandle;

        SvHost(Board board, SvLinkRegistry links, int writeCapacity, int opCapacity)
        {
            Board = board;
            Links = links;
            Capacity = board.Capacity;
            Words = (Capacity + 63) >> 6;
            var fault = links.Validate();
            if (fault != LinkFault.None)
            {
                throw new InvalidOperationException($"sv.unity: link table fault {fault}");
            }

            LinkHash = links.Hash();
            ops = new StatOp[opCapacity];
            scratch = new StatOp[opCapacity];
            SlotEntity = (Entity*)Alloc(Capacity * (long)sizeof(Entity));
            WriteCapacity = writeCapacity;
            Writes = (StatWrite*)Alloc(writeCapacity * (long)sizeof(StatWrite));
            WriteCount = (int*)Alloc(Line);
            HandleOfStat = (sbyte*)Alloc(SvLinkRegistry.MaxStatIndex);
            for (var i = 0; i < SvLinkRegistry.MaxStatIndex; i++)
            {
                HandleOfStat[i] = -1;
            }

            for (var s = 0; s < links.Stats.Count; s++)
            {
                HandleOfStat[links.Stats[s].Stat.Index] = (sbyte)s;
            }

            Sent = (int*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            EdgeCount = (int*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            Channels = (SvChannel*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            for (var k = 0; k < links.Intrinsics.Count; k++)
            {
                var spec = links.Intrinsics[k];
                if (spec.Remote)
                {
                    Inboxes[k] = (long*)Alloc(Capacity * 8L);
                }

                if (spec.HasCondition)
                {
                    States[k] = (ulong*)Alloc(Words * 8L);
                    Edges[k] = (ulong*)Alloc(Words * 8L);
                    EventBuffers[k] = (SvEvent*)Alloc(Math.Max(1, spec.Condition.Capacity) * (long)sizeof(SvEvent));
                }
            }

            PartialCount = JobsUtility.ThreadIndexCount * 8;
            Partial = (ulong*)Alloc(PartialCount * 8L);
            PendingEntities = new NativeList<Entity>(Capacity, Allocator.Persistent);
            NativeBytes += Capacity * (long)sizeof(Entity);
            PendingSlots = (int*)Alloc(Capacity * 4L);
            PendingIndex = (int*)Alloc(Capacity * 4L);
            PendingRemovals = new NativeList<Entity>(64, Allocator.Persistent);
        }

        public static SvHost Install(World world, Board board, SvLinkRegistry links, int writeCapacity = 1 << 16, int opCapacity = 1 << 16, int digestEvery = 8)
        {
            if (writeCapacity < 1 || opCapacity < 1 || digestEvery < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(writeCapacity), "sv.unity: writeCapacity and opCapacity must be >= 1 and digestEvery >= 0");
            }

            var manager = world.EntityManager;
            using (var existing = manager.CreateEntityQuery(ComponentType.ReadOnly<SvHostRef>()))
            {
                if (!existing.IsEmptyIgnoreFilter)
                {
                    throw new InvalidOperationException($"sv.unity: world '{world.Name}' already has an SvHost; one SvHost per world, register every intrinsic and stat in one SvLinkRegistry");
                }
            }

            var host = new SvHost(board, links, writeCapacity, opCapacity);
            host.DigestEvery = digestEvery;
            host.world = world;
            host.self = GCHandle.Alloc(host);
            host.Adopt(manager);
            host.hostEntity = manager.CreateEntity(typeof(SvHostRef));
            manager.SetComponentData(host.hostEntity, new SvHostRef { Handle = GCHandle.ToIntPtr(host.self) });
            var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystem<SvBeginSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<SvEndSystem>());
            group.SortSystems();
            return host;
        }

        internal static SvHost From(IntPtr handle) => (SvHost)GCHandle.FromIntPtr(handle).Target;

        void Adopt(EntityManager manager)
        {
            using (var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SvSlot>()))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            using (var slots = query.ToComponentDataArray<SvSlot>(Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var slot = slots[i].Slot;
                    if (slot < 0 || slot >= Capacity || (Board.LiveWord(slot >> 6) & (1ul << (slot & 63))) == 0)
                    {
                        continue;
                    }

                    if (PendingIndex[slot] != 0)
                    {
                        throw new InvalidOperationException($"sv.unity: entities {PendingEntities[PendingIndex[slot] - 1]} and {entities[i]} both carry SvSlot {slot}");
                    }

                    Queue(entities[i], slot);
                }
            }

            for (var s = 0; s < Links.Stats.Count; s++)
            {
                Board.BindSink(Links.Stats[s].Stat, true);
            }
        }

        public int Bind(Entity entity)
        {
            var slot = Board.Acquire();
            Fresh(slot);
            Queue(entity, slot);
            return slot;
        }

        public void Spawn(EntityManager manager, Entity prefab, int templateSlot, NativeArray<Entity> spawned)
        {
            manager.Instantiate(prefab, spawned);
            var at = PendingEntities.Length;
            Board.Spawn(templateSlot, spawned.Length, PendingSlots + at);
            for (var i = 0; i < spawned.Length; i++)
            {
                var slot = PendingSlots[at + i];
                Fresh(slot);
                Queue(spawned[i], slot);
            }
        }

        public void Unbind(int slot)
        {
            var entity = SlotEntity[slot];
            if (PendingIndex[slot] != 0)
            {
                entity = PendingEntities[PendingIndex[slot] - 1];
                Dequeue(slot);
            }

            Free(slot);
            if (entity != Entity.Null)
            {
                PendingRemovals.Add(entity);
            }
        }

        public void Enqueue(in StatOp op)
        {
            if (opCount == ops.Length)
            {
                Array.Resize(ref ops, ops.Length * 2);
                scratch = new StatOp[ops.Length];
            }

            ops[opCount++] = op;
        }

        public int QueuedOps => opCount;

        public int OpCapacity => ops.Length;

        public SvInboxWriter InboxWriter(int intrinsic)
        {
            if (Inboxes[intrinsic] == null)
            {
                throw new InvalidOperationException($"sv.unity: intrinsic {intrinsic} has no inbox; register it with remote: true");
            }

            return new SvInboxWriter { Cells = Inboxes[intrinsic], Sent = Sent + intrinsic * (Line / 4) };
        }

        public long InboxCell(int intrinsic, int slot) => Inboxes[intrinsic] == null ? 0L : Inboxes[intrinsic][slot];

        public SvChannel Channel(int intrinsic) => *ChannelPtr(intrinsic);

        public ReadOnlySpan<SvEvent> Events(int intrinsic)
        {
            var channel = Channel(intrinsic);
            return new ReadOnlySpan<SvEvent>(EventBuffers[intrinsic], channel.Delivered);
        }

        public bool EdgeBit(int intrinsic, int slot) => Edges[intrinsic] != null && (Edges[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public bool ConditionBit(int intrinsic, int slot) => States[intrinsic] != null && (States[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public ReadOnlySpan<ulong> EdgeWords(int intrinsic) => Edges[intrinsic] == null ? default : new ReadOnlySpan<ulong>(Edges[intrinsic], Words);

        public ReadOnlySpan<ulong> ConditionWords(int intrinsic) => States[intrinsic] == null ? default : new ReadOnlySpan<ulong>(States[intrinsic], Words);

        public Entity EntityOf(int slot) => SlotEntity[slot];

        public bool IsPending(int slot) => PendingIndex[slot] != 0;

        internal SvChannel* ChannelPtr(int intrinsic) => (SvChannel*)((byte*)Channels + intrinsic * Line);

        internal int* SentPtr(int intrinsic) => Inboxes[intrinsic] == null ? null : Sent + intrinsic * (Line / 4);

        internal int* EdgeCountPtr(int intrinsic) => EdgeCount + intrinsic * (Line / 4);

        public long ExpireTicks { get; private set; }

        public long ApplyTicks { get; private set; }

        public long RecalcTicks { get; private set; }

        public long TakeTicks { get; private set; }

        internal void Sparse()
        {
            var t0 = SvClock.Now;
            LastExpired = Board.ExpireModifiers(Tick);
            var t1 = SvClock.Now;
            if (opCount > 0)
            {
                StatOps.Canonicalize(new Span<StatOp>(ops, 0, opCount), new Span<StatOp>(scratch, 0, opCount));
                LastApplied = Board.Apply(new ReadOnlySpan<StatOp>(ops, 0, opCount));
                opCount = 0;
            }
            else
            {
                LastApplied = 0;
            }

            var t2 = SvClock.Now;
            Board.Recalculate();
            var t3 = SvClock.Now;
            ExpireTicks = t1 - t0;
            ApplyTicks = t2 - t1;
            RecalcTicks = t3 - t2;
            TakeTicks = 0;
        }

        internal int TakeWrites()
        {
            var t0 = SvClock.Now;
            *WriteCount = Board.TakeBroadcast(new Span<StatWrite>(Writes, WriteCapacity));
            TakeTicks += SvClock.Now - t0;
            return *WriteCount;
        }

        internal void ClearEdges()
        {
            for (var k = 0; k < Links.Intrinsics.Count; k++)
            {
                if (Edges[k] != null)
                {
                    UnsafeUtility.MemClear(Edges[k], Words * 8L);
                }

                *EdgeCountPtr(k) = 0;
            }
        }

        internal void Free(int slot)
        {
            Board.Release(slot);
            SlotEntity[slot] = Entity.Null;
            Fresh(slot);
        }

        internal void DropPending(int index)
        {
            var slot = PendingSlots[index];
            Dequeue(slot);
            Free(slot);
        }

        void Queue(Entity entity, int slot)
        {
            PendingIndex[slot] = PendingEntities.Length + 1;
            PendingSlots[PendingEntities.Length] = slot;
            PendingEntities.AddNoResize(entity);
        }

        void Dequeue(int slot)
        {
            var at = PendingIndex[slot] - 1;
            var last = PendingEntities.Length - 1;
            if (at != last)
            {
                var moved = PendingSlots[last];
                PendingSlots[at] = moved;
                PendingEntities[at] = PendingEntities[last];
                PendingIndex[moved] = at + 1;
            }

            PendingEntities.RemoveAt(last);
            PendingIndex[slot] = 0;
        }

        internal void ClearPending()
        {
            for (var i = 0; i < PendingEntities.Length; i++)
            {
                PendingIndex[PendingSlots[i]] = 0;
            }

            PendingEntities.Clear();
        }

        void Fresh(int slot)
        {
            for (var k = 0; k < Links.Intrinsics.Count; k++)
            {
                if (Inboxes[k] != null)
                {
                    Inboxes[k][slot] = 0;
                }

                if (States[k] != null)
                {
                    States[k][slot >> 6] &= ~(1ul << (slot & 63));
                }
            }
        }

        void* Alloc(long bytes)
        {
            NativeBytes += bytes;
            var p = UnsafeUtility.Malloc(bytes, Line, Allocator.Persistent);
            UnsafeUtility.MemClear(p, bytes);
            return p;
        }

        static void Free<T>(ref T* p) where T : unmanaged
        {
            if (p != null)
            {
                UnsafeUtility.Free(p, Allocator.Persistent);
                p = null;
            }
        }

        public void Dispose()
        {
            if (!self.IsAllocated)
            {
                return;
            }

            FoldHandle.Complete();
            if (world != null && world.IsCreated)
            {
                world.EntityManager.CompleteAllTrackedJobs();
            }

            if (world != null && world.IsCreated && world.EntityManager.Exists(hostEntity))
            {
                world.EntityManager.DestroyEntity(hostEntity);
            }

            Free(ref SlotEntity);
            Free(ref Writes);
            Free(ref WriteCount);
            Free(ref HandleOfStat);
            Free(ref Sent);
            Free(ref EdgeCount);
            Free(ref Channels);
            Free(ref Partial);
            Free(ref PendingSlots);
            Free(ref PendingIndex);
            for (var k = 0; k < SvLinkRegistry.MaxIntrinsics; k++)
            {
                Free(ref Inboxes[k]);
                Free(ref States[k]);
                Free(ref Edges[k]);
                Free(ref EventBuffers[k]);
            }

            if (PendingEntities.IsCreated)
            {
                PendingEntities.Dispose();
            }

            if (PendingRemovals.IsCreated)
            {
                PendingRemovals.Dispose();
            }

            self.Free();
        }
    }
}
`````

### sv packages/com.sv.unity/SvJobs.cs

Path: `sv/packages/com.sv.unity/SvJobs.cs`  (18670 bytes)

`````csharp
using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Sv.Unity
{
    public static class SvProbe
    {
        public static int ManagedRuns;

        [BurstDiscard]
        public static void Mark()
        {
            ManagedRuns++;
        }
    }

    public struct SvEvent
    {
        public int Slot;
        public byte Entered;
    }

    public struct SvChannel
    {
        public int Count;
        public int Delivered;
        public byte Dense;
    }

    public unsafe struct SvInboxWriter
    {
        [NativeDisableUnsafePtrRestriction] public long* Cells;
        [NativeDisableUnsafePtrRestriction] public int* Sent;

        public bool IsCreated => Cells != null;

        public void Send(int targetSlot, int amount)
        {
            if (*Sent == 0)
            {
                *Sent = 1;
            }

            Inbox.Send(Cells, targetSlot, amount);
        }
    }

    public unsafe struct SvLane
    {
        public DynamicComponentTypeHandle Value;
        public DynamicComponentTypeHandle Delta;
        public DynamicComponentTypeHandle Ceiling;
        public DynamicComponentTypeHandle Floor;
        public DynamicComponentTypeHandle Gate;
        public int Column;
        public int CeilingConstant;
        public int FloorConstant;
        public bool HasCeiling;
        public bool HasFloor;
        public bool HasCondition;
        public bool HasGate;
        public int EnterNumerator;
        public int EnterDenominator;
        public int StayNumerator;
        public int StayDenominator;
        [NativeDisableUnsafePtrRestriction] public long* Inbox;
        [NativeDisableUnsafePtrRestriction] public int* Sent;
        [NativeDisableUnsafePtrRestriction] public ulong* State;
        [NativeDisableUnsafePtrRestriction] public ulong* Edges;
        [NativeDisableUnsafePtrRestriction] public int* EdgeCount;
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct SvScatterJob : IJob
    {
        public DynamicComponentTypeHandle S0;
        public DynamicComponentTypeHandle S1;
        public DynamicComponentTypeHandle S2;
        public DynamicComponentTypeHandle S3;
        public DynamicComponentTypeHandle S4;
        public DynamicComponentTypeHandle S5;
        public DynamicComponentTypeHandle S6;
        public DynamicComponentTypeHandle S7;
        [ReadOnly] public EntityStorageInfoLookup Storage;
        [NativeDisableUnsafePtrRestriction] public StatWrite* Writes;
        [NativeDisableUnsafePtrRestriction] public int* Count;
        [NativeDisableUnsafePtrRestriction] public Entity* SlotEntity;
        [NativeDisableUnsafePtrRestriction] public sbyte* HandleOfStat;

        public void Execute()
        {
            SvProbe.Mark();
            var count = *Count;
            for (var i = 0; i < count; i++)
            {
                var write = Writes[i];
                var entity = SlotEntity[write.Slot];
                if (entity == Entity.Null)
                {
                    continue;
                }

                var info = Storage[entity];
                switch (HandleOfStat[write.Stat])
                {
                    case 0: Store(ref S0, info, write.Bits); break;
                    case 1: Store(ref S1, info, write.Bits); break;
                    case 2: Store(ref S2, info, write.Bits); break;
                    case 3: Store(ref S3, info, write.Bits); break;
                    case 4: Store(ref S4, info, write.Bits); break;
                    case 5: Store(ref S5, info, write.Bits); break;
                    case 6: Store(ref S6, info, write.Bits); break;
                    case 7: Store(ref S7, info, write.Bits); break;
                }
            }
        }

        static void Store(ref DynamicComponentTypeHandle handle, EntityStorageInfo info, uint bits)
        {
            if (!info.Chunk.Has(ref handle))
            {
                return;
            }

            var column = (uint*)NativeArrayUnsafeUtility.GetUnsafePtr(info.Chunk.GetDynamicComponentDataArrayReinterpret<uint>(ref handle, 4));
            column[info.IndexInChunk] = bits;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct SvFoldJob : IJobChunk
    {
        public SvLane L0;
        public SvLane L1;
        public SvLane L2;
        public SvLane L3;
        public int Lanes;
        public bool Filter;
        public uint LastVersion;
        [ReadOnly] public ComponentTypeHandle<SvSlot> Slot;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SvProbe.Mark();
            var visit0 = Lanes > 0 && Due(ref L0, chunk);
            var visit1 = Lanes > 1 && Due(ref L1, chunk);
            var visit2 = Lanes > 2 && Due(ref L2, chunk);
            var visit3 = Lanes > 3 && Due(ref L3, chunk);
            if (!visit0 && !visit1 && !visit2 && !visit3)
            {
                return;
            }

            var slots = (int*)chunk.GetRequiredComponentDataPtrRO(ref Slot);
            var rebound = chunk.DidChange(ref Slot, LastVersion);
            if (visit0) Run(ref L0, chunk, slots, Every(ref L0, chunk, rebound));
            if (visit1) Run(ref L1, chunk, slots, Every(ref L1, chunk, rebound));
            if (visit2) Run(ref L2, chunk, slots, Every(ref L2, chunk, rebound));
            if (visit3) Run(ref L3, chunk, slots, Every(ref L3, chunk, rebound));
        }

        bool Every(ref SvLane lane, in ArchetypeChunk chunk, bool rebound)
        {
            return !Filter || rebound || (lane.HasCeiling && chunk.DidChange(ref lane.Ceiling, LastVersion));
        }

        bool Due(ref SvLane lane, in ArchetypeChunk chunk)
        {
            if (!chunk.Has(ref lane.Value) || !chunk.Has(ref lane.Delta))
            {
                return false;
            }

            if (!Filter || (lane.Sent != null && *lane.Sent != 0))
            {
                return true;
            }

            return chunk.DidChange(ref lane.Delta, LastVersion)
                || chunk.DidChange(ref lane.Value, LastVersion)
                || chunk.DidChange(ref Slot, LastVersion)
                || (lane.HasCeiling && chunk.DidChange(ref lane.Ceiling, LastVersion))
                || (lane.HasFloor && chunk.DidChange(ref lane.Floor, LastVersion));
        }

        static void Run(ref SvLane lane, in ArchetypeChunk chunk, int* slots, bool every)
        {
            var rows = chunk.Count;
            var word = new FoldWord
            {
                Value = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref lane.Value, 4)),
                Delta = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref lane.Delta, 4)),
                Ceiling = lane.HasCeiling && chunk.Has(ref lane.Ceiling) ? (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<float>(ref lane.Ceiling, 4)) : null,
                Floor = lane.HasFloor && chunk.Has(ref lane.Floor) ? (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<float>(ref lane.Floor, 4)) : null,
                CeilingConstant = lane.CeilingConstant,
                FloorConstant = lane.FloorConstant,
            };
            if (lane.Inbox != null && *lane.Sent != 0)
            {
                for (var row = 0; row < rows; row++)
                {
                    word.Delta[row] = Inbox.Take(lane.Inbox, slots[row], word.Delta[row]);
                }
            }

            var changedLow = 0ul;
            var changedHigh = 0ul;
            if (word.Floor == null && word.Ceiling != null)
            {
                changedLow = FoldCeiling(word.Value, word.Delta, word.Ceiling, word.FloorConstant, rows < 64 ? rows : 64);
                changedHigh = rows > 64 ? FoldCeiling(word.Value + 64, word.Delta + 64, word.Ceiling + 64, word.FloorConstant, rows - 64) : 0ul;
            }
            else if (word.Floor == null && word.Ceiling == null)
            {
                changedLow = FoldConstant(word.Value, word.Delta, word.FloorConstant, word.CeilingConstant, rows < 64 ? rows : 64);
                changedHigh = rows > 64 ? FoldConstant(word.Value + 64, word.Delta + 64, word.FloorConstant, word.CeilingConstant, rows - 64) : 0ul;
            }
            else
            {
                changedLow = Fold.Word(in word, rows < 64 ? rows : 64);
                if (rows > 64)
                {
                    var high = word;
                    high.Value += 64;
                    high.Delta += 64;
                    high.Ceiling = word.Ceiling == null ? null : word.Ceiling + 64;
                    high.Floor = word.Floor == null ? null : word.Floor + 64;
                    changedHigh = Fold.Word(in high, rows - 64);
                }
            }

            if (lane.HasCondition)
            {
                if (every)
                {
                    changedLow = rows >= 64 ? ulong.MaxValue : (1ul << rows) - 1ul;
                    changedHigh = rows > 64 ? (rows >= 128 ? ulong.MaxValue : (1ul << (rows - 64)) - 1ul) : 0ul;
                }

                Conditions(ref lane, chunk, slots, in word, changedLow, 0);
                Conditions(ref lane, chunk, slots, in word, changedHigh, 64);
            }
        }

        static ulong FoldCeiling(int* value, int* delta, float* ceiling, int floor, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var before = value[row];
                var after = IntrinsicLaw.Step(before, delta[row], floor, Clamp.BoundOf(ceiling[row]));
                value[row] = after;
                delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

        static ulong FoldConstant(int* value, int* delta, int floor, int ceiling, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var before = value[row];
                var after = IntrinsicLaw.Step(before, delta[row], floor, ceiling);
                value[row] = after;
                delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

        static void Conditions(ref SvLane lane, in ArchetypeChunk chunk, int* slots, in FoldWord word, ulong rowsToCheck, int offset)
        {
            if (rowsToCheck == 0)
            {
                return;
            }

            var edges = 0;
            var gate = lane.HasGate && chunk.Has(ref lane.Gate);
            while (rowsToCheck != 0)
            {
                var row = offset + math.tzcnt(rowsToCheck);
                rowsToCheck &= rowsToCheck - 1;
                var whole = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var value = word.Value[row];
                var enter = Condition.Below(value, whole, lane.EnterNumerator, lane.EnterDenominator) ? 1ul : 0ul;
                var stay = Condition.Below(value, whole, lane.StayNumerator, lane.StayDenominator) ? 1ul : 0ul;
                var slot = slots[row];
                var bit = 1ul << (slot & 63);
                var state = (lane.State[slot >> 6] & bit) != 0 ? 1ul : 0ul;
                var next = Condition.Next(state, enter, stay);
                if (Condition.Edges(state, next) == 0)
                {
                    continue;
                }

                Flip(lane.State + (slot >> 6), bit, next != 0);
                Flip(lane.Edges + (slot >> 6), bit, true);
                if (gate)
                {
                    chunk.SetComponentEnabled(ref lane.Gate, row, next != 0);
                }

                edges++;
            }

            if (edges != 0)
            {
                Interlocked.Add(ref *lane.EdgeCount, edges);
            }
        }

        static void Flip(ulong* cell, ulong bit, bool on)
        {
            while (true)
            {
                var old = *cell;
                var next = on ? old | bit : old & ~bit;
                if (Interlocked.CompareExchange(ref *(long*)cell, (long)next, (long)old) == (long)old)
                {
                    return;
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct SvLaneResetJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public int* Sent0;
        [NativeDisableUnsafePtrRestriction] public int* Sent1;
        [NativeDisableUnsafePtrRestriction] public int* Sent2;
        [NativeDisableUnsafePtrRestriction] public int* Sent3;

        public void Execute()
        {
            SvProbe.Mark();
            if (Sent0 != null) *Sent0 = 0;
            if (Sent1 != null) *Sent1 = 0;
            if (Sent2 != null) *Sent2 = 0;
            if (Sent3 != null) *Sent3 = 0;
        }
    }

    [BurstCompile]
    public unsafe struct SvDeriveJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public ulong* Edges;
        [NativeDisableUnsafePtrRestriction] public ulong* State;
        [NativeDisableUnsafePtrRestriction] public int* EdgeCount;
        [NativeDisableUnsafePtrRestriction] public SvEvent* Events;
        [NativeDisableUnsafePtrRestriction] public SvChannel* Channel;
        public int Capacity;
        public int Words;

        public void Execute()
        {
            SvProbe.Mark();
            var count = *EdgeCount;
            Channel->Count = count;
            Channel->Delivered = 0;
            Channel->Dense = (byte)(count > Capacity ? 1 : 0);
            if (count == 0 || count > Capacity)
            {
                return;
            }

            var written = 0;
            for (var w = 0; w < Words; w++)
            {
                var bits = Edges[w];
                while (bits != 0)
                {
                    var slot = (w << 6) + Bits.Tzcnt(bits);
                    bits &= bits - 1;
                    Events[written++] = new SvEvent { Slot = slot, Entered = (byte)((State[slot >> 6] >> (slot & 63)) & 1ul) };
                }
            }

            Channel->Delivered = written;
        }
    }

    [BurstCompile]
    public unsafe struct SvDigestJob : IJobChunk
    {
        public SvLane L0;
        public SvLane L1;
        public SvLane L2;
        public SvLane L3;
        public int Lanes;
        [ReadOnly] public ComponentTypeHandle<SvSlot> Slot;
        [NativeDisableUnsafePtrRestriction] public ulong* Partial;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SvProbe.Mark();
            var slots = (int*)chunk.GetRequiredComponentDataPtrRO(ref Slot);
            var sum = 0ul;
            if (Lanes > 0) sum += Lane(ref L0, chunk, slots);
            if (Lanes > 1) sum += Lane(ref L1, chunk, slots);
            if (Lanes > 2) sum += Lane(ref L2, chunk, slots);
            if (Lanes > 3) sum += Lane(ref L3, chunk, slots);
            Partial[JobsUtility.ThreadIndex * 8] += sum;
        }

        static ulong Lane(ref SvLane lane, in ArchetypeChunk chunk, int* slots)
        {
            if (!chunk.Has(ref lane.Value))
            {
                return 0ul;
            }

            var values = (uint*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<uint>(ref lane.Value, 4));
            var sum = 0ul;
            var rows = chunk.Count;
            unchecked
            {
                for (var row = 0; row < rows; row++)
                {
                    sum += CellDigest.Key(lane.Column, slots[row], values[row]);
                }
            }

            return sum;
        }
    }

    [BurstCompile]
    public unsafe struct SvSlotWriteJob : IJob
    {
        public ComponentLookup<SvSlot> Slots;
        public ComponentLookup<SvSlotCleanup> Cleanups;
        [ReadOnly] public NativeArray<Entity> Entities;
        [NativeDisableUnsafePtrRestriction] public int* Assigned;
        [NativeDisableUnsafePtrRestriction] public Entity* SlotEntity;

        public void Execute()
        {
            SvProbe.Mark();
            for (var i = 0; i < Entities.Length; i++)
            {
                var slot = Assigned[i];
                Slots[Entities[i]] = new SvSlot { Slot = slot };
                Cleanups[Entities[i]] = new SvSlotCleanup { Slot = slot };
                SlotEntity[slot] = Entities[i];
            }
        }
    }

    [BurstCompile]
    public unsafe struct SvSendJob : IJobChunk
    {
        public DynamicComponentTypeHandle Effect;
        public DynamicComponentTypeHandle Target;
        public SvInboxWriter Inbox;
        public int Capacity;
        public uint LastVersion;
        public bool Filter;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SvProbe.Mark();
            if (!chunk.Has(ref Effect) || !chunk.Has(ref Target))
            {
                return;
            }

            if (Filter && !chunk.DidChange(ref Effect, LastVersion))
            {
                return;
            }

            var effects = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref Effect, 4));
            var rows = chunk.Count;
            var any = 0;
            for (var row = 0; row < rows; row++)
            {
                any |= effects[row];
            }

            if (any == 0)
            {
                return;
            }

            var targets = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref Target, 4));
            for (var row = 0; row < rows; row++)
            {
                var amount = effects[row];
                if (amount == 0)
                {
                    continue;
                }

                effects[row] = 0;
                var target = targets[row];
                if ((uint)target < (uint)Capacity)
                {
                    Inbox.Send(target, amount);
                }
            }
        }
    }
}
`````

### sv packages/com.sv.unity/SvLinkRegistry.cs

Path: `sv/packages/com.sv.unity/SvLinkRegistry.cs`  (8088 bytes)

`````csharp
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity
{
    public struct SvBound
    {
        public ComponentType Stat;
        public int Constant;
        public bool FromStat;

        public static SvBound Of(int constant) => new SvBound { Constant = constant };

        public static SvBound OfStat<T>() where T : unmanaged, IComponentData => new SvBound { Stat = ComponentType.ReadOnly<T>(), FromStat = true };
    }

    public struct SvConditionSpec
    {
        public ComponentType Gate;
        public bool HasGate;
        public int EnterNumerator;
        public int EnterDenominator;
        public int StayNumerator;
        public int StayDenominator;
        public int Capacity;
    }

    public sealed class SvIntrinsicSpec
    {
        public ushort Column;
        public ushort DeltaColumn;
        public ComponentType Value;
        public ComponentType Delta;
        public SvBound Floor;
        public SvBound Ceiling;
        public bool Remote;
        public bool HasCondition;
        public SvConditionSpec Condition;
    }

    public sealed class SvStatSpec
    {
        public ushort Column;
        public StatId Stat;
        public ComponentType Type;
    }

    public sealed class SvSendSpec
    {
        public int Intrinsic;
        public ComponentType Effect;
        public ComponentType Target;
    }

    public sealed class SvLinkRegistry
    {
        public const int MaxIntrinsics = 4;
        public const int MaxStats = 8;
        public const int MaxStatIndex = 64;
        public const int MaxSends = 4;

        readonly List<ColumnDef> columns = new List<ColumnDef>();
        readonly List<Link> links = new List<Link>();
        readonly Dictionary<TypeIndex, ushort> columnOf = new Dictionary<TypeIndex, ushort>();
        readonly List<SvIntrinsicSpec> intrinsics = new List<SvIntrinsicSpec>();
        readonly List<SvStatSpec> stats = new List<SvStatSpec>();
        readonly List<SvSendSpec> sends = new List<SvSendSpec>();

        public IReadOnlyList<SvIntrinsicSpec> Intrinsics => intrinsics;

        public IReadOnlyList<SvStatSpec> Stats => stats;

        public IReadOnlyList<SvSendSpec> Sends => sends;

        public ReadOnlySpan<ColumnDef> Columns => columns.ToArray();

        public ReadOnlySpan<Link> Links => links.ToArray();

        public ushort Stat<T>(StatId stat) where T : unmanaged, IComponentData
        {
            Require4<T>();
            if (stats.Count == MaxStats)
            {
                throw new InvalidOperationException($"sv.unity: more than {MaxStats} stat columns");
            }

            if (stat.Index >= MaxStatIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(stat), $"sv.unity: stat index {stat.Index} must be below {MaxStatIndex}");
            }

            var column = Add(ComponentType.ReadWrite<T>(), ColumnRole.StatOutput, (ushort)stat.Index, 0);
            stats.Add(new SvStatSpec { Column = column, Stat = stat, Type = ComponentType.ReadWrite<T>() });
            return column;
        }

        public int Intrinsic<TValue, TDelta>(IntrinsicId intrinsicId, SvBound floor, SvBound ceiling, bool remote = false)
            where TValue : unmanaged, IComponentData
            where TDelta : unmanaged, IComponentData
        {
            Require4<TValue>();
            Require4<TDelta>();
            if (intrinsics.Count == MaxIntrinsics)
            {
                throw new InvalidOperationException($"sv.unity: more than {MaxIntrinsics} dense intrinsics");
            }

            var value = Add(ComponentType.ReadWrite<TValue>(), ColumnRole.Intrinsic, (ushort)intrinsicId.Index, 0);
            var delta = Add(ComponentType.ReadWrite<TDelta>(), ColumnRole.Delta, (ushort)intrinsicId.Index, value);
            if (remote)
            {
                columns.Add(new ColumnDef(NameOf(typeof(TDelta)) ^ 0x1B0Bul, ColumnRole.Inbox, (ushort)intrinsicId.Index, value));
            }

            intrinsics.Add(new SvIntrinsicSpec { Column = value, DeltaColumn = delta, Value = ComponentType.ReadWrite<TValue>(), Delta = ComponentType.ReadWrite<TDelta>(), Floor = floor, Ceiling = ceiling, Remote = remote });
            return intrinsics.Count - 1;
        }

        public ushort Condition<TGate>(int intrinsic, int enterNumerator, int enterDenominator, int stayNumerator, int stayDenominator, int capacity = EventDerive.DefaultCapacity)
            where TGate : unmanaged, IComponentData, IEnableableComponent
        {
            var spec = intrinsics[intrinsic];
            if (enterDenominator <= 0 || stayDenominator <= 0 || capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(enterDenominator), "sv.unity: condition fractions need positive denominators and a capacity >= 0");
            }

            spec.HasCondition = true;
            spec.Condition = new SvConditionSpec { Gate = ComponentType.ReadWrite<TGate>(), HasGate = true, EnterNumerator = enterNumerator, EnterDenominator = enterDenominator, StayNumerator = stayNumerator, StayDenominator = stayDenominator, Capacity = capacity };
            return Add(ComponentType.ReadWrite<TGate>(), ColumnRole.Condition, spec.Column, spec.Column);
        }

        public void Send<TEffect, TTarget>(int intrinsic)
            where TEffect : unmanaged, IComponentData
            where TTarget : unmanaged, IComponentData
        {
            Require4<TEffect>();
            Require4<TTarget>();
            if (intrinsic < 0 || intrinsic >= intrinsics.Count || !intrinsics[intrinsic].Remote)
            {
                throw new ArgumentException($"sv.unity: send pass {typeof(TEffect).FullName} needs an intrinsic registered with remote: true");
            }

            if (sends.Count == MaxSends)
            {
                throw new InvalidOperationException($"sv.unity: more than {MaxSends} send passes");
            }

            sends.Add(new SvSendSpec { Intrinsic = intrinsic, Effect = ComponentType.ReadWrite<TEffect>(), Target = ComponentType.ReadOnly<TTarget>() });
        }

        public void Link<TColumn>(ushort kind, LinkMode mode, LinkScope scope = LinkScope.Self) where TColumn : unmanaged, IComponentData
        {
            if (!columnOf.TryGetValue(ComponentType.ReadWrite<TColumn>().TypeIndex, out var column))
            {
                throw new ArgumentException($"sv.unity: {typeof(TColumn).FullName} is not a registered sv column");
            }

            links.Add(new Link(kind, column, mode, scope));
        }

        public LinkFault Validate() => Sv.Links.Validate(links.ToArray(), columns.ToArray());

        public ulong Hash() => Sv.Links.Hash(links.ToArray(), columns.ToArray());

        public bool TryColumn(ComponentType type, out ushort column) => columnOf.TryGetValue(type.TypeIndex, out column);

        ushort Add(ComponentType type, ColumnRole role, ushort schema, ushort consumer)
        {
            if (columnOf.ContainsKey(type.TypeIndex))
            {
                throw new ArgumentException($"sv.unity: {TypeManager.GetType(type.TypeIndex).FullName} is registered twice");
            }

            var column = (ushort)columns.Count;
            columns.Add(new ColumnDef(NameOf(TypeManager.GetType(type.TypeIndex)), role, schema, consumer));
            columnOf.Add(type.TypeIndex, column);
            return column;
        }

        static void Require4<T>() where T : unmanaged
        {
            if (UnsafeUtility.SizeOf<T>() != 4)
            {
                throw new ArgumentException($"sv.unity: column {typeof(T).FullName} must be one 4-byte field");
            }
        }

        static ulong NameOf(Type type)
        {
            var name = type.FullName ?? type.Name;
            var hash = 0xCBF29CE484222325ul;
            for (var i = 0; i < name.Length; i++)
            {
                hash = (hash ^ name[i]) * 0x100000001B3ul;
            }

            return hash;
        }
    }
}
`````

---

## Appendix E. tl and tl.unity

### tl README.md

Path: `tl/README.md`  (23525 bytes)

`````
# tl

**tl** compiles designer-authored timelines into deterministic execution — data first, an authoring step that bakes it, a game system that plays it. tl contains no game behaviour:

| Who | Does | Writes code |
|---|---|---|
| Designer | Builds timelines: places clips on tracks, sets values, loop, next, channel | Never |
| Toolbox programmer | Writes each clip type once: payload struct + consumer code | Once per clip type, never per timeline |
| Gameplay programmer | Decides when an entity starts a timeline; declares systems listing which entity data clip code may touch | A little |
| Tl | Bakes timelines, stores each once, plays them on many entities, calls clip code at the right ticks | n/a |

A timeline never lives on an entity. All timelines live once in the shared library the bootstrap loads. What an entity stores is a short list of **playbacks**: `{timeline, tick}` pairs. Jump id 2 vs slide id 5 just means "manifest index 2 vs 5".

[![ci](https://github.com/IAFahim/tl/actions/workflows/ci.yml/badge.svg)](https://github.com/IAFahim/tl/actions/workflows/ci.yml)

## Install for Unity

In Unity the package is enough: newest Unity 7, a **Universal 3D** (URP) project, Package Manager > **+** > **Install package from git URL** >
`https://github.com/IAFahim/tl.unity.git?path=/com.tl.unity#main`. No .NET, no tool, no JSON: designers use Unity's Timeline window. Guide:
[USING-ECS](https://github.com/IAFahim/tl.unity/blob/main/docs/USING-ECS.md).

One pasted line does all of it from a terminal: it creates a URP project on the newest Unity 7 editor (installs one with the Unity CLI if
none), adds the package, imports every demo (Force, Events, Crowd) and the Demo hub, builds their scenes and a **Tl Demos** hub scene, runs
every demo's PlayMode test and prints one line per demo with actual and expected numbers (Force x 4.0333 and back to 0, Events 15 strikes
and dummy health 10, Crowd ns/entity at 10k and 100k with 0 B), then leaves Tl Demos open. `TL_DEMOS=force,events` picks demos,
`--no-tests` (or `TL_NO_TESTS=1`) skips the test run. Needs git with access to `tl.unity` (`gh auth login`, `gh auth setup-git`; the repo
is private for now). Safe to rerun on the same project.

```powershell
$env:TL_PROJECT='C:\path\MyGame'; irm https://raw.githubusercontent.com/IAFahim/tl/main/install/unity.ps1 | iex
```

```sh
curl -sSL https://raw.githubusercontent.com/IAFahim/tl/main/install/unity.sh | bash -s -- <project-path>
```

Verified on Windows 11 (PowerShell script, fresh project). The bash script follows the same steps and has not been run on this version.

## Install

```sh
dotnet add package Tl.CSharp --version 4.0.0-preview.1
dotnet tool install --global Tl.Bake --prerelease     # the tl bake command
```

## Get started

Three structs and one JSON file — a first timeline in a minute. Put the structs and `timelines/jump.json` in a console project:

```cs
public readonly struct ArcClip { public readonly float Velocity; public ArcClip(float velocity) { Velocity = velocity; } }

public readonly struct ArcTrack : IBlend<ArcClip>
{
    public readonly float Scale;
    public ArcTrack(float scale) { Scale = scale; }
    public void Blend(in ArcClip first, in ArcClip second, float factor, out ArcClip result)
        => result = new ArcClip(first.Velocity + ((second.Velocity - first.Velocity) * factor));
}

public readonly struct Rise : ITrack<ArcTrack, ArcClip>
{
    public static void Fold(in Frame<ArcTrack, ArcClip> frame, out float lift)
        => lift = frame.Direction * frame.Clip.Velocity * frame.Track.Scale;

    public static void ExecuteActive(in Frame<ArcTrack, ArcClip> frame, in float lift, ref Motion m)
        => m.Y += lift;

    public static void OnClipEnd(in Frame<ArcTrack, ArcClip> frame)
    {
    }
}
```

```json
{
  "name": "jump", "duration": 30, "next": "run", "channel": "locomotion",
  "tracks": [
    { "namespace": "Game", "type": "ArcTrack", "data": { "Scale": 1.0 },
      "clips": [ { "namespace": "Game", "type": "ArcClip", "start": 0, "end": 15, "data": { "Velocity": 2.0 } },
                 { "namespace": "Game", "type": "ArcClip", "start": 15, "end": 30, "data": { "Velocity": -2.0 } } ] }
  ]
}
```

Build, then bake the whole library with `tl library` — it packs every timeline into one `game.tlib` and writes the stable ids to `Timelines.g.cs`:

```sh
dotnet build -c Release
tl library timelines --assembly bin/Release/net10.0/Runner.dll --out game.tlib --ids Timelines.g.cs --namespace Runner
```

Play it — systems run every step, then one `Advance` moves every clock; `Run` never moves ticks. After the full loop the arc has risen and fallen and `next` has handed the entity back to `run`. Put the loop in `Program.cs` and `dotnet run`:

```cs
using var world = new TimelineWorld(File.ReadAllBytes("game.tlib"));
using var players = new PlaybackTable(entityCapacity: 1, slotsPerEntity: 4);
var motion = new Motion[1];
var power = new JumpPower[1];
players.Start(0, Timelines.Jump);
for (var frame = 0; frame < 30; frame++)
{
    world.ApplyRequests(players);
    new Movement { Motion = motion, JumpPower = power }.Run(world, players);
    world.Advance(players);
}
```

## Run the full thing

`samples/Runner` is the whole pipeline with the code inline — data, bake, systems, output. Its `GameLogic.cs` is the shared toolbox both hosts compile (payloads, tracks and clips, consumers, the `Movement` and `Combat` systems); the console host loads `game.tlib` and steps three entities through it.

**1 · the data** — `samples/Runner/timelines/`, authored by a designer, naming the game's own C# types. `run.json` loops locomotion under the whole game; `jump.json` hands back to `run` through `next`; `slide.json` opens an `InputWindow` that requests `barrel_explode` on a second channel when the player presses Attack mid-slide; `potion_hop.json` stacks channel-free on top of anything:

```json
{"name":"slide","duration":24,"next":"run","channel":"locomotion","tracks":[
{"name":"hands","namespace":"Game","type":"InputTrack","clips":[
{"name":"window","namespace":"Game","type":"InputWindowClip","start":6,"end":14,"data":{"Button":"Attack","OnPress":"barrel_explode"}}]}]}
```

**2 · the bake** — `timelines/tl.project.json` to one canonical `game.tlib`, deterministic on every machine:

```sh
cd samples/Runner
tl library timelines --assembly bin/Release/net10.0/Runner.dll --out game.tlib --ids Timelines.g.cs --namespace Runner
```

**3 · the systems** — `samples/Runner/GameLogic.cs`. `Jump` folds the pure arc once per tick and composes it with live per-jumper power every step; `InputWindow` requests `barrel_explode` while Attack is held; `ChangeStat` spends one `JumpCounter` on clip start; `Stomp` and `Cues` raise typed events:

```cs
public readonly struct Jump : ITrack<ArcTrack, ArcClip>
{
    public static void Fold(in Frame<ArcTrack, ArcClip> frame, out float lift)
        => lift = frame.Direction * frame.Clip.Velocity * frame.Track.Scale;

    public static void ExecuteActive(in Frame<ArcTrack, ArcClip> frame, in float lift, ref Motion m, in JumpPower power)
        => m.Y += lift * power.Value;

    public static void OnClipEnd(in Frame<ArcTrack, ArcClip> frame)
    {
    }
}
```

Run it:

```sh
cd samples/Runner
dotnet build -c Release
tl library timelines --assembly bin/Release/net10.0/Runner.dll --out game.tlib --ids Timelines.g.cs --namespace Runner
dotnet build -c Release
dotnet run -c Release --no-build
dotnet run -c Release --no-build -- --verify
```

You see the hero run, jump exactly back to the takeoff height, and hand over to `run`; the slider's mid-slide button press detonates the barrel on the action channel; `--verify` proves `Jump_lands_exactly_where_it_took_off`, exact rewind, and a stable state hash across two 600-step runs.

No game behaviour lives in tl. The jump-arc math is `Jump.Fold` (toolbox, once per clip type); pressing Attack to detonate is the host setting one `InputState` cell (gameplay, your ECS); sliding into a barrel needs no slide-specific code — the designer arranged an `InputWindow` clip plus a `Cue`/`Stomp` pair the toolbox already provided.

## Data

Each timeline file is the whole input format — authored by a designer, naming the game's own C# types:

- windows are half-open `[start, end)`; execution order is authored clip order
- `data` fields map onto struct fields by name (`Scale` → `ArcTrack.Scale`); an omitted `data` on a track or clip bakes the type's default state
- an optional `name` on the timeline, each track, and each clip labels the asset for tools and reports
- two clips overlapping on one track blend through the type's `IBlend` with the authored factor; overlap on a track whose type lacks `IBlend` fails the bake
- `loop: true` wraps at `duration`; finite timelines without `next` disappear at the end; `hold: true` keeps the last tick with `Ended` instead
- `next` names the timeline that takes over the same slot and channel at tick 0 when this one ends; it is incompatible with `loop: true`
- `channel` names an exclusive lane per entity with replacement semantics; a timeline with no channel stacks with everything
- `markers` are unmanaged structs resolved at marker ticks in either direction
- enum fields take member names case-sensitively (`"Voice": "Boom"`); `TimelineId` fields take timeline names resolved against the manifest (`"OnPress": "barrel_explode"`, unknown names fail the bake)
- unknown keys are rejected; a timeline may mix track and clip types freely; steps resolve per stage, with per-step frame tables built at load
- every baked pair carries a layout fingerprint of its two structs (a hash over field names, field types, and declaration order). The generator folds the pair's field shape from source symbols; a struct edit between bake and bind fails fast with a rebake repair instead of silently misreading

One `tl.project.json` per library: `tickRate` feeds the Unity exporter (seconds to ticks; the runtime counts ticks only), `channels` order sets channel numbers (1-up, 0 = none; unknown channel fails the bake), `timelines` lists the timeline files. The manifest is the timeline names sorted ordinally; `TimelineId` is the dense id in load order (manifest order, id = index + 1, 0 = none) with `Timelines.<PascalCase>` compile-time constants.

## Authoring

`tl library` compiles the data to canonical `game.tlib` bytes — deterministic (same input, same bytes, every machine and culture), content-deduped (each identical timeline stored once), digested end to end:

```sh
tl library <dir> [--assembly Game.dll] --out game.tlib --ids Timelines.g.cs [--namespace Game] [--diagnostics json]
```

1. Bakes every timeline as today (TLB v6 with the JSON v2 fields), resolving types via `--assembly`/discovery/`--auto`, then resolves name fields in a second pass against the manifest.
2. Writes `game.tlib`: header, manifest (names/ids/channels), each timeline's TLB bytes (content-digest dedup), digest over everything. Documented in [docs/library-layout.md](docs/library-layout.md) in the style of [docs/tlb1-layout.md](docs/tlb1-layout.md) (offsets, widths, alignment, endianness scope, rejection contract, fixtures).
3. Writes `Timelines.g.cs` (`PascalCase`, collision fails bake, `const` so Burst reads them) plus `LibraryDigest`.

`tl bake tl.project.json -o game.tlib` is the same pack, but it writes `Timelines.g.cs` and `Kinds.g.cs` only with `--emit-sources <dir>` (`tl library` writes them beside the folder as above); `tl decode game.tlib -o decoded/` (or one `.tlb`) writes the exact JSON back, or fails by name when it cannot. The three JSON documents (timeline, `tl.project.json`, scene), the decode guarantees and the migration notes are in [docs/json-format.md](docs/json-format.md).

`new TimelineWorld(ReadOnlySpan<byte> library)` is the one cold step per library: it validates (rejection contract) + digest, turns TLB structures into native blocks, and builds the derived tables — per-step fold lanes (every tick both directions; pure functions of the frame), frame tables per (timeline, step, direction), an event index (sorted tick/kind/step/marker plus 1 bit per tick), and per (system, timeline, stage) step lists in authored order so dispatch costs active steps, not clip types in the game. It publishes a `LibraryView`: an unmanaged versioned pointer struct documented in [docs/library-view-abi.md](docs/library-view-abi.md) with readers for tools.

`tl --json --assembly bin/Release/net10.0/Runner.dll` lists every authorable pair in the given assemblies — the authoritative answer to "what can this JSON name?":

```json
{
  "schemaVersion": 1,
  "assemblies": ["Runner"],
  "pairs": [
    {
      "track": { "namespace": "Game", "name": "ArcTrack", "assembly": "Runner" },
      "clip": { "namespace": "Game", "name": "ArcClip", "assembly": "Runner" },
      "consumers": [{ "name": "Game.Jump", "assembly": "Runner" }]
    }
  ]
}
```

## System

The structs in the run above are the whole game side — the clip payload, the track settings with its blend, and the consumers that read entity columns plus shared context. `ITrack<TTrack, TClip>` consumers are discovered compilation-wide — no registration, no catalog. `frame.Direction` is +1 forward and −1 backward, which is why rewind is exact.

A **system** is a struct the gameplay programmer declares once. It lists entity columns plus shared context (spatial index, event writer). The generator connects each consumer to the system holding its parameters and writes the loop. A game has a handful of systems and any number of timelines and clip types:

```cs
[TimelineSystem]
public ref partial struct Movement
{
    public Span<Motion> Motion;
    public ReadOnlySpan<JumpPower> JumpPower;
    public Span<Stats> Stats;
    public ReadOnlySpan<InputState> Input;
    public Requests Requests;
    public Events<Cue> Cues;
}
```

`Span`/`ReadOnlySpan` fields are columns (one element per entity); anything else is context; all column and context types are unmanaged. One consumer belongs to exactly one system (all params covered); fitting two systems without `[TimelineSystemMember(typeof(S))]` is an error; fitting none warns and never runs. `Run` never moves ticks: any number of systems run per step, ticks move once in `Advance`.

All consumer methods are static, optional, in any combination, found by name and shape:

| Method | Called | Receives | Use |
|---|---|---|---|
| `Fold(in Frame, out R...)` | at load, every tick both directions; must be pure | frame only | authored-data numbers |
| `ExecuteActive(...)` | every step, once per active step of this pair per playback | frame **or** leading fold results, then columns, then context | per-entity effects |
| `ExecuteBatch(...)` | once per block of playbacks instead of `ExecuteActive` | gathered folds + column spans | hand SIMD |
| `OnClipStart(in Frame, ...)` | first tick of clip either direction (`Direction` says which); mid-clip start gives `Synthetic` | frame, columns, context | one-shots, stat changes |
| `OnClipEnd(in Frame, ...)` | last tick either direction; mid-clip stop/replace gives `Interrupted` | frame, columns, context | cleanup |
| `OnMarker(in MarkerFrame, ...)` on `struct X : IMarker<T>` | at marker tick either direction | marker frame, columns, context | footsteps, spawns |

Rewind is exact (tied to ticks): `ChangeStat` applies `+delta` forward and `-delta` back across the same tick. Binding: `ref/in T` column type = this entity's element; `ref/in T` context type = shared value; `ref Requests` = start/stop for this entity applied next request phase; `ref Events<T>` = raises with `EntityRef`+slot; fold results = leading `in R` in declaration order. Caps are unchanged: 10 folds, widths 1/2/4/8, 30 live columns per consumer, 40-slot row, same-type duplicate-column rejection.

A **playback** is one timeline playing on an entity: `{Timeline, Tick}` plus channel and flags. `Tick` is the next tick to play forward. Storage is a native table behind a managed owner with inline slots per entity (slot order = start order, O(1) start/stop, removal keeps order). Operations: `Start(entity, id, at = 0)` (channel replace: active clips of replaced playbacks get `OnClipEnd`+`Interrupted`; `at > 0` gives `OnClipStart`+`Synthetic` next step), `Stop`/`StopChannel`/`StopAll` (actives get `Interrupted` ends), `IsPlaying`/`Find(channel)`. Games with per-skill loadouts declare `[TimelineSlotKeys] enum Skill { Jump, Slide, Hurt }` and swap each entity's slot table (key → `TimelineId`, see `slots` in `samples/Runner/Program.cs`) so a potion swaps the jump without touching entities.

One whole step, both hosts:

```cs
using var world = new TimelineWorld(File.ReadAllBytes("game.tlib"));
var players = new PlaybackTable(entityCapacity: 4096, slotsPerEntity: 4);
players.Start(0, Timelines.Run);
void Step() {
    world.ApplyRequests(players);
    new Movement { Motion = motion, JumpPower = power, Stats = stats, Input = input, Requests = world.Requests(players), Cues = cues.Writer }.Run(world, players);
    new Combat { Position = position, Heads = heads, Hits = hits.Writer }.Run(world, players);
    world.Advance(players);
}
```

Step order: 1. Requests (last step's, then host's). 2. Systems in game order. 3. `Advance` per playback (++/--, wrap if loop, `next` handover same slot+channel at tick 0 + `StartPending`, else `hold` → `Ended` else removal; `Paused`/`Ended` don't move). 4. Events drained in (entity, slot, raise) order. Determinism: no hash-ordered containers; every tick played exactly once per step, no skipping; float order fixed by consumer order (entities row order; per entity slots in slot order; per playback steps in authored track order, reversed backward; per step consumers in ordinal consumer-name order, reversed backward; event methods after `ExecuteActive` of the same playback+tick).

Game owns column arrays plus result systems (integrate motion, apply damage, present cues). Tl requires none of that.

## Generated reports

```sh
dotnet msbuild -t:TlGenExport -p:Configuration=Release   # content-stable .g.cs snapshot + manifest
```

Checked-in systems snapshots are byte-exact emission goldens. Regenerate them
only with the commands below and check in the result; the matching tests fail
on any drift:

- `samples/Runner/Generated/TlSystemsRunner.g.cs` — regenerate via
  `Tl.Systems.Tests` `RunnerSnapshotTests` with `TL_UPDATE_SNAPSHOT=1`
  (emits the `Game.Logic` toolbox with id `Runner`; the Runner sample and
  `LoaderRunTests` consume it).
- `benchmarks/Systems/Generated/TlSystemsBench.g.cs` — regenerate via
  `Tl.Systems.Tests` `BenchGenTests` with `TL_UPDATE_BENCH=1`
  (emits the bench domain with id `Bench`; the Systems benchmarks consume it).
- `tests/Tl.Systems.Tests/LoaderLibrary/game.tlib` — rebake with
  `tl library tests/Tl.Systems.Tests/LoaderLibrary --assembly <Tl.Systems.Tests.dll> --out game.tlib`
  (the `LoaderRunTests` loader-built proof consumes it; ids are positional).

## Numbers

<!-- tl-numbers: generated by eng/refresh-numbers from benchmarks/Systems/results/numbers.json; edit the renderer, not this block -->
4.0 systems, one step per call (i9-14900K, 32 cores, .NET 10.0.12, Release; best of 5 in-process runs after per-shape steady-state warm-up (at least 0.5 s and until the best frame is flat across three rounds); hot = consecutive frames with the working set cache-resident, cold = interleaved shapes streaming from DRAM; every shape bit-exact forward and backward, 0 B allocated:

| shape | hot ns/entity | cold ns/entity | warm alloc B |
| --- | ---: | ---: | ---: |
| folded lane through a generated system | 9.42 | 9.46 | 0 |
| folded lane rewound through the same system | 9.45 | 9.50 | 0 |
| fold-fed compose through a generated system | 10.86 | 10.79 | 0 |
| frame-consumer dispatch through a generated system | 10.58 | 10.57 | 0 |
| four pairs through one generated system | 21.84 | 22.03 | 0 |
| one playback per entity (2-playback reference) | 8.88 | 8.94 | 0 |
| two playbacks per entity | 17.26 | 17.21 | 0 |
| 64 consumers, 2 active per tick | 372.47 | 372.19 | 0 |

Two playbacks per entity cost 1.94x one playback on the same run. Committed 3.x LiveBatch baselines (per-entity medians from a different harness and row count) ship in the receipt for reference; the 4.10 gates (folded ≤1.03x, fold-fed ≤0.15x, frame ≤0.25x, four-pair ≤0.70x, 64-consumer ≤1.5x, 2-playback ≤2.2x) are decided by same-harness BenchmarkDotNet runs on the release machine, not by the ratios above. Warm allocation is 0 B on every shape and determinism is verified by identical state hashes across two full runs.
<!-- /tl-numbers -->

The table is rendered from [benchmarks/Systems](benchmarks/Systems) receipts — every shape bit-exact forward and backward with 0 B warm — and CI fails if the receipt and this section disagree. Re-measure on the release machine with `eng/refresh-numbers`, which rebuilds the harness, re-runs every shape, and re-renders this block from the fingerprinted receipt. The 4.10 gates (folded ≤1.03x, fold-fed ≤0.15x, frame ≤0.25x, four-pair ≤0.70x, 64-consumer ≤1.5x, 2-playback ≤2.2x, 0 B warm, parity) run against these rows plus the committed 3.x baselines; ratios below are indicative until a same-harness release-machine run records the verdicts.

## Migrating 3.x → 4.0

4.0.0 ships one blessed playback path with no compatibility shims. Rename or remove directly; rebake every asset (`next`, `hold`, `channel`, and marker data have no 3.x spelling).

| 3.x | 4.0 |
|---|---|
| `TimelineAsset.Load/Of/Dispose`, `ushort` index, `TimelineComponent` | `new TimelineWorld(library)`, `TimelineId` consts, `Reload` |
| `Timeline<TTrack,TClip>.Apply` overloads, `ApplyChunk`, generic `Apply<TIndex,...>`, row-list and shared-clock overloads | `system.Run(world, players)` |
| `Timeline.Advance`, `Timeline<TTrack,TClip>.Advance` overloads | `world.Advance(players)` |
| `ColumnSet` caller-assembled columns | `[TimelineSystem]` struct fields |
| `Prepare`/`Play`/`Release`/`BankState` | removed (systems own the plan) |
| `IBake<T>`, `Timeline.Bake`/`BakeRef` | removed; host code plus systems cover it |
| `Timeline.SealConsumers` | removed; registry immutable after install |
| `PairRuntime` function-pointer registration | `ITlModule` (generator ABI v2) |
| `Frame<,>` ref struct holding refs | `Frame<,>` readonly struct holding values |
| `FrameFlags : byte` | `FrameFlags : ushort` + `Synthetic`/`Interrupted` |
| `TTrack : IBlend<TClip>` required | every track implements `IBlend<TClip>` (blend runs only where clips overlap) |
| `ITimelineLane<T>`, `Timeline<T>` lane facade | removed (kernels internal) |
| `Timeline.Query`, `FrameQuery`, `TickFrame` | removed (bake-time inspection via `TlbInspection`/`SystemReader`; runtime reads `LibraryView`) |
| `TimelineState`/`TimelineMovement`, `SlotView`/`LaneSegment` | internal / `LibraryView` ABI v1 |
| `TrySample`/`SampleClip`/`View` | removed (`Inspection` keeps `Tables` snapshots for bake diagnostics) |

Behaviour change to name (D10): a fold-fed consumer with several active tracks of one pair now runs once per active step with its own track's values (matching frame consumers and authored track order). 3.x ran the composed value once per row. If a timeline overlaps two tracks of one pair feeding one composed consumer, re-tune by ear and re-record the receipt.

Contributing: [AGENTS.md](AGENTS.md) · Security: [SECURITY.md](SECURITY.md) · License: [MIT](LICENSE)
`````

### tl docs/json-format.md

Path: `tl/docs/json-format.md`  (8064 bytes)

`````
# Portable JSON, schema 1

Tl data lives in three JSON documents. `tl bake` turns them into binary (`.tlb` per timeline, `game.tlib` per project); `tl decode` turns the binary back into the same JSON. Unity's `Tl > Export` writes the same timeline JSON through the same packer (`TimelineJson.Export` in `Tl.Gen.Tlb`).

Every document starts with `"schema": 1`. Any other value, a missing value where it is required, an unknown key or a duplicate key fails with a message that names the field.

## Timeline (`<name>.json`)

```json
{
  "schema": 1,
  "name": "push",
  "duration": 180,
  "channel": "body",
  "next": "rest",
  "tracks": [
    {
      "name": "force",
      "namespace": "Tl.Pack.Force", "type": "ForceTrack", "data": { "Origin": { "x": 0, "y": 0, "z": 0 } },
      "clips": [
        { "name": "shove", "namespace": "Tl.Pack.Force", "type": "ForceClip", "start": 0, "end": 60,
          "data": { "Direction": { "x": 1, "y": 0, "z": 0 }, "Falloff": [1,0.9,0.8,0.7,0.6,0.5,0.4,0.3,0.2,0.1,0,0,0,0,0,0] } }
      ]
    }
  ],
  "markers": [ { "namespace": "Tl.Pack.Force", "type": "Hit", "tick": 30, "data": { "Amount": 5 } } ]
}
```

- `schema` is optional in a timeline (a timeline without it bakes the same bytes); when present it must be `1`.
- Root keys: `name`, `duration` (ticks, at most 65,535), `loop`, `hold`, `next` (timeline name), `channel` (channel name), `tracks`, `markers`.
- Types are `namespace` (dotted, may be empty) plus `type`. A generic type spells its arguments with their namespaces: `TlNoSettings<Game.Hit>`, `IndependentTrack<System.Int32>`. No assembly name is needed: pair keys use the full type name only.
- `data` sets struct fields by name. Numbers must fit the field and be finite; `bool` is `true`/`false`; enums are member names; `TimelineId` fields are timeline names; nested structs are objects; `TlCurve` is an array of exactly 16 numbers; a `fixed` buffer is an array of exactly its length. Fields left out are zero.

## Project (`tl.project.json`)

```json
{
  "schema": 1,
  "tickRate": 60,
  "channels": ["body", "voice"],
  "timelines": ["push.json", "moves/*.json"],
  "kinds": { "Game.Damage": { "gamePulses": 4 } },
  "packs": [ { "name": "Tl.Force", "assembly": "Tl.Pack.Force", "version": "0.1.0", "layout": "<digest>" } ]
}
```

`schema` and `timelines` are required. `kinds` keys are kind full type names (or group names). Channel order sets channel numbers (1-up). Timeline globs are relative to the project file and never match the project file itself. Layout of the packed result: `docs/library-layout.md`.

## Scene (`*.scene.json`)

```json
{
  "schema": 1,
  "project": "tl.project.json",
  "entities": [ { "name": "cube", "states": [ { "namespace": "Game", "type": "Health", "data": { "Value": 10 } } ], "play": [ { "timeline": "push" } ] } ],
  "requests": [ { "step": 40, "entity": 0, "op": "Pause", "timeline": "push" } ],
  "direction": [ { "step": 100, "forward": false } ]
}
```

Each request names the fields its operation needs (`timeline` for timeline operations, `channel` for channel operations, `tick` for `Seek` and `SeekChannel`, `speed` for `SetSpeed*`, `reverse` for `SetReverse*`) and nothing else. `direction` (optional) flips the whole world: `[{ "step": 450, "forward": false }, { "step": 900, "forward": true }]`.

`tl run <scene.json> --steps N --assembly <pack or game dll>...` executes a scene in the plain .NET host (`--library game.tlib` to skip baking `project`). Step `s` (0-based) applies the world direction set for `s`, the `play` entries (step 0 only, in entity order), then that step's requests in file order, then ticks once and runs every `[TimelineSystem]` of the given assemblies in full-name order. It prints `hash=<H> steps=<N> entities=<E>`; `--checkpoints K` prints the running hash every K steps (`--show` adds every state value). `--export states.json` writes the state after every step (`--export-every N`: every N-th step) as a JSON array with one object per entity and step: `step` (1-based, the same count as a checkpoint), `entity`, `playback` (each playback slot as `timeline:tick`, space separated, in slot order, free slots as `0:0`) and one `Type.Field` key per field of every column in the scene, read after the systems ran, the same bytes the hash covers. Floats are JSON numbers (`NaN` and infinities are strings), enums are their numbers. The export does not change the printed lines or the hash. `SetSpeed` and `SetSpeedChannel` take `speed` as a multiple of 1/16 from 0.0625 to 15.9375; any other value is refused.

The state hash (`Tl.TlStateHash`, FNV-1a 64 over little-endian 32-bit words, a short tail zero-padded) is updated after every step: for each entity in scene order, its playback slot count and each 8-byte `Playback` (timeline id, tick, channel, flags, reserved), then the raw bytes of every state type any entity lists, types in full-name order (an entity without that state hashes zeros). Timeline ids are manifest ids (names in ordinal order), so any host that loads the same timelines prints the same hash.

## Commands

```sh
tl bake tl.project.json -o game.tlib --assembly Game.dll     # whole project (also: a folder)
tl bake push.json -o push.tlb --assembly Game.dll            # one timeline without channel or next
tl bake tl.project.json -o game.tlib --emit-sources Generated # also write Timelines.g.cs and Kinds.g.cs into Generated/
tl decode push.tlb -o push.json --assembly Game.dll          # one timeline; stdout without -o
tl decode game.tlib -o decoded/ --assembly Game.dll          # every timeline plus tl.project.json
tl run demo.scene.json --steps 600 --assembly Game.dll        # run a scene, print its state hash
tl run demo.scene.json --steps 600 --assembly Game.dll --export states.json --export-every 10   # every column field every 10 steps
tl inspect game.tlib jump --pulses --markers                  # tick, trigger, kind and method of each pulse; tick, type key and payload of each marker
```

Decode is exact or it fails. It rebuilds each timeline from the TLB cold metadata (`docs/tlb1-layout.md`, "Cold metadata tail"): authored track and clip order, each clip label bound to its window, track and clip type arguments, channel and next names. It writes every value through its fields, refuses a value whose bytes do not survive that (for example a struct with nonzero padding bytes, named with the byte offset), and re-bakes the JSON it wrote, failing unless the bytes are identical. A stripped TLB (`tl --strip`) has no metadata and cannot be decoded.

What `game.tlib` does not store, so `tl decode game.tlib` cannot give back: the project's `kinds` and `packs` entries (copy them from the authoring project; decode says so), channel names no timeline uses (decode fails rather than renumbering), and the original timeline file names (each timeline is written as `<name>.json`).

## Migration from the earlier format

No compatibility layer is kept.

- `library.json` is now `tl.project.json` and needs `"schema": 1`. Rename the file; the old name is not read.
- The TLB cold metadata tail is version 2 (64-byte header, track and clip tables, generic type names, channel and next labels). Tails of version 1 are rejected by every metadata reader (`tl --report`, `tl decode`, `Tl.Tlb`); playback never reads the tail, so old files still play, but rebake them to inspect or decode them. Every TLB and `game.tlib` byte fixture was re-pinned; hot sections and all state hashes are unchanged (13943614770702343869, 6607956939547305625, 12545803591333190611).
- `TlbMetadataView` gained `Tracks` and `Clips`; type rows of generic types now carry their arguments in `Name`.
- Kinds are identified by full type name everywhere (library kind rows, `tl.project.json` `kinds` keys, `KindStore.IndexOf`/`Pulses<T>`, generated systems, the Unity registry and kind ids), so a pack's `Tl.Pack.Force.ForceKind` and a game's `ForceKind` coexist (IAFahim/tl#582). Write `"Game.Damage"` where you wrote `"Damage"`. The generated `KindCapacity` constants keep the short name unless two kinds share it.
`````

### tl src/Tl.Core/CoderApi.cs

Path: `tl/src/Tl.Core/CoderApi.cs`  (3796 bytes)

`````csharp
namespace Tl;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class TlClipAttribute : Attribute
{
    public TlClipAttribute(string menu)
    {
        Menu = menu;
    }

    public string Menu { get; }
}

[AttributeUsage(AttributeTargets.Struct)]
public sealed class TlKindAttribute : Attribute
{
    public KindEntries Accepts { get; set; }
    public KindMode Mode { get; set; }
    public int Order { get; set; }
    public string? Group { get; set; }
    public bool FoldsPulses { get; set; }
    public int Inline { get; set; }
    public int GamePulses { get; set; }
}

[AttributeUsage(AttributeTargets.Struct)]
public sealed class TlStateAttribute : Attribute
{
}

public interface ITlTrack<TClip> : ITrack<TlNoSettings<TClip>, TClip>
    where TClip : unmanaged
{
}

public struct TlNoSettings<TClip> : IBlend<TClip>
    where TClip : unmanaged
{
    public void Blend(in TClip first, in TClip second, float factor, out TClip result)
    {
        result = first;
    }
}

public readonly struct TlFrame<TClip>
    where TClip : unmanaged
{
    public TlFrame(in Frame<TlNoSettings<TClip>, TClip> frame)
    {
        Clip = frame.Clip;
        Tick = frame.TimelineTick;
        ClipLength = frame.ClipLength;
        WithinClip = frame.WithinClip;
        TrackIndex = frame.TrackIndex;
        Flags = frame.Flags;
    }

    public readonly TClip Clip;
    public readonly ushort Tick;
    public readonly ushort ClipLength;
    public readonly ushort WithinClip;
    public readonly ushort TrackIndex;
    public readonly FrameFlags Flags;
    public float Progress => ClipLength == 0 ? 0f : WithinClip / (float)ClipLength;
    public int Direction => Has(FrameFlags.Reverse) ? -1 : 1;
    public bool IsBackward => Has(FrameFlags.Reverse);
    public bool Has(FrameFlags flags) => (Flags & flags) == flags;
}

public unsafe struct TlCurve
{
    public const int Samples = 16;

    fixed float _values[Samples];

    public float this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    public float Evaluate(float progress)
    {
        var x = (progress < 0f ? 0f : progress > 1f ? 1f : progress) * (Samples - 1);
        var i = (int)x;
        var j = i < Samples - 1 ? i + 1 : i;
        return _values[i] + (_values[j] - _values[i]) * (x - i);
    }

    public static TlCurve Constant(float value)
    {
        var curve = default(TlCurve);
        for (var i = 0; i < Samples; i++)
            curve._values[i] = value;
        return curve;
    }
}

[Serializable]
public struct TlAssetId : IEquatable<TlAssetId>
{
    public uint X;
    public uint Y;
    public uint Z;
    public uint W;

    public TlAssetId(uint x, uint y, uint z, uint w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public bool IsValid => (X | Y | Z | W) != 0;

    public bool Equals(TlAssetId other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

    public override bool Equals(object? obj) => obj is TlAssetId other && Equals(other);

    public override int GetHashCode() => (int)(X ^ (Y * 16777619u) ^ (Z * 2166136261u) ^ (W * 486187739u));

    public static bool operator ==(TlAssetId a, TlAssetId b) => a.Equals(b);

    public static bool operator !=(TlAssetId a, TlAssetId b) => !a.Equals(b);

    public override string ToString() => X.ToString("x8") + Y.ToString("x8") + Z.ToString("x8") + W.ToString("x8");
}

[Serializable]
public struct TlTimeline : IEquatable<TlTimeline>
{
    public TlAssetId Asset;

    public bool IsValid => Asset.IsValid;

    public bool Equals(TlTimeline other) => Asset.Equals(other.Asset);

    public override bool Equals(object? obj) => obj is TlTimeline other && Equals(other);

    public override int GetHashCode() => Asset.GetHashCode();
}
`````

### tl src/Tl.Core/KindAttributes.cs

Path: `tl/src/Tl.Core/KindAttributes.cs`  (960 bytes)

`````csharp
namespace Tl;

public enum KindEntries : byte { Spans, Pulses, Both }

public enum KindMode : byte { Combine, Each }

public enum ClipEvent : byte { Start, End, Marker }

[AttributeUsage(AttributeTargets.Struct)]
public sealed class TimelineKindAttribute : Attribute
{
    public KindEntries Accepts { get; set; }
    public KindMode Mode { get; set; }
    public int Order { get; set; }
    public string? Group { get; set; }
    public bool FoldsPulses { get; set; }
}

public interface IPulseFold<T> where T : unmanaged
{
    void Fold(ref T total, in T next);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class FeedsAttribute : Attribute
{
    public FeedsAttribute(Type kind) { Kind = kind; }
    public Type Kind { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PulseAttribute : Attribute
{
    public PulseAttribute(Type kind) { Kind = kind; }
    public Type Kind { get; }
    public ClipEvent On { get; set; }
}
`````

### tl src/Tl.Core/TimelineSystem.cs

Path: `tl/src/Tl.Core/TimelineSystem.cs`  (466 bytes)

`````csharp
using System;

namespace Tl;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class TimelineSystemAttribute : Attribute;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class TimelineSystemMemberAttribute : Attribute
{
    public TimelineSystemMemberAttribute(Type system) => System = system;
    public Type System { get; }
}

[AttributeUsage(AttributeTargets.Enum)]
public sealed class TimelineSlotKeysAttribute : Attribute;
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 01_PlaybackModel.cs

Path: `senior-feed/01_PlaybackModel.cs`  (15443 bytes)

`````csharp
// ===== tl: src/Tl.Core/PlaybackModel.cs  (Playback slot struct, PlaybackTable: per-entity runs, spill arena, Playing bitset)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tl;

[Flags]
public enum PlaybackFlags : byte
{
    None = 0,
    Reverse = 1 << 0,
    Paused = 1 << 1,
    StartPending = 1 << 2,
    Ended = 1 << 3,
    Stopping = 1 << 4,
    StopAged = 1 << 5,
    Began = 1 << 6,
    ReenterPending = 1 << 7,
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct Playback
{
    public ushort Timeline;
    public ushort Tick;
    public byte Channel;
    public PlaybackFlags Flags;
    public byte Rate;
    public byte Phase;
}

public readonly struct EntityRef
{
    public EntityRef(int index, int version)
    {
        Index = index;
        Version = version;
    }

    public readonly int Index;
    public readonly int Version;
}

public readonly unsafe struct PlaybackView
{
    internal PlaybackView(Playback* slots, int* counts, ulong* spilled, Playback* blocks, int entities, int slotsPerEntity)
    {
        Slots = slots;
        Counts = counts;
        Spilled = spilled;
        Blocks = blocks;
        EntityCount = entities;
        SlotsPerEntity = slotsPerEntity;
    }

    public readonly Playback* Slots;
    public readonly int* Counts;
    public readonly ulong* Spilled;
    public readonly Playback* Blocks;
    public readonly int EntityCount;
    public readonly int SlotsPerEntity;

    public int CountOf(int entity) => Counts[entity];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Playback* RunOf(int entity)
        => (Spilled[entity >> 6] & (1ul << (entity & 63))) == 0
            ? Slots + (nuint)entity * (nuint)SlotsPerEntity
            : Blocks + *(int*)(Slots + (nuint)entity * (nuint)SlotsPerEntity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Playback* SlotOf(int entity, int slot) => RunOf(entity) + slot;
}

public sealed unsafe class PlaybackTable : IDisposable
{
    Playback* _slots;
    readonly int* _counts;
    readonly int _entities;
    readonly int _slotsPerEntity;
    readonly ulong* _spilled;
    readonly int _spilledWords;
    Playback* _blocks;
    int _blocksCapacity;
    int _blocksUsed;
    readonly int[] _freeHeads;
    readonly ulong* _playing;
    readonly int _playingWords;
    KindStore? _kinds;
    readonly RequestQueue* _consumer;
    readonly RequestQueue* _pending;
    readonly ClosingList* _closing;
    int _growGate;
    bool _disposed;
    internal ulong LibraryGeneration;

    public PlaybackTable(int entityCapacity, int slotsPerEntity = 4)
    {
        if (entityCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(entityCapacity));
        if (slotsPerEntity <= 0 || slotsPerEntity > MaxSlots) throw new ArgumentOutOfRangeException(nameof(slotsPerEntity));
        _entities = entityCapacity;
        _slotsPerEntity = slotsPerEntity;
        var slots = NativeMemory.AlignedAlloc((nuint)entityCapacity * (nuint)slotsPerEntity * 8u, 64u);
        var counts = NativeMemory.AlignedAlloc((nuint)entityCapacity * 4u, 8u);
        NativeMemory.Clear(slots, (nuint)entityCapacity * (nuint)slotsPerEntity * 8u);
        NativeMemory.Clear(counts, (nuint)entityCapacity * 4u);
        _slots = (Playback*)slots;
        _counts = (int*)counts;
        _spilledWords = (entityCapacity + 63) >> 6;
        _spilled = (ulong*)NativeMemory.AlignedAlloc((nuint)_spilledWords * 8u, 8u);
        NativeMemory.Clear(_spilled, (nuint)_spilledWords * 8u);
        _freeHeads = new int[9];
        for (var i = 0; i < _freeHeads.Length; i++) _freeHeads[i] = -1;
        _playingWords = (entityCapacity + 63) >> 6;
        _playing = (ulong*)NativeMemory.AlignedAlloc((nuint)_playingWords * 8u, 8u);
        NativeMemory.Clear(_playing, (nuint)_playingWords * 8u);
        _consumer = RequestQueue.Create();
        _pending = RequestQueue.Create();
        _closing = ClosingList.Create();
    }

    public const int MaxSlots = 256;

    public int EntityCapacity => _entities;
    public KindStore? Kinds => _kinds;
    public ulong* Playing => _playing;
    public int PlayingWords => _playingWords;

    internal void SetPlaying(int entity, bool value)
    {
        if (value) _playing[entity >> 6] |= 1ul << (entity & 63);
        else _playing[entity >> 6] &= ~(1ul << (entity & 63));
    }

    internal void AttachKinds(KindStore? kinds)
    {
        if (kinds != null && _kinds != null) throw new InvalidOperationException("A KindStore is already attached to this PlaybackTable.");
        _kinds = kinds;
    }

    public int SlotsPerEntity => _slotsPerEntity;
    public PlaybackView View => new(_slots, _counts, _spilled, _blocks, _entities, _slotsPerEntity);
    public RequestQueue* Pending => _pending;
    public RequestQueue* Consumer => _consumer;
    public ClosingList* Closing => _closing;

    public int CountOf(int entity) => _counts[entity];

    internal bool IsSpilled(int entity) => (_spilled[entity >> 6] & (1ul << (entity & 63))) != 0;

    internal int RunCapacity(int entity)
    {
        var inline = InlineRun(entity);
        return IsSpilled(entity) ? *(int*)((byte*)inline + 4) : _slotsPerEntity;
    }

    // Playback storage only: inline slots, counts, the Spilled bitset and
    // the Blocks arena capacity. Excludes the Playing bitset, the request
    // queues and the closing list.
    internal long NativeBytes =>
        (long)_entities * _slotsPerEntity * 8
        + (long)_entities * 4
        + (long)_spilledWords * 8
        + (long)_blocksCapacity * 8;
    public bool IsPlaying(int entity, ushort timeline)
    {
        var slots = new Slots(this, entity);
        return PlaybackRules.IsPlaying(ref slots, timeline);
    }

    void EnterGate() { var spin = new SpinWait(); while (Interlocked.CompareExchange(ref _growGate, 1, 0) != 0) spin.SpinOnce(); }
    void ExitGate() => Volatile.Write(ref _growGate, 0);

    Playback* InlineRun(int entity) => _slots + (nuint)entity * (nuint)_slotsPerEntity;

    static int NextPow2(int needed)
    {
        var size = 2;
        while (size < needed) size <<= 1;
        return size;
    }

    static int SizeIndex(int size)
    {
        var index = 0;
        while (size > 1)
        {
            size >>= 1;
            index++;
        }
        return index;
    }

    int AllocBlock(int size)
    {
        var index = SizeIndex(size);
        var head = _freeHeads[index];
        if (head >= 0)
        {
            _freeHeads[index] = *(int*)(_blocks + (nuint)head);
            NativeMemory.Clear(_blocks + (nuint)head, (nuint)size * 8u);
            return head;
        }
        if (_blocksUsed + size > _blocksCapacity)
        {
            var needed = _blocksUsed + size;
            var capacity = _blocksCapacity == 0 ? 256 : Math.Max(_blocksCapacity * 2, needed);
            var blocks = (Playback*)NativeMemory.AlignedAlloc((nuint)capacity * 8u, 64u);
            if (_blocks != null)
            {
                new ReadOnlySpan<Playback>(_blocks, _blocksUsed).CopyTo(new Span<Playback>(blocks, _blocksUsed));
                NativeMemory.AlignedFree(_blocks);
            }
            _blocks = blocks;
            _blocksCapacity = capacity;
        }
        var offset = _blocksUsed;
        _blocksUsed += size;
        NativeMemory.Clear(_blocks + (nuint)offset, (nuint)size * 8u);
        return offset;
    }

    void FreeBlock(int offset, int capacity)
    {
        var index = SizeIndex(capacity);
        *(int*)(_blocks + (nuint)offset) = _freeHeads[index];
        _freeHeads[index] = offset;
    }

    void Unspill(int entity, Playback* inline)
    {
        var offset = *(int*)inline;
        var capacity = *(int*)((byte*)inline + 4);
        var count = _counts[entity];
        new ReadOnlySpan<Playback>(_blocks + (nuint)offset, count).CopyTo(new Span<Playback>(inline, count));
        _spilled[entity >> 6] &= ~(1ul << (entity & 63));
        FreeBlock(offset, capacity);
    }

    // Ensures one entity's run holds at least length playbacks and returns
    // the run pointer. Other entities' runs are untouched.
    internal Playback* Reserve(int entity, int length)
    {
        EnterGate();
        try
        {
            var inline = InlineRun(entity);
            var count = _counts[entity];
            var needed = Math.Max(length, count);
            if (needed <= _slotsPerEntity)
            {
                if (IsSpilled(entity)) Unspill(entity, inline);
                return inline;
            }
            var size = NextPow2(Math.Min(needed, MaxSlots));
            if (IsSpilled(entity))
            {
                var offset = *(int*)inline;
                var capacity = *(int*)((byte*)inline + 4);
                if (capacity >= Math.Min(needed, MaxSlots)) return _blocks + (nuint)offset;
                var grown = size;
                var newOffset = AllocBlock(grown);
                new ReadOnlySpan<Playback>(_blocks + (nuint)offset, count).CopyTo(new Span<Playback>(_blocks + (nuint)newOffset, count));
                FreeBlock(offset, capacity);
                *(int*)inline = newOffset;
                *(int*)((byte*)inline + 4) = grown;
                return _blocks + (nuint)newOffset;
            }
            var block = size;
            var blockOffset = AllocBlock(block);
            new ReadOnlySpan<Playback>(inline, count).CopyTo(new Span<Playback>(_blocks + (nuint)blockOffset, count));
            *(int*)inline = blockOffset;
            *(int*)((byte*)inline + 4) = block;
            _spilled[entity >> 6] |= 1ul << (entity & 63);
            return _blocks + (nuint)blockOffset;
        }
        finally
        {
            ExitGate();
        }
    }

    internal void CloseSlot(int entity, int slot)
    {
        var slots = SlotsOf(entity);
        ClosingList.Add(_closing, entity, slot, in slots[slot]);
        if (_kinds == null) RemoveSlot(entity, slot);
        else _kinds.Close(entity, slot, ref slots[slot]);
    }

    internal void ResetEntity(int entity)
    {
        _kinds?.ResetEntity(entity);
        var slots = SlotsOf(entity);
        new Span<Playback>(slots, _counts[entity]).Clear();
        _counts[entity] = 0;
        if (IsSpilled(entity)) Unspill(entity, InlineRun(entity));
        new Span<Playback>(InlineRun(entity), _slotsPerEntity).Clear();
        SetPlaying(entity, false);
    }

    internal void RemoveSlot(int entity, int slot)
    {
        var slots = SlotsOf(entity);
        slots[slot] = default;
        var count = _counts[entity];
        while (count > 0 && slots[count - 1].Timeline == 0) count--;
        _counts[entity] = count;
        if (count <= _slotsPerEntity && IsSpilled(entity)) Unspill(entity, InlineRun(entity));
    }

    internal Playback* SlotsOf(int entity)
    {
        var inline = InlineRun(entity);
        return IsSpilled(entity) ? _blocks + *(int*)inline : inline;
    }

    internal int* Counts => _counts;

    public bool Find(int entity, byte channel, out Playback playback)
    {
        var slots = new Slots(this, entity);
        return PlaybackRules.Find(ref slots, channel, out playback);
    }

    internal struct Slots : IPlaybackSlots
    {
        readonly PlaybackTable _table;
        readonly int _entity;
        Playback* _items;

        public Slots(PlaybackTable table, int entity)
        {
            _table = table;
            _entity = entity;
            _items = table.SlotsOf(entity);
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _table._counts[_entity];
        }

        public ref Playback this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _items[index];
        }

        public bool StartsPending => _table._kinds != null;

        public bool TryAdd(in Playback playback)
        {
            var count = _table._counts[_entity];
            if (count == MaxSlots) return false;
            if (count >= _table.RunCapacity(_entity) || (_table.IsSpilled(_entity) && count <= _table._slotsPerEntity))
                _items = _table.Reserve(_entity, count + 1);
            _items[count] = playback;
            _table._counts[_entity] = count + 1;
            return true;
        }

        public bool Admit(ushort timeline)
        {
            _table._kinds?.ValidateStart(_entity, timeline);
            return true;
        }

        public void Close(int index)
        {
            _table.CloseSlot(_entity, index);
            _items = _table.SlotsOf(_entity);
        }

        public void Purge(int index) => _table._kinds?.Purge(_entity, index, in _items[index]);
    }

    public void Start(int entity, ushort timeline, ushort at = 0) => RequestQueue.Add(_pending, entity, -1, timeline, at, RequestOp.Start);
    public void Stop(int entity, ushort timeline) => RequestQueue.Add(_pending, entity, -1, timeline, 0, RequestOp.Stop);
    public void StopChannel(int entity, byte channel) => RequestQueue.Add(_pending, entity, -1, channel, 0, RequestOp.StopChannel);
    public void StopAll(int entity) => RequestQueue.Add(_pending, entity, -1, 0, 0, RequestOp.StopAll);
    public void Pause(int entity, ushort timeline) => RequestQueue.Add(_pending, entity, -1, timeline, 0, RequestOp.Pause);
    public void Resume(int entity, ushort timeline) => RequestQueue.Add(_pending, entity, -1, timeline, 0, RequestOp.Resume);
    public void Seek(int entity, ushort timeline, ushort tick) => RequestQueue.Add(_pending, entity, -1, timeline, tick, RequestOp.Seek);
    public void Reverse(int entity, ushort timeline, bool reverse) => RequestQueue.Add(_pending, entity, -1, timeline, reverse ? (ushort)1 : (ushort)0, RequestOp.Reverse);
    public void PauseChannel(int entity, byte channel) => RequestQueue.Add(_pending, entity, -1, channel, 0, RequestOp.PauseChannel);
    public void ResumeChannel(int entity, byte channel) => RequestQueue.Add(_pending, entity, -1, channel, 0, RequestOp.ResumeChannel);
    public void SeekChannel(int entity, byte channel, ushort tick) => RequestQueue.Add(_pending, entity, -1, channel, tick, RequestOp.SeekChannel);
    public void ReverseChannel(int entity, byte channel, bool reverse) => RequestQueue.Add(_pending, entity, -1, channel, reverse ? (ushort)1 : (ushort)0, RequestOp.ReverseChannel);
    public void SetSpeed(int entity, ushort timeline, ushort sixteenths) => RequestQueue.Add(_pending, entity, -1, timeline, sixteenths, RequestOp.Speed);
    public void SetChannelSpeed(int entity, byte channel, ushort sixteenths) => RequestQueue.Add(_pending, entity, -1, channel, sixteenths, RequestOp.SpeedChannel);
    public void Reset(int entity) => RequestQueue.Add(_pending, entity, -1, 0, 0, RequestOp.Reset);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _kinds?.Dispose();
        NativeMemory.AlignedFree(_playing);
        RequestQueue.Free(_pending);
        RequestQueue.Free(_consumer);
        ClosingList.Free(_closing);
        NativeMemory.AlignedFree(_slots);
        NativeMemory.AlignedFree(_counts);
        NativeMemory.AlignedFree(_spilled);
        if (_blocks != null) NativeMemory.AlignedFree(_blocks);
        GC.SuppressFinalize(this);
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 02_PlaybackRules.cs

Path: `senior-feed/02_PlaybackRules.cs`  (16719 bytes)

`````csharp
// ===== tl: src/Tl.Core/PlaybackRules.cs  (Advance fast path, Step, Move = the single motion rule, Cross = rated phase math)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System.Runtime.CompilerServices;

namespace Tl;

public interface IPlaybackSlots
{
    int Length { get; }
    ref Playback this[int index] { get; }
    bool StartsPending { get; }
    bool TryAdd(in Playback playback);
    bool Admit(ushort timeline);
    void Close(int index);
    void Purge(int index);
}

public enum AdvanceResult : byte
{
    Kept,
    Removed,
    Unknown,
}
internal enum PlaybackMotion : byte
{
    Stayed,
    Stepped,
    Wrapped,
    Handed,
    Parked,
    Held,
    Removed,
    Unknown,
}


public static unsafe class PlaybackRules
{
    public static bool Rebind(ref Playback slot, LibraryTimeline* timelines, int timelineCount)
    {
        if (slot.Timeline == 0 || (slot.Flags & PlaybackFlags.Stopping) != 0 || (uint)(slot.Timeline - 1) >= (uint)timelineCount || timelines[slot.Timeline - 1].Duration == 0)
        {
            slot = default;
            return false;
        }
        var tl = timelines + (slot.Timeline - 1);
        if (slot.Tick >= tl->Duration) slot.Tick = (ushort)(tl->Loop != 0 ? slot.Tick % tl->Duration : tl->Duration - 1);
        slot.Flags &= ~PlaybackFlags.Began;
        if ((slot.Flags & (PlaybackFlags.StartPending | PlaybackFlags.Ended)) == 0) slot.Flags |= PlaybackFlags.ReenterPending;
        return true;
    }

    public const PlaybackFlags Parked = PlaybackFlags.StartPending | PlaybackFlags.Ended;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsParked(PlaybackFlags flags) => (flags & Parked) == Parked;

    public static bool Resumes(PlaybackFlags flags, bool forward)
        => (flags & (Parked | PlaybackFlags.Paused | PlaybackFlags.Stopping)) == Parked && ((flags & PlaybackFlags.Reverse) != 0) != forward;

    public static bool Unpark(ref Playback slot, bool forward)
    {
        if (!Resumes(slot.Flags, forward)) return false;
        slot.Flags &= ~PlaybackFlags.Ended;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLive(in Playback slot) => slot.Timeline != 0 && (slot.Flags & PlaybackFlags.Stopping) == 0;

    public static void Apply<TSlots>(ref TSlots slots, LibraryTimeline* timelines, int timelineCount, RequestOp op, ushort timeline, ushort at)
        where TSlots : struct, IPlaybackSlots
    {
        switch (op)
        {
            case RequestOp.Start:
                Start(ref slots, timelines, timelineCount, timeline, at, out _);
                return;
            case RequestOp.Stop:
                Stop(ref slots, timeline);
                return;
            case RequestOp.StopChannel:
                StopChannel(ref slots, (byte)timeline);
                return;
            case RequestOp.Pause:
                Pause(ref slots, timeline, true);
                return;
            case RequestOp.Resume:
                Pause(ref slots, timeline, false);
                return;
            case RequestOp.Seek:
                Seek(ref slots, timelines, timelineCount, Find(ref slots, timeline), at);
                return;
            case RequestOp.Reverse:
                Reverse(ref slots, timelines, timelineCount, Find(ref slots, timeline), at != 0);
                return;
            case RequestOp.PauseChannel:
                PauseAt(ref slots, FindChannel(ref slots, (byte)timeline), true);
                return;
            case RequestOp.ResumeChannel:
                PauseAt(ref slots, FindChannel(ref slots, (byte)timeline), false);
                return;
            case RequestOp.SeekChannel:
                Seek(ref slots, timelines, timelineCount, FindChannel(ref slots, (byte)timeline), at);
                return;
            case RequestOp.ReverseChannel:
                Reverse(ref slots, timelines, timelineCount, FindChannel(ref slots, (byte)timeline), at != 0);
                return;
            case RequestOp.Speed:
                SetSpeed(ref slots, Find(ref slots, timeline), at);
                return;
            case RequestOp.SpeedChannel:
                SetSpeed(ref slots, FindChannel(ref slots, (byte)timeline), at);
                return;
            case RequestOp.Reset:
                for (var i = 0; i < slots.Length; i++)
                {
                    slots.Purge(i);
                    slots[i] = default;
                }
                return;
            default:
                StopAll(ref slots);
                return;
        }
    }

    public static bool Start<TSlots>(ref TSlots slots, LibraryTimeline* timelines, int timelineCount, ushort timeline, ushort at, out bool replaced)
        where TSlots : struct, IPlaybackSlots
    {
        replaced = false;
        if (timeline == 0 || (uint)(timeline - 1) >= (uint)timelineCount) return false;
        var tl = timelines + (timeline - 1);
        var duration = tl->Duration;
        if (duration == 0) return false;
        var tick = tl->Loop != 0 ? (ushort)(at % duration) : at < duration ? at : (ushort)(duration - 1);
        var channel = (byte)tl->Channel;
        if (channel != 0)
            for (var i = 0; i < slots.Length; i++)
                if (IsLive(in slots[i]) && slots[i].Channel == channel)
                {
                    slots.Close(i);
                    replaced = true;
                    break;
                }
        var count = slots.Length;
        var free = count;
        for (var i = count - 1; i >= 0; i--)
        {
            if (slots[i].Timeline == 0) free = i;
            else if (slots[i].Timeline == timeline && (slots[i].Flags & PlaybackFlags.Stopping) == 0) return false;
        }
        if (!slots.Admit(timeline)) return false;
        var playback = new Playback { Timeline = timeline, Tick = tick, Channel = channel, Flags = tick > 0 || slots.StartsPending ? PlaybackFlags.StartPending : PlaybackFlags.None };
        if (free < count)
        {
            slots[free] = playback;
            return true;
        }
        return slots.TryAdd(in playback);
    }

    public static bool Stop<TSlots>(ref TSlots slots, ushort timeline) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].Timeline == timeline && IsLive(in slots[i]))
            {
                slots.Close(i);
                return true;
            }
        return false;
    }

    public static bool StopChannel<TSlots>(ref TSlots slots, byte channel) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
            if (IsLive(in slots[i]) && slots[i].Channel == channel)
            {
                slots.Close(i);
                return true;
            }
        return false;
    }

    public static void StopAll<TSlots>(ref TSlots slots) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
            if (IsLive(in slots[i])) slots.Close(i);
    }

    public static int Find<TSlots>(ref TSlots slots, ushort timeline) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].Timeline == timeline && IsLive(in slots[i])) return i;
        return -1;
    }

    public static int FindChannel<TSlots>(ref TSlots slots, byte channel) where TSlots : struct, IPlaybackSlots
    {
        if (channel == 0) return -1;
        for (var i = 0; i < slots.Length; i++)
            if (IsLive(in slots[i]) && slots[i].Channel == channel) return i;
        return -1;
    }

    public static bool Seek<TSlots>(ref TSlots slots, LibraryTimeline* timelines, int timelineCount, int index, ushort at) where TSlots : struct, IPlaybackSlots
    {
        if (index < 0) return false;
        ref var slot = ref slots[index];
        if ((uint)(slot.Timeline - 1) >= (uint)timelineCount) return false;
        var tl = timelines + (slot.Timeline - 1);
        var duration = tl->Duration;
        if (duration == 0) return false;
        var tick = tl->Loop != 0 ? (ushort)(at % duration) : at < duration ? at : (ushort)(duration - 1);
        if ((slot.Flags & PlaybackFlags.StartPending) != 0)
        {
            slot.Tick = tick;
            slot.Flags &= ~PlaybackFlags.Ended;
            return true;
        }
        slots.Purge(index);
        slot.Tick = tick;
        slot.Phase = 0;
        slot.Flags = (slot.Flags & (PlaybackFlags.Reverse | PlaybackFlags.Paused)) | PlaybackFlags.ReenterPending;
        return true;
    }

    public static bool Reverse<TSlots>(ref TSlots slots, LibraryTimeline* timelines, int timelineCount, int index, bool reverse) where TSlots : struct, IPlaybackSlots
    {
        if (index < 0) return false;
        ref var slot = ref slots[index];
        var flags = reverse ? slot.Flags | PlaybackFlags.Reverse : slot.Flags & ~PlaybackFlags.Reverse;
        if ((flags & Parked) == PlaybackFlags.Ended && (uint)(slot.Timeline - 1) < (uint)timelineCount && slot.Tick == (reverse ? timelines[slot.Timeline - 1].Duration - 1 : 0))
            flags = (flags & ~PlaybackFlags.Ended) | PlaybackFlags.StartPending;
        slot.Flags = flags;
        return true;
    }

    public static bool SetSpeed<TSlots>(ref TSlots slots, int index, ushort sixteenths) where TSlots : struct, IPlaybackSlots
    {
        if (index < 0) return false;
        ref var slot = ref slots[index];
        slot.Rate = sixteenths == 16 ? (byte)0 : sixteenths > 255 ? (byte)255 : (byte)sixteenths;
        if (slot.Rate == 0) slot.Phase = 0;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Crossings(in Playback slot) => slot.Rate == 0 ? 1 : (slot.Phase + slot.Rate) >> 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Cross(ref Playback slot)
    {
        if (slot.Rate == 0) return 1;
        var sum = slot.Phase + slot.Rate;
        slot.Phase = (byte)(sum & 15);
        return sum >> 4;
    }

    public static bool Moves(PlaybackFlags flags, bool forward)
        => (flags & (PlaybackFlags.Paused | PlaybackFlags.Stopping)) == 0 && ((flags & PlaybackFlags.Ended) == 0 || Resumes(flags, forward));

    public static AdvanceResult Step(ref Playback slot, LibraryTimeline* timelines, int timelineCount, bool forward)
    {
        if (slot.Rate == 0) return Advance(ref slot, timelines, timelineCount, forward);
        var crossings = Moves(slot.Flags, forward) ? Cross(ref slot) : 1;
        var result = AdvanceResult.Kept;
        for (var j = 0; j < crossings; j++)
        {
            result = Advance(ref slot, timelines, timelineCount, forward);
            if (result != AdvanceResult.Kept || (slot.Flags & (PlaybackFlags.Ended | PlaybackFlags.Paused)) != 0) break;
        }
        return result;
    }

    public static bool PauseAt<TSlots>(ref TSlots slots, int index, bool pause) where TSlots : struct, IPlaybackSlots
    {
        if (index < 0) return false;
        ref var slot = ref slots[index];
        slot.Flags = pause ? slot.Flags | PlaybackFlags.Paused : slot.Flags & ~PlaybackFlags.Paused;
        return true;
    }

    public static bool Pause<TSlots>(ref TSlots slots, ushort timeline, bool pause) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
        {
            ref var slot = ref slots[i];
            if (slot.Timeline != timeline || !IsLive(in slot)) continue;
            slot.Flags = pause ? slot.Flags | PlaybackFlags.Paused : slot.Flags & ~PlaybackFlags.Paused;
            return true;
        }
        return false;
    }

    internal static PlaybackMotion Move(ref Playback slot, LibraryTimeline* timelines, int timelineCount, bool forward)
    {
        var flags = slot.Flags;
        if ((flags & PlaybackFlags.ReenterPending) != 0)
        {
            slot.Flags = flags & ~(PlaybackFlags.ReenterPending | PlaybackFlags.Began | PlaybackFlags.StartPending);
            return PlaybackMotion.Stayed;
        }
        if (IsParked(flags))
        {
            if (!Unpark(ref slot, forward)) return PlaybackMotion.Stayed;
            flags = slot.Flags;
        }
        if ((flags & (PlaybackFlags.Ended | PlaybackFlags.Paused)) != 0)
        {
            slot.Flags = flags & ~PlaybackFlags.StartPending;
            return PlaybackMotion.Stayed;
        }
        if (slot.Timeline == 0 || (uint)(slot.Timeline - 1) >= (uint)timelineCount) return PlaybackMotion.Unknown;
        var tl = timelines + (slot.Timeline - 1);
        var duration = tl->Duration;
        if (duration == 0)
        {
            slot.Flags = (flags | PlaybackFlags.Ended) & ~PlaybackFlags.StartPending;
            return PlaybackMotion.Held;
        }
        var handover = false;
        var motion = PlaybackMotion.Stepped;
        if (((flags & PlaybackFlags.Reverse) != 0) == forward)
        {
            if (slot.Tick == 0)
            {
                if (tl->Loop != 0)
                {
                    slot.Tick = (ushort)(duration - 1);
                    motion = PlaybackMotion.Wrapped;
                }
                else if (forward)
                {
                    slot.Flags = flags | Parked;
                    return PlaybackMotion.Parked;
                }
                else if (tl->Hold != 0)
                {
                    slot.Flags = flags | PlaybackFlags.Ended;
                    motion = PlaybackMotion.Held;
                }
                else return PlaybackMotion.Removed;
            }
            else
            {
                slot.Tick--;
                motion = PlaybackMotion.Stepped;
            }
        }
        else if ((uint)slot.Tick + 1u >= duration)
        {
            if (tl->Loop != 0)
            {
                slot.Tick = 0;
                motion = PlaybackMotion.Wrapped;
            }
            else if (tl->Next != 0 && (uint)(tl->Next - 1) < (uint)timelineCount)
            {
                slot.Timeline = tl->Next;
                slot.Tick = 0;
                slot.Channel = (byte)timelines[tl->Next - 1].Channel;
                handover = true;
                motion = PlaybackMotion.Handed;
            }
            else if (tl->Hold != 0)
            {
                slot.Tick = (ushort)(duration - 1);
                slot.Flags = flags | PlaybackFlags.Ended;
                motion = PlaybackMotion.Held;
            }
            else return PlaybackMotion.Removed;
        }
        else
        {
            slot.Tick++;
            motion = PlaybackMotion.Stepped;
        }
        slot.Flags = handover ? slot.Flags | PlaybackFlags.StartPending : slot.Flags & ~PlaybackFlags.StartPending;
        return motion;
    }

    public static AdvanceResult Advance(ref Playback slot, LibraryTimeline* timelines, int timelineCount, bool forward)
    {
        // The common step (an active, forward, mid-timeline playback) does exactly what Move does for it: tick up, clear StartPending.
        // Parked, Ended, Paused, ReenterPending, Reverse, an unknown timeline and the last tick all fall through to Move.
        var flags = slot.Flags;
        if (forward && (flags & (PlaybackFlags.Reverse | PlaybackFlags.ReenterPending | PlaybackFlags.Paused | PlaybackFlags.Ended)) == 0)
        {
            var index = (uint)(slot.Timeline - 1);
            if (index < (uint)timelineCount && (uint)slot.Tick + 1u < (uint)timelines[index].Duration)
            {
                slot.Tick++;
                slot.Flags = flags & ~PlaybackFlags.StartPending;
                return AdvanceResult.Kept;
            }
        }
        var motion = Move(ref slot, timelines, timelineCount, forward);
        if (motion == PlaybackMotion.Removed) return AdvanceResult.Removed;
        if (motion == PlaybackMotion.Unknown) return AdvanceResult.Unknown;
        return AdvanceResult.Kept;
    }

    public static bool IsPlaying<TSlots>(ref TSlots slots, ushort timeline) where TSlots : struct, IPlaybackSlots
    {
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].Timeline == timeline && IsLive(in slots[i])) return true;
        return false;
    }

    public static bool Find<TSlots>(ref TSlots slots, byte channel, out Playback playback) where TSlots : struct, IPlaybackSlots
    {
        if (channel != 0)
            for (var i = 0; i < slots.Length; i++)
                if (IsLive(in slots[i]) && slots[i].Channel == channel)
                {
                    playback = slots[i];
                    return true;
                }
        playback = default;
        return false;
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 03_TimelineWorld.Systems.cs

Path: `senior-feed/03_TimelineWorld.Systems.cs`  (6160 bytes)

`````csharp
// ===== tl: src/Tl.Core/TimelineWorld.Systems.cs  (the dense .NET tick loop: AdvanceRange/AdvanceEntity, bit-scan over Playing)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System;

namespace Tl;

public sealed partial class TimelineWorld
{
    internal SystemRegistry? _systems;
    internal ulong _generation;

    public unsafe Requests Requests(PlaybackTable players)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        return new Requests(players.Consumer, -1, -1);
    }

    public unsafe void ApplyRequests(PlaybackTable players)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Kinds != null) players.Kinds.Sync();
        else SyncPlaybacks(players);
        Drain(players, players.Consumer);
        Drain(players, players.Pending);
    }

    public unsafe void Tick(PlaybackTable players, bool forward = true)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        var kinds = players.Kinds ?? throw new InvalidOperationException("Tick needs a KindStore attached to the PlaybackTable; use Advance for kind-free playback.");
        ClosingList.Clear(players.Closing);
        ApplyRequests(players);
        kinds.TickPass(forward);
    }



    public unsafe void Advance(PlaybackTable players, bool forward = true)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Kinds != null) throw new InvalidOperationException("A PlaybackTable with a KindStore advances through Tick, not Advance.");
        SyncPlaybacks(players);
        var timelines = _view.Timelines;
        if (timelines == null) return;
        var count = _view.TimelineCount;
        AdvanceRange(players, timelines, count, forward, 0, players.PlayingWords);
        ClosingList.Clear(players.Closing);
    }


    unsafe void AdvanceRange(PlaybackTable players, LibraryTimeline* timelines, int count, bool forward, int firstWord, int endWord)
    {
        var playing = players.Playing;
        if (firstWord < 0) firstWord = 0; if (endWord > players.PlayingWords) endWord = players.PlayingWords;
        for (var w = firstWord; w < endWord; w++)
        {
            var bits = playing[w];
            while (bits != 0)
            {
                var entity = (w << 6) + KindStore.Tz(bits);
                bits &= bits - 1;
                AdvanceEntity(players, entity, timelines, count, forward);
            }
        }
    }

    unsafe void AdvanceEntity(PlaybackTable players, int entity, LibraryTimeline* timelines, int count, bool forward)
    {
        var slots = players.SlotsOf(entity);
        var slotsCount = players.CountOf(entity);
        // One pass: step every slot and decide in the same visit whether the entity keeps playing. A removal zeroes its slot and keeps
        // the other indices stable, and a later slot never changes an earlier slot's flags, so this equals stepping all slots first and scanning them after.
        var holds = false;
        for (var i = 0; i < slotsCount; i++)
        {
            if (slots[i].Timeline == 0) continue;
            var result = PlaybackRules.Step(ref slots[i], timelines, count, forward);
            if (result == AdvanceResult.Removed) { players.RemoveSlot(entity, i); slots = players.SlotsOf(entity); continue; }
            if (result == AdvanceResult.Unknown) throw new ArgumentException($"Playback slot {i} of entity {entity} names no timeline in the loaded library.");
            if (holds) continue;
            var f = slots[i].Flags;
            if (PlaybackRules.Moves(f, forward) || PlaybackRules.IsParked(f) || (f & (PlaybackFlags.Stopping | PlaybackFlags.ReenterPending)) != 0 || ((f & (PlaybackFlags.Paused | PlaybackFlags.Ended)) != 0 && (f & PlaybackFlags.StartPending) != 0))
                holds = true;
        }
        if (!holds) players.SetPlaying(entity, false);
#if TL_CHECKED
        Checked.Visited();
#endif
    }

    unsafe void Drain(PlaybackTable players, RequestQueue* queue)
    {
        if (queue->Count == 0) return;
        RequestQueue.SortStable(queue);
        for (var i = 0; i < queue->Count; i++)
        {
            ref readonly var record = ref queue->Item(i);
            ApplyRecord(players, record);
        }
        RequestQueue.Clear(queue);
    }

    unsafe void SyncPlaybacks(PlaybackTable players)
    {
        if (players.LibraryGeneration == _generation) return;
        for (var entity = 0; entity < players.EntityCapacity; entity++)
        {
            var slots = players.SlotsOf(entity);
            var count = players.CountOf(entity);
            for (var slot = 0; slot < count; slot++)
            {
                if (slots[slot].Timeline > _view.TimelineCount && (slots[slot].Flags & PlaybackFlags.Stopping) == 0)
                    throw new ArgumentException($"Playback slot {slot} of entity {entity} names no timeline in the reloaded library.");
                if (!PlaybackRules.Rebind(ref slots[slot], _view.Timelines, _view.TimelineCount)) { players.RemoveSlot(entity, slot); slots = players.SlotsOf(entity); }
            }
        }
        players.LibraryGeneration = _generation;
    }

    unsafe void ApplyRecord(PlaybackTable players, in RequestRecord record)
    {
        if ((uint)record.Entity >= (uint)players.EntityCapacity)
            throw new ArgumentOutOfRangeException(nameof(record), $"Request names entity {record.Entity} outside the table capacity.");
        if (record.Op == RequestOp.Start && (record.Timeline == 0 || (uint)(record.Timeline - 1) >= (uint)_view.TimelineCount))
            throw new ArgumentException($"Request starts timeline {record.Timeline}; the loaded library holds {_view.TimelineCount}.");
        if (record.Op == RequestOp.Reset)
        {
            players.ResetEntity(record.Entity);
            return;
        }
        var slots = new PlaybackTable.Slots(players, record.Entity);
        PlaybackRules.Apply(ref slots, _view.Timelines, _view.TimelineCount, record.Op, record.Timeline, record.At);
        players.SetPlaying(record.Entity, true);
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 04_KindRules.cs

Path: `senior-feed/04_KindRules.cs`  (15124 bytes)

`````csharp
// ===== tl: src/Tl.Core/KindRules.cs  (kind-path tick: TickEntity/TickSlot/TickRated)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System.Runtime.CompilerServices;

namespace Tl;

public unsafe struct KindTables
{
    public LibraryTimeline* Timelines;
    public int TimelineCount;
    public KindSectionView Sec;
    public int KindCount;
    public ulong* Closure;
    public uint* Pairs;
    public int* PairStart;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Reaches(ushort timeline, ulong reach) => KindCount == 0 || (Closure[timeline - 1] & ~reach) == 0;
}

public unsafe interface IKindRows
{
    KindEntry* Append(int row, LibraryFeeder* feeder);
    void RemoveSpan(int row, int kind, byte slot, ushort timeline, ushort step);
    void Purge(int row, byte slot, ushort timeline);
    Playback* Reserve(int row, int used, int length);
    void Sample(int row, byte slot, byte sample);
    void Unsample(int row);
}

public static unsafe class KindRules
{
    public static bool Rebind<TRows>(in KindTables t, ref TRows rows, int row, ref Playback s, int slot) where TRows : struct, IKindRows
    {
        if (s.Timeline == 0) return false;
        rows.Purge(row, (byte)slot, s.Timeline);
        return PlaybackRules.Rebind(ref s, t.Timelines, t.TimelineCount);
    }

    public static void Close<TRows>(in KindTables t, ref TRows rows, int row, ref Playback s, int slot) where TRows : struct, IKindRows
    {
        Stop(in t, ref rows, row, in s, slot);
        s.Flags = (s.Flags & (PlaybackFlags.Reverse | PlaybackFlags.Paused | PlaybackFlags.Ended | PlaybackFlags.StartPending)) | PlaybackFlags.Stopping;
    }

    static void Stop<TRows>(in KindTables t, ref TRows rows, int row, in Playback s, int slot) where TRows : struct, IKindRows
    {
        if (t.KindCount == 0 || (s.Flags & (PlaybackFlags.StartPending | PlaybackFlags.Ended)) != 0) return;
        var id = s.Timeline;
        var tk = t.Sec.Timelines + (id - 1);
        if (tk->KindMask == 0) return;
        var windows = t.Sec.Windows(tk);
        int tick = s.Tick;
        for (var k = t.PairStart[id - 1]; k < t.PairStart[id]; k++)
        {
            var w = (int)(t.Pairs[k] >> 16);
            if (tick < windows[w].Start || tick >= windows[w].End) continue;
            var feeder = t.Sec.Feeders + (t.Pairs[k] & 0xffff);
            if (feeder->Trigger == 0) rows.RemoveSpan(row, feeder->Kind, (byte)slot, id, (ushort)w);
            else if (feeder->Trigger == 2 && tick < windows[w].End - 1) Append(ref rows, row, feeder, slot, id, w, KindEntryFlags.Pulse | KindEntryFlags.Interrupted);
        }
    }

    public const byte Marker = 255;
    public const int MaxSampleSlots = 256;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSampled(byte* data, int length) => length != 0 && (((KindEntry*)data)->Flags & KindEntryFlags.Sample) != 0;

    public static int SampleDepth(byte* data, int length, int stride, Playback* slots)
    {
        var depth = 1;
        for (var i = 0; i < length; i++)
        {
            var e = (KindEntry*)(data + (nuint)i * (nuint)stride);
            if ((e->Flags & KindEntryFlags.Sample) == 0) break;
            var j = slots[e->Slot].Phase;
            if (j != Marker && j + 1 > depth) depth = j + 1;
        }
        return depth;
    }

    public static bool Due(KindEntry* entry, int substep, Playback* slot)
    {
        if ((entry->Flags & KindEntryFlags.Sample) != 0) return slot->Phase == substep;
        return substep == 0 && (entry->Timeline == 0 || (entry->Flags & KindEntryFlags.Pulse) != 0 || slot->Rate == 0);
    }

    public static bool TickEntity<TRows>(in KindTables t, Playback* slots, int count, ref TRows rows, int row, int stepDir) where TRows : struct, IKindRows
    {
        var live = false;
        var tls = t.Timelines;
        for (var i = 0; i < count; i++)
        {
            var s = slots + i;
            var id = s->Timeline;
            if (id == 0) continue;
            var flags = s->Flags;
            if ((flags & ~PlaybackFlags.Reverse) == 0 && s->Rate == 0)
            {
                var dir = flags == 0 ? stepDir : -stepDir;
                var tick = s->Tick + dir;
                if ((uint)tick < tls[id - 1].Duration)
                {
                    s->Tick = (ushort)tick;
                    Events(in t, ref rows, row, i, id, tick, dir);
                    live = true;
                    continue;
                }
            }
            else if (s->Rate != 0) return TickRatedFrom(in t, slots, count, ref rows, row, i, stepDir, live);
            live |= TickSlot(in t, ref rows, row, i, s, stepDir);
        }
        return live;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool TickRatedFrom<TRows>(in KindTables t, Playback* slots, int count, ref TRows rows, int row, int from, int stepDir, bool live) where TRows : struct, IKindRows
    {
        var samples = 0;
        for (var i = from; i < count; i++)
        {
            if (slots[i].Timeline == 0) continue;
            if (slots[i].Rate == 0) live |= TickSlot(in t, ref rows, row, i, slots + i, stepDir);
            else
            {
                slots = TickRated(in t, slots, count, ref rows, row, i, stepDir, ref samples, out var rated);
                live |= rated;
            }
        }
        return live;
    }

    static Playback* TickRated<TRows>(in KindTables t, Playback* slots, int count, ref TRows rows, int row, int i, int stepDir, ref int samples, out bool live) where TRows : struct, IKindRows
    {
        var s = slots + i;
        live = true;
        if (!PlaybackRules.Moves(s->Flags, stepDir > 0))
        {
            live = TickSlot(in t, ref rows, row, i, s, stepDir);
            return slots;
        }
        if (samples == 0) rows.Unsample(row);
        var crossings = PlaybackRules.Cross(ref *s);
        var room = MaxSampleSlots - count - samples;
        if (room <= 0) return slots;
        if (crossings > room) crossings = room;
        slots = rows.Reserve(row, count + samples, count + samples + (crossings == 0 ? 1 : crossings));
        s = slots + i;
        if (crossings == 0)
        {
            var mark = count + samples++;
            slots[mark] = *s;
            slots[mark].Phase = Marker;
            rows.Sample(row, (byte)i, (byte)mark);
            return slots;
        }
        for (var j = 0; j < crossings; j++)
        {
            live = TickSlot(in t, ref rows, row, i, s, stepDir);
            var at = count + samples++;
            slots[at] = *s;
            slots[at].Phase = (byte)j;
            rows.Sample(row, (byte)i, (byte)at);
            if (!live || s->Timeline == 0 || (s->Flags & (PlaybackFlags.Ended | PlaybackFlags.Paused)) != 0) break;
        }
        return slots;
    }

    static bool TickSlot<TRows>(in KindTables t, ref TRows rows, int row, int i, Playback* s, int stepDir) where TRows : struct, IKindRows
    {
        if ((s->Flags & (PlaybackFlags.Began | PlaybackFlags.ReenterPending)) == (PlaybackFlags.Began | PlaybackFlags.ReenterPending)) s->Flags &= ~PlaybackFlags.ReenterPending;
        s->Flags &= ~PlaybackFlags.Began;
        if ((s->Flags & PlaybackFlags.Stopping) != 0)
        {
            if ((s->Flags & PlaybackFlags.StopAged) != 0)
            {
                *s = default;
                return false;
            }
            s->Flags |= PlaybackFlags.StopAged;
            return true;
        }
        if (PlaybackRules.IsParked(s->Flags) && !PlaybackRules.Unpark(ref *s, stepDir > 0)) return true;
        if ((s->Flags & PlaybackFlags.StartPending) != 0)
        {
            Begin(in t, ref rows, row, i, s, stepDir);
            return true;
        }
        if ((s->Flags & PlaybackFlags.ReenterPending) != 0)
        {
            s->Flags |= PlaybackFlags.Began;
            Reenter(in t, ref rows, row, in *s, i);
            return true;
        }
        if ((s->Flags & PlaybackFlags.Paused) != 0) return false;
        if ((s->Flags & PlaybackFlags.Ended) != 0) return false;
        var before = *s;
        var motion = PlaybackRules.Move(ref *s, t.Timelines, t.TimelineCount, stepDir > 0);
        var dir = (before.Flags & PlaybackFlags.Reverse) != 0 ? -stepDir : stepDir;
        switch (motion)
        {
            case PlaybackMotion.Stepped:
                Events(in t, ref rows, row, i, before.Timeline, s->Tick, dir);
                return true;
            case PlaybackMotion.Wrapped:
                WrapRemove(in t, ref rows, row, i, before.Timeline, dir);
                Events(in t, ref rows, row, i, before.Timeline, s->Tick, dir);
                return true;
            case PlaybackMotion.Handed:
                RemoveActiveSpans(in t, ref rows, row, i, &before);
                Begin(in t, ref rows, row, i, s, stepDir);
                return true;
            case PlaybackMotion.Parked:
                RemoveActiveSpans(in t, ref rows, row, i, &before);
                return true;
            case PlaybackMotion.Held:
                RemoveActiveSpans(in t, ref rows, row, i, &before);
                return false;
            case PlaybackMotion.Removed:
                RemoveActiveSpans(in t, ref rows, row, i, &before);
                *s = default;
                return false;
            case PlaybackMotion.Unknown:
                *s = default;
                return false;
            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Events<TRows>(in KindTables t, ref TRows rows, int row, int i, ushort id, int tick, int dir) where TRows : struct, IKindRows
    {
        if (t.KindCount != 0 && t.Sec.Timelines[id - 1].KindMask != 0 && (t.Sec.EventBitsAt(t.Sec.Timelines + (id - 1), tick) & (dir > 0 ? 1 : 2)) != 0) ApplyEvents(in t, ref rows, row, i, id, tick, dir);
    }

    static void Begin<TRows>(in KindTables t, ref TRows rows, int row, int i, Playback* s, int stepDir) where TRows : struct, IKindRows
    {
        s->Flags = (s->Flags & ~PlaybackFlags.StartPending) | PlaybackFlags.Began;
        var id = s->Timeline;
        if (t.KindCount == 0 || t.Sec.Timelines[id - 1].KindMask == 0) return;
        var dir = (s->Flags & PlaybackFlags.Reverse) != 0 ? -stepDir : stepDir;
        var windows = t.Sec.Windows(t.Sec.Timelines + (id - 1));
        int tick = s->Tick;
        for (var k = t.PairStart[id - 1]; k < t.PairStart[id]; k++)
        {
            var w = (int)(t.Pairs[k] >> 16);
            var window = windows[w];
            var feeder = t.Sec.Feeders + (t.Pairs[k] & 0xffff);
            var inside = tick >= window.Start && tick < window.End;
            switch (feeder->Trigger)
            {
                case 0:
                    if (inside) Append(ref rows, row, feeder, i, id, w, KindEntryFlags.Span | ((dir > 0 ? tick != window.Start : tick != window.End - 1) ? KindEntryFlags.Synthetic : 0));
                    break;
                case 1:
                    if (inside) Append(ref rows, row, feeder, i, id, w, KindEntryFlags.Pulse | (tick != window.Start ? KindEntryFlags.Synthetic : 0));
                    break;
                case 2:
                    if (tick == window.End - 1) Append(ref rows, row, feeder, i, id, w, KindEntryFlags.Pulse);
                    break;
                default:
                    if (tick == window.Start) Append(ref rows, row, feeder, i, id, w, KindEntryFlags.Pulse);
                    break;
            }
        }
    }

    public static void Reenter<TRows>(in KindTables t, ref TRows rows, int row, in Playback s, int slot) where TRows : struct, IKindRows
    {
        var id = s.Timeline;
        if (t.KindCount == 0 || t.Sec.Timelines[id - 1].KindMask == 0) return;
        var windows = t.Sec.Windows(t.Sec.Timelines + (id - 1));
        int tick = s.Tick;
        for (var k = t.PairStart[id - 1]; k < t.PairStart[id]; k++)
        {
            var w = (int)(t.Pairs[k] >> 16);
            var feeder = t.Sec.Feeders + (t.Pairs[k] & 0xffff);
            if (feeder->Trigger == 0 && tick >= windows[w].Start && tick < windows[w].End) Append(ref rows, row, feeder, slot, id, w, KindEntryFlags.Span | KindEntryFlags.Synthetic);
        }
    }

    static void RemoveActiveSpans<TRows>(in KindTables t, ref TRows rows, int row, int i, Playback* s) where TRows : struct, IKindRows
    {
        var id = s->Timeline;
        if (t.KindCount == 0 || t.Sec.Timelines[id - 1].KindMask == 0) return;
        var windows = t.Sec.Windows(t.Sec.Timelines + (id - 1));
        int tick = s->Tick;
        for (var k = t.PairStart[id - 1]; k < t.PairStart[id]; k++)
        {
            var w = (int)(t.Pairs[k] >> 16);
            var feeder = t.Sec.Feeders + (t.Pairs[k] & 0xffff);
            if (feeder->Trigger == 0 && tick >= windows[w].Start && tick < windows[w].End) rows.RemoveSpan(row, feeder->Kind, (byte)i, id, (ushort)w);
        }
    }

    static void WrapRemove<TRows>(in KindTables t, ref TRows rows, int row, int i, ushort id, int dir) where TRows : struct, IKindRows
    {
        if (t.KindCount == 0) return;
        var tk = t.Sec.Timelines + (id - 1);
        if (tk->KindMask == 0) return;
        var events = t.Sec.Events(tk);
        var from = dir > 0 ? tk->WrapForward : tk->WrapBackward;
        var to = from + (dir > 0 ? tk->WrapForwardCount : tk->WrapBackwardCount);
        for (var n = from; n < to; n++)
        {
            var feeder = t.Sec.Feeders + events[n].Feeder;
            rows.RemoveSpan(row, feeder->Kind, (byte)i, id, events[n].Window);
        }
    }

    static void ApplyEvents<TRows>(in KindTables t, ref TRows rows, int row, int i, ushort id, int tick, int dir) where TRows : struct, IKindRows
    {
        var tk = t.Sec.Timelines + (id - 1);
        var events = t.Sec.Events(tk);
        var d = dir > 0 ? 0 : 1;
        var to = t.Sec.EventEnd(tk, tick, d);
        for (var n = t.Sec.EventStart(tk, tick, d); n < to; n++)
        {
            var ev = events[n];
            var feeder = t.Sec.Feeders + ev.Feeder;
            switch ((KindEventOp)ev.Op)
            {
                case KindEventOp.Remove:
                    rows.RemoveSpan(row, feeder->Kind, (byte)i, id, ev.Window);
                    break;
                case KindEventOp.Insert:
                    Append(ref rows, row, feeder, i, id, ev.Window, KindEntryFlags.Span);
                    break;
                default:
                    Append(ref rows, row, feeder, i, id, ev.Window, KindEntryFlags.Pulse);
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Append<TRows>(ref TRows rows, int row, LibraryFeeder* feeder, int slot, ushort timeline, int window, KindEntryFlags flags) where TRows : struct, IKindRows
    {
        var entry = rows.Append(row, feeder);
        if (entry == null) return;
        entry->Timeline = timeline;
        entry->Step = (ushort)window;
        entry->Slot = (byte)slot;
        entry->Flags = flags;
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 05_KindTable.cs

Path: `senior-feed/05_KindTable.cs`  (10260 bytes)

`````csharp
// ===== tl: src/Tl.Core/KindTable.cs  (kind rows: inline entry run + heap spill, Active bitset)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System.Runtime.InteropServices;

namespace Tl;

[Flags]
public enum KindEntryFlags : byte
{
    Span = 1,
    Pulse = 2,
    Synthetic = 4,
    Interrupted = 8,
    Ready = 16,
    Sample = 32,
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct KindEntry
{
    public ushort Timeline;
    public ushort Step;
    public byte Slot;
    public byte Feeder;
    public KindEntryFlags Flags;
    public byte Owner;
}

public unsafe struct KindTable
{
    public int Kind;
    public int ValueAt;
    public bool Grouped;
    public int Rows;
    public int Inline;
    public int Stride;
    public int Words;
    public int* Entity;
    byte* _inline;
    public ushort* Length;
    public ulong* Spilled;
    byte** _heap;
    public ulong* Active;
    public ulong* Enabled;
    public int MaxLength;
    public int Overflows;

    internal static KindTable* Create(int kind, ReadOnlySpan<int> entities, int inlineCapacity, int entryBytes, bool grouped = false)
    {
        var t = (KindTable*)Zeroed((nuint)sizeof(KindTable));
        t->Kind = kind;
        t->Grouped = grouped;
        t->ValueAt = grouped ? 12 : 8;
        t->Rows = entities.Length;
        t->Inline = inlineCapacity;
        t->Stride = entryBytes;
        t->Words = (t->Rows + 63) >> 6;
        t->Entity = (int*)Zeroed((nuint)t->Rows * 4);
        for (var r = 0; r < t->Rows; r++) t->Entity[r] = entities[r];
        t->_inline = (byte*)Zeroed((nuint)t->Rows * (nuint)inlineCapacity * (nuint)entryBytes);
        t->Length = (ushort*)Zeroed((nuint)t->Rows * 2);
        t->Spilled = (ulong*)Zeroed((nuint)t->Words * 8);
        if (inlineCapacity == 0) t->_heap = (byte**)Zeroed((nuint)t->Rows * (nuint)sizeof(nint));
        t->Active = (ulong*)Zeroed((nuint)t->Words * 8);
        t->Enabled = (ulong*)Zeroed((nuint)t->Words * 8);
        for (var w = 0; w < t->Words; w++) t->Enabled[w] = ~0ul;
        if ((t->Rows & 63) != 0) t->Enabled[t->Words - 1] = (1ul << (t->Rows & 63)) - 1;
        return t;
    }

    internal static void Free(KindTable* t)
    {
        if (t == null) return;
        if (t->_heap != null)
        {
            for (var r = 0; r < t->Rows; r++)
                if (t->_heap[r] != null) NativeMemory.AlignedFree(t->_heap[r]);
            NativeMemory.AlignedFree(t->_heap);
        }
        if (t->Inline != 0)
            for (var r = 0; r < t->Rows; r++)
                if (t->IsSpilled(r)) NativeMemory.AlignedFree(*(byte**)(t->_inline + (nuint)r * (nuint)t->Inline * (nuint)t->Stride));
        NativeMemory.AlignedFree(t->Entity);
        NativeMemory.AlignedFree(t->_inline);
        NativeMemory.AlignedFree(t->Length);
        NativeMemory.AlignedFree(t->Spilled);
        NativeMemory.AlignedFree(t->Active);
        NativeMemory.AlignedFree(t->Enabled);
        NativeMemory.AlignedFree(t);
    }

    static void* Zeroed(nuint bytes)
    {
        var p = NativeMemory.AlignedAlloc(bytes == 0 ? 1 : bytes, 64);
        NativeMemory.Clear(p, bytes);
        return p;
    }

    // Marginal bytes per row: the Entity slot (4 B), the Length (2 B) and
    // the inline run. The Spilled bit (1 bit per row) is bitset overhead.
    public long StorageBytesPerRow => 6 + (long)Inline * Stride;

    public bool IsActive(int row) => (Active[row >> 6] & (1ul << (row & 63))) != 0;

    bool IsSpilled(int row) => (Spilled[row >> 6] & (1ul << (row & 63))) != 0;

    void SetSpilled(int row, bool value)
    {
        if (value) Spilled[row >> 6] |= 1ul << (row & 63);
        else Spilled[row >> 6] &= ~(1ul << (row & 63));
    }

    // Heap blocks carry an 8 B header holding the capacity; the pointer
    // stored for a spilled row names the header.
    byte* HeapOf(int row) => Inline == 0 ? _heap[row] : *(byte**)(_inline + (nuint)row * (nuint)Inline * (nuint)Stride);

    int Capacity(int row) => IsSpilled(row) ? *(int*)HeapOf(row) : Inline;

    public byte* Data(int row)
    {
        if (IsSpilled(row)) return HeapOf(row) + 8;
        if (Inline == 0) return (byte*)0;
        return _inline + (nuint)row * (nuint)Inline * (nuint)Stride;
    }

    public KindEntry* At(int row, int index) => (KindEntry*)(Data(row) + (nuint)index * (nuint)Stride);

    public KindEntry* Append(int row)
    {
        var length = Length[row];
        if (length == Capacity(row)) Grow(row);
        Length[row] = (ushort)(length + 1);
        Active[row >> 6] |= 1ul << (row & 63);
        return At(row, length);
    }

    void Grow(int row)
    {
        var capacity = Math.Max(4, Capacity(row) * 2);
        if (capacity > ushort.MaxValue) throw new InvalidOperationException($"kind {Kind} row {row} holds more than 65,535 entries.");
        var raw = (byte*)NativeMemory.AlignedAlloc(8u + (nuint)capacity * (nuint)Stride, 64);
        NativeMemory.Clear(raw, 8u + (nuint)capacity * (nuint)Stride);
        *(int*)raw = capacity;
        var length = Length[row];
        if (length > 0)
            new ReadOnlySpan<byte>(Data(row), length * Stride).CopyTo(new Span<byte>(raw + 8, length * Stride));
        if (IsSpilled(row)) NativeMemory.AlignedFree(HeapOf(row));
        if (Inline == 0) _heap[row] = raw;
        else *(byte**)(_inline + (nuint)row * (nuint)Inline * (nuint)Stride) = raw;
        SetSpilled(row, true);
    }

    internal int ColdMaxLength() { var m = 0; for (var r = 0; r < Rows; r++) if (Length[r] > m) m = Length[r]; return m; }
    internal int ColdOverflows() { var s = 0; for (var r = 0; r < Rows; r++) if (IsSpilled(r)) s++; return s; }

    public ushort KindOf(KindEntry* entry) => Grouped ? *(ushort*)((byte*)entry + 8) : (ushort)Kind;

    public bool RemoveSpan(int row, byte slot, ushort timeline, ushort step, int kind = -1)
    {
        var length = Length[row];
        var data = Data(row);
        for (var i = 0; i < length; i++)
        {
            var e = (KindEntry*)(data + (nuint)i * (nuint)Stride);
            if ((e->Flags & KindEntryFlags.Span) == 0 || e->Slot != slot || e->Timeline != timeline || e->Step != step || (Grouped && kind >= 0 && KindOf(e) != kind)) continue;
            var tail = (length - i - 1) * Stride;
            Buffer.MemoryCopy(data + (nuint)(i + 1) * (nuint)Stride, data + (nuint)i * (nuint)Stride, tail, tail);
            Length[row] = (ushort)(length - 1);
            if (length == 1) Active[row >> 6] &= ~(1ul << (row & 63));
            return true;
        }
        return false;
    }

    public void Purge(int row, byte slot, ushort timeline)
    {
        var length = Length[row];
        var data = Data(row);
        var kept = 0;
        for (var i = 0; i < length; i++)
        {
            var entry = (KindEntry*)(data + (nuint)i * (nuint)Stride);
            if (entry->Slot == slot && entry->Timeline == timeline) continue;
            if (kept != i) Buffer.MemoryCopy(entry, data + (nuint)kept * (nuint)Stride, Stride, Stride);
            kept++;
        }
        Length[row] = (ushort)kept;
        if (kept == 0) Active[row >> 6] &= ~(1ul << (row & 63));
    }

    public void Sample(int row, byte slot, byte sample)
    {
        var length = Length[row];
        var add = 0;
        for (var i = 0; i < length; i++)
        {
            var e = At(row, i);
            if ((e->Flags & KindEntryFlags.Sample) == 0 && e->Slot == slot && e->Timeline != 0) add++;
        }
        if (add == 0) return;
        for (var i = 0; i < add; i++) Append(row);
        var data = Data(row);
        Buffer.MemoryCopy(data, data + (nuint)add * (nuint)Stride, (long)length * Stride, (long)length * Stride);
        var w = 0;
        var pulses = 0;
        for (var i = 0; i < length; i++)
        {
            var e = (KindEntry*)(data + (nuint)(add + i) * (nuint)Stride);
            if ((e->Flags & KindEntryFlags.Sample) != 0 || e->Slot != slot || e->Timeline == 0) continue;
            var copy = (KindEntry*)(data + (nuint)w++ * (nuint)Stride);
            Buffer.MemoryCopy(e, copy, Stride, Stride);
            copy->Slot = sample;
            copy->Flags = (copy->Flags | KindEntryFlags.Pulse | KindEntryFlags.Sample) & ~KindEntryFlags.Ready;
            if ((e->Flags & KindEntryFlags.Pulse) != 0)
            {
                e->Flags = (KindEntryFlags)0xff;
                pulses++;
            }
        }
        if (pulses == 0) return;
        var total = length + add;
        var kept = add;
        for (var i = add; i < total; i++)
        {
            var e = (KindEntry*)(data + (nuint)i * (nuint)Stride);
            if (e->Flags == (KindEntryFlags)0xff) continue;
            if (kept != i) Buffer.MemoryCopy(e, data + (nuint)kept * (nuint)Stride, Stride, Stride);
            kept++;
        }
        Length[row] = (ushort)kept;
    }

    public void Unsample(int row)
    {
        var length = Length[row];
        var data = Data(row);
        var kept = 0;
        for (var i = 0; i < length; i++)
        {
            var e = (KindEntry*)(data + (nuint)i * (nuint)Stride);
            if ((e->Flags & KindEntryFlags.Sample) != 0) continue;
            if (kept != i) Buffer.MemoryCopy(e, data + (nuint)kept * (nuint)Stride, Stride, Stride);
            kept++;
        }
        Length[row] = (ushort)kept;
        if (kept == 0) Active[row >> 6] &= ~(1ul << (row & 63));
    }

    public void RemovePulses(int row)
    {
        var length = Length[row];
        var data = Data(row);
        var kept = 0;
        for (var i = 0; i < length; i++)
        {
            var e = (KindEntry*)(data + (nuint)i * (nuint)Stride);
            if ((e->Flags & KindEntryFlags.Pulse) != 0) continue;
            if (kept != i) Buffer.MemoryCopy(e, data + (nuint)kept * (nuint)Stride, Stride, Stride);
            kept++;
        }
        Length[row] = (ushort)kept;
        if (kept == 0) Active[row >> 6] &= ~(1ul << (row & 63));
    }

    public void Clear(int row)
    {
        Length[row] = 0;
        Active[row >> 6] &= ~(1ul << (row & 63));
    }

    public void SetEnabled(int row, bool value)
    {
        if (value) Enabled[row >> 6] |= 1ul << (row & 63);
        else Enabled[row >> 6] &= ~(1ul << (row & 63));
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 06_TlWorkers.cs

Path: `senior-feed/06_TlWorkers.cs`  (4634 bytes)

`````csharp
// ===== tl: src/Tl.Core/TlWorkers.cs  (the .NET worker pool, semaphore wait)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
namespace Tl;
public sealed unsafe class TlWorkers : IDisposable
{
    readonly int _w;
    readonly Thread[]? _threads;
    readonly SemaphoreSlim _gate = new(0);
    readonly ManualResetEventSlim _done = new(false);
    delegate*<void*, int, int, int, void> _body;
    void* _state;
    int* _ends;
    int _chunks;
    int _next;
    int _d;
    Exception? _error;
    bool _disposed;
    public TlWorkers(int workers)
    {
        if (workers < 1) throw new ArgumentOutOfRangeException(nameof(workers));
        _w = workers;
        if (workers > 1)
        {
            _threads = new Thread[workers - 1];
            for (var i = 0; i < _threads.Length; i++) { var t = new Thread(WorkerLoop) { IsBackground = true, Name = "TlWorkers-" + i }; _threads[i] = t; t.Start(); }
        }
    }
    public int WorkerCount => _w;
    public static IntPtr Pin(object o) => GCHandle.ToIntPtr(GCHandle.Alloc(o));
    public static object? Target(IntPtr h) => GCHandle.FromIntPtr(h).Target;
    public static void Free(IntPtr h) => GCHandle.FromIntPtr(h).Free();
    public static void ClampRange(int n, int first, int last, out int firstEntity, out int endEntity)
    {
        var words = (n + 63) >> 6;
        firstEntity = first <= 0 ? 0 : first >= words ? n : first * 64;
        endEntity = last >= words ? n : last <= 0 ? 0 : last * 64;
        if (endEntity < firstEntity) endEntity = firstEntity;
    }
    public static void Split(ulong* bits, int words, int* ends, int chunks)
    {
        if (bits == null) throw new ArgumentNullException(nameof(bits));
        if (ends == null) throw new ArgumentNullException(nameof(ends));
        if (words < 0) throw new ArgumentOutOfRangeException(nameof(words));
        if (chunks <= 0) throw new ArgumentOutOfRangeException(nameof(chunks));
        var total = 0;
        for (var w = 0; w < words; w++) total += KindStore.Pop(bits[w]);
        var target = Math.Max(1, (total + chunks - 1) / chunks);
        var have = 0;
        var chunk = 0;
        for (var w = 0; w < words && chunk < chunks - 1; w++)
        {
            have += KindStore.Pop(bits[w]);
            if (have >= target) { ends[chunk++] = w + 1; have = 0; }
        }
        for (; chunk < chunks - 1; chunk++) ends[chunk] = words;
        ends[chunks - 1] = words;
    }
    public void Run(delegate*<void*, int, int, int, void> body, void* state, int* ends, int chunks)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TlWorkers));
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (chunks < 0) throw new ArgumentOutOfRangeException(nameof(chunks));
        if (chunks > 0 && ends == null) throw new ArgumentNullException(nameof(ends));
        if (chunks == 0) return;
        if (_w == 1) { for (var c = 0; c < chunks; c++) body(state, c == 0 ? 0 : ends[c - 1], ends[c], c); return; }
        _next = 0; _d = 0; _error = null; _body = body; _state = state; _ends = ends; _chunks = chunks; _done.Reset(); _gate.Release(_w - 1);
        while (true)
        {
            var c = Interlocked.Increment(ref _next) - 1;
            if (c >= chunks) break;
            try { body(state, c == 0 ? 0 : ends[c - 1], ends[c], c); }
            catch (Exception ex) { Interlocked.CompareExchange(ref _error, ex, null); }
        }
        _done.Wait();
        if (_error != null) ExceptionDispatchInfo.Capture(_error).Throw();
    }
    void WorkerLoop()
    {
        while (true)
        {
            try { _gate.Wait(); } catch (ObjectDisposedException) { return; }
            if (_disposed) return;
            var body = _body;
            var state = _state;
            var ends = _ends;
            var n = _chunks;
            while (true)
            {
                var c = Interlocked.Increment(ref _next) - 1;
                if (c >= n) break;
                try { body(state, c == 0 ? 0 : ends[c - 1], ends[c], c); }
                catch (Exception ex) { Interlocked.CompareExchange(ref _error, ex, null); }
            }
            if (Interlocked.Increment(ref _d) == _w - 1) _done.Set();
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_w > 1 && _threads != null) { try { _gate.Release(_w - 1); } catch (SemaphoreFullException) { } foreach (var t in _threads) t.Join(); }
        _gate.Dispose();
        _done.Dispose();
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 07_SystemDispatch.cs

Path: `senior-feed/07_SystemDispatch.cs`  (3626 bytes)

`````csharp
// ===== tl: src/Tl.Core/SystemDispatch.cs  (the ONLY runtime SIMD in Core: FastFoldAdd with Avx2.IsSupported)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
#if NET
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
using System.Collections.Generic;

namespace Tl;

public readonly struct SystemStep
{
    public SystemStep(int step, int consumer)
    {
        Step = step;
        Consumer = consumer;
    }

    public readonly int Step;
    public readonly int Consumer;
}

readonly struct FilterKey : System.IEquatable<FilterKey>
{
    public FilterKey(ulong system, ushort timeline, int stage)
    {
        System = system;
        Timeline = timeline;
        Stage = stage;
    }

    public readonly ulong System;
    public readonly ushort Timeline;
    public readonly int Stage;

    public bool Equals(FilterKey other) => System == other.System && Timeline == other.Timeline && Stage == other.Stage;
    public override bool Equals(object? obj) => obj is FilterKey other && Equals(other);
    public override int GetHashCode() => System.GetHashCode() ^ (Timeline.GetHashCode() * 397) ^ Stage.GetHashCode();
}

public static unsafe class SystemDispatch
{
    public static Span<SystemStep> FilteredSteps(TimelineWorld world, ulong system, ushort timeline, int stage, ReadOnlySpan<ulong> pairKeys)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var registry = world._systems ??= new SystemRegistry();
        registry.RefreshModules();
        var view = world._view;
        if (registry.ViewStamp != (nint)view.Timelines || registry.ViewBytes != view.ByteCount || registry.ViewCount != view.TimelineCount || registry.ViewGeneration != world._generation)
            registry.Reset((nint)view.Timelines, view.ByteCount, view.TimelineCount, world._generation);
        var key = new FilterKey(system, timeline, stage);
        if (registry.Filters.TryGetValue(key, out var cached)) return cached;
        var built = Build(world._view, timeline, stage, pairKeys);
        return registry.Filters.GetOrAdd(key, built);
    }

    static SystemStep[] Build(LibraryView view, ushort timeline, int stage, ReadOnlySpan<ulong> pairKeys)
    {
        var tl = LibraryViewReader.GetTimeline(in view, new TimelineId(timeline));
        if (tl == null || stage < 0 || stage >= tl->StageCount || pairKeys.Length == 0) return [];
        var steps = LibraryViewReader.StepsFor(tl, tl->Stages + stage);
        var list = new List<SystemStep>(steps.Length);
        for (var s = 0; s < steps.Length; s++)
        {
            var want = steps[s].PairKey;
            for (var c = 0; c < pairKeys.Length; c++)
                if (pairKeys[c] == want) list.Add(new SystemStep(s, c));
        }
        return list.ToArray();
    }

    public static void FastFoldAdd(float* lane, ushort* ticks, float* columns, int count)
    {
#if NET
        if (Avx2.IsSupported)
        {
            var i = 0;
            var bound = count & ~7;
            for (; i < bound; i += 8)
            {
                var narrow = Vector128.Load(ticks + i);
                var index = Vector256.Create(Vector128.WidenLower(narrow).AsInt32(), Vector128.WidenUpper(narrow).AsInt32());
                var gathered = Avx2.GatherVector256(lane, index, 4);
                var current = Vector256.Load(columns + i);
                Avx.Add(current, gathered).Store(columns + i);
            }
            for (; i < count; i++) columns[i] += lane[ticks[i]];
            return;
        }
#endif
        for (var i = 0; i < count; i++) columns[i] += lane[ticks[i]];
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 08_KindStore_TickPass_excerpt.cs

Path: `senior-feed/08_KindStore_TickPass_excerpt.cs`  (1651 bytes)

`````csharp
// ===== tl: src/Tl.Core/KindStore.cs lines 425-475 (TickPass / TickPassRange excerpt)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====

    internal void Sync()
    {
        if (_generation != _world._generation) Rebuild();
    }

    internal void TickPass(bool forward)
    {
        Sync();
        var stepDir = forward ? 1 : -1;
        TickPassRange(stepDir, 0, _players.PlayingWords);
        MergePulses();
    }

    internal void TickPassRange(int stepDir, int firstWord, int endWord)
    {
        var playing = _players.Playing;
        if (firstWord < 0) firstWord = 0; if (endWord > _players.PlayingWords) endWord = _players.PlayingWords;
        for (var w = firstWord; w < endWord; w++)
        {
            var bits = playing[w];
            while (bits != 0)
            {
                var e = (w << 6) + Tz(bits);
                bits &= bits - 1;
                TickEntity(e, stepDir);
            }
        }
    }


    void TickEntity(int e, int stepDir)
    {
        if (!KindRules.TickEntity(in _t, _players.SlotsOf(e), _players.CountOf(e), ref _kindRows, e, stepDir)) _players.SetPlaying(e, false);
    }

    void MergePulses()
    {
        for (var k = 0; k < _kindCount; k++)
        {
            var q = _queues + k;
            if (q->Count == 0) continue;
            var table = _tables[k];
            for (var n = 0; n < q->Count; n++)
            {
                var at = q->Items + (nuint)n * (nuint)q->Stride;
                var entity = *(int*)at;
                var row = table == null ? -1 : RowOf(entity, k);
                if (row < 0)
                {
                    DroppedPulses++;
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 09_Unity_TimelineSystems.cs

Path: `senior-feed/09_Unity_TimelineSystems.cs`  (5248 bytes)

`````csharp
// ===== tl.unity: com.tl.unity/Runtime/TimelineSystems.cs  (host systems: request gather/sort, tick scheduling)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Tl.Unity
{
    [UpdateInGroup(typeof(TlSimulationGroup))]
    public partial class TimelineSimulationGroup : ComponentSystemGroup
    {
    }

    [BurstCompile]
    [UpdateInGroup(typeof(TimelineSimulationGroup))]
    public partial struct TimelineRequestSystem : ISystem
    {
        EntityQuery _requestQuery;
        EntityQuery _closedQuery;
        int _sequence;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _requestQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TimelineRequest>().WithAllRW<Playback>().WithAbsent<TimelinePlaying>().Build(ref state);
            _closedQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TimelineLibrary>().WithAllRW<ClosedPlayback>().Build(ref state);
            state.RequireForUpdate<TimelineLibrary>();
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var view = SystemAPI.GetSingleton<TimelineLibrary>().View;
            if (!view.Kinds.IsEmpty)
                return;
            var entities = _requestQuery.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
                return;
            var manager = state.EntityManager;
            var gathered = new NativeList<global::Tl.RequestRecord>(entities.Length * 2, Allocator.Temp);
            for (var e = 0; e < entities.Length; e++)
            {
                var requests = manager.GetBuffer<TimelineRequest>(entities[e]);
                for (var r = 0; r < requests.Length; r++)
                    gathered.Add(new global::Tl.RequestRecord(entities[e].Index, requests[r].Slot, requests[r].Timeline, requests[r].At, requests[r].Op, _sequence++));
            }
            var scratch = new NativeArray<global::Tl.RequestRecord>(gathered.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            global::Tl.RequestQueue.Sort((global::Tl.RequestRecord*)gathered.GetUnsafePtr(), gathered.Length, (global::Tl.RequestRecord*)scratch.GetUnsafePtr());
            var closed = new NativeList<ClosedPlayback>(gathered.Length, Allocator.Temp);
            for (var i = 0; i < gathered.Length; i++)
            {
                var entry = gathered[i];
                var target = FindEntity(entities, entry.Entity);
                if (target == Entity.Null)
                    continue;
                var slots = new PlaybackSlots(manager.GetBuffer<Playback>(target), entry.Entity, closed);
                global::Tl.PlaybackRules.Apply(ref slots, view.Timelines, view.TimelineCount, entry.Op, entry.Timeline, entry.At);
                manager.GetBuffer<TimelineRequest>(target).Clear();
            }
            if (closed.Length == 0)
                return;
            var holders = _closedQuery.ToEntityArray(Allocator.Temp);
            if (holders.Length == 0)
                return;
            var sink = manager.GetBuffer<ClosedPlayback>(holders[0]);
            for (var i = 0; i < closed.Length; i++)
                sink.Add(closed[i]);
        }

        static Entity FindEntity(NativeArray<Entity> entities, int index)
        {
            for (var i = 0; i < entities.Length; i++)
                if (entities[i].Index == index)
                    return entities[i];
            return Entity.Null;
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(TimelineSimulationGroup))]
    [UpdateAfter(typeof(TimelineRequestSystem))]
    public partial struct TimelineAdvanceSystem : ISystem
    {
        EntityQuery _playbackQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _playbackQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<Playback>().WithAbsent<TimelinePlaying>().Build(ref state);
            state.RequireForUpdate<TimelineLibrary>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var view = SystemAPI.GetSingleton<TimelineLibrary>().View;
            if (!view.Kinds.IsEmpty)
                return;
            state.Dependency = TimelineJobs.ScheduleAdvance(ref state, _playbackQuery, view, state.Dependency);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(TimelineSimulationGroup))]
    [UpdateAfter(typeof(TimelineAdvanceSystem))]
    public partial struct TimelineEventSystem : ISystem
    {
        EntityQuery _closedQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _closedQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<ClosedPlayback>().Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var holders = _closedQuery.ToEntityArray(Allocator.Temp);
            var manager = state.EntityManager;
            for (var i = 0; i < holders.Length; i++)
                manager.GetBuffer<ClosedPlayback>(holders[i]).Clear();
        }
    }
}
`````

### tl internals excerpt (prepared 2026-11-11 from tl main) 10_Unity_generated_kind_job_excerpt.cs

Path: `senior-feed/10_Unity_generated_kind_job_excerpt.cs`  (8014 bytes)

`````csharp
// ===== tl.unity: generated Burst kind job (Tests/Kinds/Generated/TlUnity.Movement.g.cs lines 13-168 of 800; FloatMode.Strict already on)  (tl main 3e58a4f3 / tl.unity main 3b44b20c) =====
[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
public unsafe partial struct MovementChunkJob : IJobChunk
{
[global::Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestriction] public global::Tl.LibraryView View;
[global::Unity.Collections.LowLevel.Unsafe.NativeDisableUnsafePtrRestriction] public global::Tl.Unity.KindIndex Index;
public bool Forward;
[ReadOnly] public BufferTypeHandle<global::Tl.Unity.Playback> PlaybackHandle;
public ComponentTypeHandle<Movement_MotionData> MotionHandle;
[ReadOnly] public ComponentTypeHandle<Movement_WalkStatData> WalkStatHandle;
[ReadOnly] public ComponentTypeHandle<Movement_JumpPowerData> JumpPowerHandle;
[ReadOnly] public ComponentTypeHandle<Movement_TargetData> TargetHandle;
public ComponentTypeHandle<Movement_StaminaData> StaminaHandle;
public global::KindGame.AnchorLookup Anchors;
public BufferTypeHandle<global::Tl.Unity.TimelineEvent<global::KindGame.Cue>> CuesEventHandle;
public BufferTypeHandle<global::KindGame.LiftEntry> LiftEntryHandle;
public ComponentTypeHandle<global::KindGame.LiftActive> LiftActiveHandle;
[ReadOnly] public ComponentTypeHandle<global::KindGame.LiftEnabled> LiftEnabledHandle;
public BufferTypeHandle<global::KindGame.LocomotionEntry> LocomotionEntryHandle;
public ComponentTypeHandle<global::KindGame.LocomotionActive> LocomotionActiveHandle;
[ReadOnly] public ComponentTypeHandle<global::KindGame.LocomotionEnabled> LocomotionEnabledHandle;
public BufferTypeHandle<global::KindGame.StatChangeEntry> StatChangeEntryHandle;
public ComponentTypeHandle<global::KindGame.StatChangeActive> StatChangeActiveHandle;
[ReadOnly] public ComponentTypeHandle<global::KindGame.StatChangeEnabled> StatChangeEnabledHandle;
public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in global::Unity.Burst.Intrinsics.v128 chunkEnabledMask)
{
var __l0 = false;
if (chunk.Has(ref LiftEntryHandle))
{
var __a0 = chunk.GetEnableableBits(ref LiftActiveHandle);
var __e0 = chunk.GetEnableableBits(ref LiftEnabledHandle);
__l0 = ((__a0.ULong0 & __e0.ULong0) | (__a0.ULong1 & __e0.ULong1)) != 0;
}
var __l1 = false;
if (chunk.Has(ref LocomotionEntryHandle))
{
var __a1 = chunk.GetEnableableBits(ref LocomotionActiveHandle);
var __e1 = chunk.GetEnableableBits(ref LocomotionEnabledHandle);
__l1 = ((__a1.ULong0 & __e1.ULong0) | (__a1.ULong1 & __e1.ULong1)) != 0;
}
var __l2 = false;
if (chunk.Has(ref StatChangeEntryHandle))
{
var __a2 = chunk.GetEnableableBits(ref StatChangeActiveHandle);
var __e2 = chunk.GetEnableableBits(ref StatChangeEnabledHandle);
__l2 = ((__a2.ULong0 & __e2.ULong0) | (__a2.ULong1 & __e2.ULong1)) != 0;
}
var __x = chunk.Has(ref CuesEventHandle);
if (!__l0 && !__l1 && !__l2 && !__x) return;
var __playbacks = chunk.GetBufferAccessor(ref PlaybackHandle);
if (__x) Events(chunk, __playbacks);
var __c0 = chunk.Has(ref MotionHandle) ? (global::KindGame.Motion*)chunk.GetRequiredComponentDataPtrRW(ref MotionHandle) : null;
var __c1 = chunk.Has(ref WalkStatHandle) ? (global::KindGame.WalkStat*)chunk.GetRequiredComponentDataPtrRO(ref WalkStatHandle) : null;
var __c2 = chunk.Has(ref JumpPowerHandle) ? (global::KindGame.JumpPower*)chunk.GetRequiredComponentDataPtrRO(ref JumpPowerHandle) : null;
var __c3 = chunk.Has(ref TargetHandle) ? (global::KindGame.Target*)chunk.GetRequiredComponentDataPtrRO(ref TargetHandle) : null;
var __c4 = chunk.Has(ref StaminaHandle) ? (global::KindGame.Stamina*)chunk.GetRequiredComponentDataPtrRW(ref StaminaHandle) : null;
if (__l0 && __c0 != null && __c2 != null) Kind_0(chunk, __playbacks, __c0, __c1, __c2, __c3, __c4);
if (__l1 && __c0 != null && __c1 != null && __c3 != null) Kind_1(chunk, __playbacks, __c0, __c1, __c2, __c3, __c4);
if (__l2 && __c4 != null) Kind_2(chunk, __playbacks, __c0, __c1, __c2, __c3, __c4);
}
void Kind_0(in ArchetypeChunk chunk, BufferAccessor<global::Tl.Unity.Playback> __playbacks, global::KindGame.Motion* __c0, global::KindGame.WalkStat* __c1, global::KindGame.JumpPower* __c2, global::KindGame.Target* __c3, global::KindGame.Stamina* __c4)
{
if (!chunk.Has(ref LiftEntryHandle)) return;
var __acc = chunk.GetBufferAccessor(ref LiftEntryHandle);
var __ema = chunk.GetEnableableBits(ref LiftActiveHandle);
var __eme = chunk.GetEnableableBits(ref LiftEnabledHandle);
var __stride = sizeof(global::KindGame.LiftEntry);
var __count = chunk.Count;
for (var __kw = 0; __kw < 2; __kw++)
{
if (__kw == 1 && __count <= 64) break;
var __kb = __kw == 0 ? __ema.ULong0 & __eme.ULong0 : __ema.ULong1 & __eme.ULong1;
while (__kb != 0)
{
var __row = (__kw << 6) + (int)global::Unity.Mathematics.math.tzcnt(__kb);
__kb &= __kb - 1;
if (__row >= __count) break;
var __buf = __acc[__row];
var __len = __buf.Length;
byte* __data = (byte*)__buf.GetUnsafePtr();
var __pulses = 0;
var __pbase = (global::Tl.Playback*)__playbacks[__row].GetUnsafeReadOnlyPtr();
var __sampled = global::Tl.KindRules.IsSampled(__data, __len);
if (!__sampled)
{
float __total = default;
var __has = false;
for (var __j = 0; __j < __len; __j++)
{
var __en = (global::Tl.KindEntry*)(__data + (long)__j * __stride);
float __v = default;
if (__en->Timeline == 0)
{
__v = *(float*)((byte*)__en + 8);
__pulses++;
}
else
{
var __isPulse = (__en->Flags & global::Tl.KindEntryFlags.Pulse) != 0;
if (__isPulse) __pulses++;
if (__en->Owner != 0)
{
if ((__en->Flags & global::Tl.KindEntryFlags.Ready) == 0) continue;
__v = *(float*)((byte*)__en + 8);
__en->Flags &= ~global::Tl.KindEntryFlags.Ready;
}
else
{
var __pb = __pbase + __en->Slot;
if (!__isPulse && (__pb->Flags & global::Tl.PlaybackFlags.Paused) != 0) continue;
var __tl = Index.Timelines + (__en->Timeline - 1);
ushort __tick = __pb->Tick;
var __backward = Forward ? (__pb->Flags & global::Tl.PlaybackFlags.Reverse) != 0 : (__pb->Flags & global::Tl.PlaybackFlags.Reverse) == 0;
var __kflags = (__backward ? global::Tl.FrameFlags.Reverse : global::Tl.FrameFlags.None)
| ((__en->Flags & global::Tl.KindEntryFlags.Synthetic) != 0 ? global::Tl.FrameFlags.Synthetic : global::Tl.FrameFlags.None)
| ((__en->Flags & global::Tl.KindEntryFlags.Interrupted) != 0 ? global::Tl.FrameFlags.Interrupted : global::Tl.FrameFlags.None);
if (__tick == 0) __kflags |= global::Tl.FrameFlags.TimelineStart;
if (__tick == (ushort)(__tl->Duration - 1)) __kflags |= global::Tl.FrameFlags.TimelineEnd;
var __ok = false;
switch (__en->Feeder)
{
case 0:
{
var __def = Index.StepOf(__en->Timeline, __en->Step, __tick);
if (__def == null || __tick < __def->WindowStart || __tick >= __def->WindowEnd) break;
byte* __row1 = (byte*)__def->FrameRow;
byte* __first1 = __row1 + sizeof(global::KindGame.ArcTrack);
byte* __second1 = __def->FactorSpan == 0 ? null : __first1 + sizeof(global::KindGame.ArcClip);
float __blend1 = __def->FactorSpan == 0 ? 0f : (__def->FactorSpan <= 1 ? 0.5f : (__tick - __def->FactorStart) / (float)(__def->FactorSpan - 1));
ushort __len1 = (ushort)(__def->WindowEnd - __def->WindowStart);
ushort __within1 = (ushort)(__tick - __def->WindowStart);
sbyte __dir1 = __backward ? (sbyte)-1 : (sbyte)1;
global::TlUnityGenerated.TlUnityFrames.Frame_0_1(__row1, __first1, __second1, __blend1, __tick, __len1, __within1, (ushort)__def->TrackIndex, (ushort)__tl->Duration, __dir1, __kflags, out var __t1, out var __cc1, out var __f1);
byte* __lane1 = (byte*)__def->FoldLane + (((uint)__tick - __def->WindowStart) * 2u + (__backward ? 1u : 0u)) * (uint)4;
float __fd1_0 = default;
if (__def->FoldLane == null)
{
global::KindGame.Jump.Fold(in __f1, out __fd1_0);
}
else
{
__fd1_0 = *(float*)(__lane1 + 0);
}
__v = global::KindGame.Jump.Evaluate(in __fd1_0, in __c2[__row]);
__ok = true;
break;
}
}
if (!__ok) continue;
}
}
if (__has) global::KindGame.Lift.Combine(ref __total, in __v);
else { __total = __v; __has = true; }
}
if (__has) global::KindGame.Lift.Apply(in __total, ref __c0[__row]);
}
else
`````

### tl.unity docs/KINDS.md

Path: `tl.unity/docs/KINDS.md`  (17641 bytes)

`````
# Tl kinds in Unity (`com.tl.unity`)

How game-defined per-step work runs on the Unity host: the kind model, the
step order, authoring in a SubScene, sizing (kind sets, capacities), the
capacity bake, the Kind Report, and the samples with their cross-host hashes.

## Kind model

A kind is a value type plus a `Combine` rule (or `Each`) plus an `Apply`
method. A clip feeds a kind through a `[Feeds]` method evaluated every step
while the clip is active; a `[Pulse]` method fires a one-step value at clip
start, clip end, or a marker. Game code sends one-step values through
`KindPulses<K>.To(entity, value)`. The test game (`Tests/Kinds/Game`) defines:

| Kind | Value | Combine | Apply target | Fed / pulsed by |
|---|---|---|---|---|
| Locomotion | `Speed` (`Value`, `Priority`) | highest priority wins | `Motion.X` | `Walk.Evaluate` (reads `WalkStat`, `Target`, `AnchorLookup`) |
| Lift | `float` | sum | `Motion.Y` | `Jump` fold + `Evaluate` (reads `JumpPower`) |
| Damage | `int` | sum | `Health` | `Hit` start pulse; game code via `KindPulses<int>.To` |
| StatChange | `int` | each applied once | `Stamina` | `ChangeStat` start pulse, `Steps` marker pulse |

`Locomotion` shows overriding by priority (a faster high-priority entry
shadows the walk until its clip ends). `Lift` shows stacking (jump plus two
potion hops sum into one value per step). `Damage` shows game-code pulses
summed with timeline hit pulses into one `Health` write per step. `StatChange`
shows `Each` mode: every pulse applies exactly once, in a fixed merge order.

A kind is named by its type name (`Damage`). That name is what the authoring
fields, `[RequiresKinds]` and `library.json` use.

## Step order

Each step runs `TimelineKindTickSystem` first, then the kind loops inside each
system (`Movement` runs Locomotion, Lift, StatChange; `Vitals` runs Damage):

1. `Tick`: apply start/stop requests, advance playback ticks, maintain span
   entries (span start/end from boundaries, marker pulses), merge queued game
   pulses in a deterministic order, handle end/next/hold/finish, maintain the
   `Active`/`Enabled` row state.
2. Kind loops: one loop per kind per chunk over that kind's entry buffer.
   Rows whose kind row is disabled (`Enabled` off, or `Active` off) contribute
   nothing but keep their entries.
3. `Apply`: each kind writes its owner's data (`Motion`, `Health`, `Stamina`).

Your kind systems run after the Tick:

```csharp
[UpdateInGroup(typeof(TimelineSimulationGroup))]
[UpdateAfter(typeof(TimelineKindTickSystem))]
public partial class MovementSystem : SystemBase
```

`Tests/Kinds/Harness/KindWorld.cs` has the full `Movement` and `Vitals` pair.

An entity that carries `TimelinePlaying` belongs to the kind Tick.
`TimelineRequestSystem` and `TimelineAdvanceSystem` skip it. The Timeline
Player bake adds `TimelinePlaying` (disabled) to every player; the Tick
enables it when it applies a request.

Send game pulses before the step (`Damage(t, v)` then `Step()`); they merge
during that step's Tick. Pulses to an entity without the kind are counted in
`Tick.DroppedPulses` and dropped, as are starts for unreachable timelines
(`Tick.DroppedStarts`). Both are public on `TimelineKindTickSystem`. A count
above zero usually means a missing `[RequiresKinds]`, Kinds list entry or May
Start name (see Authoring).

## Authoring in a SubScene

A GameObject runs kinds when a Timeline Player is baked onto it. The baker
gives the entity the playback buffers and exactly the kinds it can reach.
Baking only runs for GameObjects in a SubScene.

Before you start you need:

- A game assembly that declares the kinds, feeders and pulses. Its asmdef
  references `Tl.Unity` and `Tl.Unity.Authoring` (the attribute, the
  components and the assets live in `Tl.Unity.Authoring`, a runtime
  assembly). `Tests/Kinds/Tl.Unity.Kinds.asmdef` lists the full references.
- A folder with `library.json`, the timeline `.json` files and the baked
  `game.tlib`, plus the generated ids class (one `ushort` constant per
  timeline, named in PascalCase: `potion_hop.json` gives `PotionHop`).
  `tlb library <folder>` writes `game.tlib` into the folder, and
  `Timelines.g.cs` and `Kinds.g.cs` beside it. Pass `--ids` and `--kinds` to
  put them where your game assembly expects them, and use the same file for
  `--kinds` as for the library's Kinds File, so only one `KindCapacity`
  exists. Its options are in the tl repository, `docs/library-layout.md`.
- The game's data components that the kinds' systems read and write (in the
  test game, `RunnerAuthoring` bakes `Movement_MotionData`,
  `Vitals_HealthData` and the other columns).

### Step 1: create the Timeline Library asset

`Assets > Create > Tl > Timeline Library`. One asset per timeline folder.
Fill its fields:

| Field | Enter |
|---|---|
| Folder | The folder with `library.json`, the timeline files and `game.tlib`. Project-relative; `Packages/...` paths work. |
| Timeline Ids | Full name of the generated ids class, for example `Game.Timelines`. |
| Game Assembly | Name of the script assembly with your kinds, for example `Game.Logic`. |
| Namespace | Namespace of the generated `KindCapacity` class. Default `Game`. |
| Kinds File | Project path where the capacity bake writes `Kinds.g.cs`. Leave empty to skip the capacity bake for this library. |
| Tlb | The `tlb` command, or the path to `tlb` or `Tl.Bake.dll`. |
| Kind Sets | `Exact` (default) or `Union Per Profile`. See Step 4. |

Timeline Ids, Game Assembly and Folder decide what the Timeline Player can
resolve. Namespace, Kinds File, Game Assembly and Tlb matter only for the
capacity bake.

### Step 2: add a Timeline Player

Select a GameObject in a SubScene. `Add Component`, search for Timeline
Player (the component is `TimelinePlayerAuthoring`). There is one per
GameObject.

| Field | Enter |
|---|---|
| Library | The Timeline Library asset from Step 1. |
| Start With | Timeline names started when the entity spawns, in list order. |
| May Start | Timeline names game code may start on this entity later. |
| Kinds | Kinds game code sends pulses to on this entity (see Step 3). |
| Profile | A Kind Profile. Used only with `Union Per Profile` (see Step 4). |

Timeline names are the constant names of the Timeline Ids class. Kind names
are kind type names. A name that does not exist is a baking error.

The baker puts this on the entity:

- `Playback` and `TimelineRequest` buffers, with one start request per Start
  With name.
- `TimelinePlaying`, disabled.
- For each reached kind: its entry buffer, `<Kind>Active` (disabled) and
  `<Kind>Enabled` (enabled).

A kind is reached when it is:

- in the library kind mask of a Start With or May Start timeline, or of any
  timeline on that timeline's `next` chain;
- in the Kinds list;
- named by `[RequiresKinds]` on another component on the same GameObject;
- in the Profile, with `Union Per Profile`.

Kinds that are not reached are not on the entity. A start for a timeline
whose kinds the entity lacks is dropped (`DroppedStarts`). Starting a May
Start timeline is game code's job: add a `TimelineRequest.Start` to the
entity's request buffer.

The baker depends on the Library and Profile assets, so editing them bakes
again. It does not depend on `game.tlib`. After rebuilding `game.tlib`,
reimport the SubScene.

### Step 3: name the kinds game code pulses

A timeline reaches its own kinds. A kind that only game code pulses (such as
`Damage` from a bullet) has no timeline on that entity, so name it.

Preferred: `[RequiresKinds]` on a component on the same GameObject. It takes
the kind types, so a renamed kind is a compile error:

```csharp
using Tl.Unity;
using UnityEngine;

[RequiresKinds(typeof(Damage))]
public sealed class DamageableAuthoring : MonoBehaviour
{
}
```

`[RequiresKinds(params Type[])]` is in namespace `Tl.Unity`, applies to
classes and is inherited. Several kinds go in one attribute.

For a one-off, type the kind name in the Timeline Player's Kinds list.

### Step 4: share archetypes with a profile (optional)

With `Exact`, each entity carries exactly its reached kinds, so entities with
different kind sets are in different archetypes. That is the least memory and
the most archetypes. To make several kinds of GameObject share one archetype:

1. On the Timeline Library asset, set Kind Sets to `Union Per Profile`.
2. `Assets > Create > Tl > Kind Profile`. Fill Kinds with kind names.
3. Set the Profile field on each Timeline Player that should share.

A player with a Profile gets its reached kinds plus every kind of the
profile. Players without a profile stay exact. With `Exact` the Profile field
is ignored. If a player reaches a kind its profile lacks, the bake warns and
that player gets its own archetype. Fewer archetypes cost more bytes per
entity; the Kind Report shows both.

### Step 5: read baking errors

Messages start with `Tl baking: GameObject '<name>'`. The bake keeps going
and the entity is baked with what could be resolved.

| Message | Fix |
|---|---|
| `has no Library.` | Set the Library field. |
| `library '<name>' names timeline ids class '<class>', which is not compiled.` | Fix Timeline Ids and Game Assembly, or generate the ids class. |
| `start-with names timeline '<name>', which <ids class> does not declare.` | Fix the name (also `may-start`). |
| `the Kinds list names kind '<name>', which no Unity system runs.` | Use the kind type name. The same text appears for `[RequiresKinds]` and profile names. |
| `timeline '<name>' feeds kind '<kind>', which no Unity system runs.` | The library has a kind the compiled game code does not register. Recompile the game assembly and rebuild `game.tlib` from the same code. |
| `gets kind '<kind>' from <source>, fed by <feeders>, but has no '<column>' component.` | Add the component whose baker adds that column to the same GameObject. `<source>` says why the kind was reached; `<feeders>` is `game code` when no timeline feeds it. |
| `: Timeline library '<name>' has no game.tlib in '<folder>'.` | Bake `game.tlib` into Folder. |

One warning exists: `reaches kind '<kind>' through <source>, which profile
'<profile>' lacks, so it gets its own archetype.` Add the kind to the profile
or accept the extra archetype.

Example, from `Tests/Kinds/EditorTests/KindBakingTests.cs`:

```
Tl baking: GameObject 'Bare' gets kind 'Locomotion' from timeline 'Run', fed by Walk.Evaluate, but has no 'Movement_MotionData' component.
```

The column check runs after all bakers, so the component can come from any
baker on that GameObject.

### Step 6: load the library at runtime

The world needs the library before the first step:

```csharp
TlGeneratedBootstrap.InstallDiscoveredModules();
TimelineLibraryBootstrap.Ensure(World.DefaultGameObjectInjectionWorld.EntityManager, "game.tlib", bytes);
```

`bytes` is the content of `game.tlib`. Call
`TimelineLibraryBootstrap.Shutdown(entityManager)` when it goes away.
`Tests/Kinds/Game/KindLibraryBootstrap.cs` does both from a MonoBehaviour
next to the SubScene; the test game embeds the bytes in a generated class.

## Sizing

Every entity pays a header plus inline entries for each kind it can reach.
Step 2 assigns exactly the reachable kinds, so in the test game a walker
carries Locomotion only. Kind sets (Step 4) trade archetypes against bytes
per entity.

Each kind's capacity comes from `Kinds.g.cs`: one `const int` per kind in
`KindCapacity`, used as the `[InternalBufferCapacity]` of the kind's entry
buffer. `tlb kinds` computes it as the worst case on one entity (span entries
from the timelines, timeline pulses, plus `gamePulses` for game-code pulses),
capped by `maxInlineBytes`. `inline` sets a capacity directly. These are
set per kind in the `kinds` section of `library.json`; the test library has:

```json
"kinds": {
  "Damage": { "gamePulses": 8, "maxInlineBytes": 132 }
}
```

and `Tests/Kinds/Game/Kinds.g.cs` pins Damage 11, Lift 2, Locomotion 2,
StatChange 3. The capacity rules and the `kinds` keys are in the tl
repository, `docs/library-layout.md`, section "Kinds (library version 2)".

Rules of thumb: tens of kinds, not hundreds; capacity 0 for rare kinds; let
the report guide the rest.

## Capacity bake

The capacity bake keeps `Kinds.g.cs` current. It is an Editor step; the
game does not run it.

Setup:

1. Use a `tlb` that has the `kinds` command. `tl.bake` 2.0.1 on NuGet is too
   old. Build `tools/Tl.Bake` in the tl repository, or use a newer package.
2. On the Timeline Library asset, set Tlb to the `tlb` command, or to the
   path of `Tl.Bake.dll` (it is run with `dotnet`). Set Game Assembly,
   Namespace and Kinds File.
3. `Tools > Tl > Bake Kind Capacities`.

For each Timeline Library asset in the project that has a Kinds File, the
bake copies the compiled script assemblies into `Temp/TlKindBake` and runs:

```
tlb kinds --library <Folder> --namespace <Namespace> --out <KindsFile> --assembly <staged game dll> --diagnostics json
```

`tlb` rewrites `Kinds.g.cs` only when a capacity changes. When it did, the
bake refreshes the AssetDatabase and scripts recompile. Otherwise nothing
changes and nothing recompiles. Either way the Console lists each kind: its
capacity, its worst case, what set it, and its inline bytes.

The bake also runs by itself:

- after every script reload;
- when a `.json` file in a library's Folder is imported, moved or deleted.

After a capacity changes, the reload it causes runs the bake again and finds
nothing to write. A Folder whose name ends in `~` is not imported by Unity,
so the `.json` trigger does not fire there; use the menu. Libraries with an
empty Kinds File are skipped.

Until `Kinds.g.cs` exists, the generator warns `TLUNITY9103` and inline
capacity is 0. The first bake writes the file.

Failures are logged as `Tl kind capacities: library '<name>': <message>` and
leave `Kinds.g.cs` as it was. The usual ones:

- `Script assembly '<name>' is not compiled yet.` Fix Game Assembly, or wait
  for the compile.
- `could not run '<tlb>' ...` Fix the Tlb field.
- `tlb exited <code>: ...` The text is tlb's own error (a missing
  `library.json`, an unknown `kinds` key, and so on).

The EditMode test `EditingATimelineWithoutChangingACapacityRewritesNothing`
reads `TL_TLB` (the same value as the Tlb field) and is ignored when it is
not set.

## Kind Report

`Tools > Tl > Kind Report` logs what the kinds cost in the entities that
exist. To use it:

1. Open a scene whose SubScene bakes Timeline Players, or enter Play mode.
2. Run the menu item.
3. Read the Console. There is one block per world that has entities with
   kinds. A block that has warnings is logged as a warning.

Each row is one archetype that carries at least one kind:

```
<kinds>: <entities> entities in <chunks> chunks of <chunk capacity>, <n> kinds, <bytes> B kind storage per entity
```

`<kinds>` are the kind names joined with `+`. Kind storage is the size the
kind entry buffers take in the chunk, per entity. Compare `entities` with
`chunks` times `chunk capacity`: many mostly empty chunks mean too many
archetypes, and a profile merges them.

Three warnings:

- More than 32 kinds on one archetype. Give the entity fewer kinds, or split
  the entity.
- More than 1024 B of kind storage per entity. Lower capacities, give rare
  kinds capacity 0, or take kinds out of a profile.
- Fewer than 16 entities per chunk. Per-entity storage is large; fix it as
  for the 1024 B warning.

With no entities that carry kinds, the menu logs: no world has entities with
kinds.

The report logic is in the runtime assembly, so game code can call it:
`KindReport.Capture(entityManager, KindRegistry.Discover())` returns the
rows, `KindReport.Warnings(rows)` the warning strings, and
`KindReport.Text(worldName, rows)` the text above. The limits are
`KindReport.MaxKinds`, `MaxKindBytes` and `MinChunkCapacity`.

## Samples

Both samples live in `com.tl.unity/Samples~`, run headless (`-batchmode`
quits 0 on match, 3 on mismatch), and print `hash expected match backend`
receipts.

- `KindsIL2CPP` (`KindsSample`): the Runner on kinds. A speed boost overrides
  the normal walk by priority (X 2 then 7, two entries, 102 over 19 steps,
  run takes over at 104 with one entry), lifts add up (Y 44, then 84 with a
  stacked potion hop), stat pulses apply once (Stamina -1, then 9 from the
  marker). Then the 600-step random script: hash `13943614770702343869`,
  equal to the .NET `KindUnityHashTests` run.
- `BulletsIL2CPP` (`BulletsSample`): game-code `Damage` pulses plus a `Stomp`
  timeline's hit pulses summed into one `Health` write per step (993 after the
  game pulse, 955 after two game pulses land with the timeline hit, holds at
  955, 945 on the second hit, untouched rows stay 1000, zero dropped
  pulses). Step hash over all four rows' `Health`: `3081412629212866412`,
  reproduced from the .NET `KindRunTests` bullets script.

## Legacy mixing

Plan-1 `ExecuteActive` consumers keep plan 1's order inside one implicit
legacy kind that runs first in its system unless `Order` says otherwise.
Mixing legacy consumers and kinds that write the same field changes the order
between those two groups. On Unity, legacy consumers are not dispatched yet
(`TLUNITY9105`: feed a kind instead). The legacy implicit kind and
`ExecuteActive` stay (K5 decision, IAFahim/tl#558). They go only after the
Runner sample and the bench domains move to kinds, a migration tool exists,
and K-E still passes without the legacy loop.

## Not covered

The authoring, bake and report code is Editor-side code with EditMode tests
(`Tests/Kinds/EditorTests`). This page makes no claim about player builds,
other Unity versions or other `tlb` versions.
`````

### tl.unity Tests/Kinds/Game/KindGame.cs

Path: `tl.unity/com.tl.unity/Tests/Kinds/Game/KindGame.cs`  (4593 bytes)

`````csharp
using System;
using Tl;
using Unity.Collections;
using Unity.Entities;

namespace KindGame
{

public struct Motion { public float X, Y; }
public struct Health { public int Value; }
public struct Stamina { public int Value; }
public struct WalkStat { public float Value; }
public struct JumpPower { public float Value; }
public struct Speed { public float Value; public byte Priority; }
public struct Target { public Entity Value; }
public struct Anchor : IComponentData { public float Boost; }

public struct AnchorLookup
{
    [ReadOnly] public ComponentLookup<Anchor> Anchors;

    public float Boost(in Target target) => Anchors.TryGetComponent(target.Value, out var anchor) ? anchor.Boost : 1f;
}

public struct WalkClip { public float Speed; public byte Priority; }
public struct WalkTrack : IBlend<WalkClip>
{
    public float Scale;
    public void Blend(in WalkClip first, in WalkClip second, float factor, out WalkClip result) => result = first;
}

public struct LegacyWalkClip { public float Speed; }
public struct LegacyWalkTrack : IBlend<LegacyWalkClip>
{
    public float Scale;
    public void Blend(in LegacyWalkClip first, in LegacyWalkClip second, float factor, out LegacyWalkClip result) => result = first;
}

public struct ArcClip { public float Velocity; }
public struct ArcTrack : IBlend<ArcClip>
{
    public float Scale;
    public void Blend(in ArcClip first, in ArcClip second, float factor, out ArcClip result) => result = first;
}

public struct HitClip { public int Amount; }
public struct HitTrack : IBlend<HitClip>
{
    public float Scale;
    public void Blend(in HitClip first, in HitClip second, float factor, out HitClip result) => result = first;
}

public struct StatClip { public int Delta; }
public struct StatTrack : IBlend<StatClip>
{
    public float Scale;
    public void Blend(in StatClip first, in StatClip second, float factor, out StatClip result) => result = first;
}

public struct StepMarker { public int Delta; }

[TimelineKind]
public struct Locomotion
{
    public static void Combine(ref Speed total, in Speed next) { if (next.Priority >= total.Priority) total = next; }
    public static void Apply(in Speed total, ref Motion motion) => motion.X += total.Value;
}

[TimelineKind]
public struct Lift
{
    public static void Combine(ref float total, in float next) => total += next;
    public static void Apply(in float total, ref Motion motion) => motion.Y += total;
}

[TimelineKind(Accepts = KindEntries.Pulses)]
public struct Damage
{
    public static void Combine(ref int total, in int next) => total += next;
    public static void Apply(in int total, ref Health health) => health.Value -= total;
}

[TimelineKind(Accepts = KindEntries.Pulses, Mode = KindMode.Each)]
public struct StatChange
{
    public static void Apply(in int delta, ref Stamina stamina) => stamina.Value += delta;
}

public struct Walk : ITrack<WalkTrack, WalkClip>
{
    [Feeds(typeof(Locomotion))]
    public static Speed Evaluate(in Frame<WalkTrack, WalkClip> f, in WalkStat stat, in Target target, in AnchorLookup anchors)
        => new Speed { Value = f.Direction * f.Clip.Speed * f.Track.Scale * stat.Value * anchors.Boost(in target), Priority = f.Clip.Priority };
}

public struct Jump : ITrack<ArcTrack, ArcClip>
{
    public static void Fold(in Frame<ArcTrack, ArcClip> f, out float lift) => lift = f.Direction * f.Clip.Velocity * f.Track.Scale;

    [Feeds(typeof(Lift))]
    public static float Evaluate(in float lift, in JumpPower power) => lift * power.Value;
}

[TimelineSystemMember(typeof(Vitals))]
public struct Hit : ITrack<HitTrack, HitClip>
{
    [Pulse(typeof(Damage), On = ClipEvent.Start)]
    public static int Start(in Frame<HitTrack, HitClip> f) => f.Direction > 0 ? f.Clip.Amount : 0;
}

[TimelineSystemMember(typeof(Movement))]
public struct ChangeStat : ITrack<StatTrack, StatClip>
{
    [Pulse(typeof(StatChange), On = ClipEvent.Start)]
    public static int Start(in Frame<StatTrack, StatClip> f) => f.Direction * f.Clip.Delta;
}

[TimelineSystemMember(typeof(Movement))]
public struct Steps : IMarker<StepMarker>
{
    [Pulse(typeof(StatChange), On = ClipEvent.Marker)]
    public static int OnStep(in MarkerFrame<StepMarker> m) => m.Marker.Delta;
}

[TimelineSystem]
public ref partial struct Movement
{
    public Span<Motion> Motion;
    public ReadOnlySpan<WalkStat> WalkStat;
    public ReadOnlySpan<JumpPower> JumpPower;
    public ReadOnlySpan<Target> Target;
    public AnchorLookup Anchors;
    public Span<Stamina> Stamina;
}

[TimelineSystem]
public ref partial struct Vitals
{
    public Span<Health> Health;
}

}
`````

### tl.unity Authoring/TimelinePlayerAuthoring.cs

Path: `tl.unity/com.tl.unity/Authoring/TimelinePlayerAuthoring.cs`  (896 bytes)

`````csharp
using System;
using UnityEngine;

namespace Tl.Unity
{
    [DisallowMultipleComponent]
    public sealed class TimelinePlayerAuthoring : MonoBehaviour
    {
        [Tooltip("The library whose timelines this player runs.")]
        public TimelineLibraryAsset Library;

        [Tooltip("Timelines started when the entity spawns, by name.")]
        public string[] StartWith = Array.Empty<string>();

        [Tooltip("Other timelines game code may start on this entity, by name. Their kinds and their next chains are added too.")]
        public string[] MayStart = Array.Empty<string>();

        [Tooltip("Kinds game code sends pulses to on this entity, by name.")]
        public string[] Kinds = Array.Empty<string>();

        [Tooltip("With Kind Sets set to Union Per Profile on the library, the entity also gets every kind of this profile.")]
        public KindProfile Profile;
    }
}
`````

---

## Appendix F. Research: StateSmith and QuanTAlib (read-only notes by two agents; check them)

### notes-statesmith.md

Path: `D:/GitHub/_work/research/notes-statesmith.md`  (36168 bytes)

`````
# StateSmith study notes (for the stats/intrinsics/events rules compiler)

Repo studied: D:\GitHub\_work\research\StateSmith (HEAD 0c32be37, 2026-05-11), plus D:\GitHub\_work\research\StateSmith-examples.
Method: read-only (Read/Grep/shell listing only). Nothing was built or run. All "what generated code looks like" quotes come from generated files
already checked into the repo (listed in section 2). Section 9 lists what I did NOT verify.

Abbreviations: SS = StateSmith, `src` = D:\GitHub\_work\research\StateSmith\src\StateSmith (the library), `ST` = ...\src\StateSmithTest.

---------------------------------------------------------------------------------------------------
## 0. One-paragraph model of SS

SS reads a diagram (draw.io / yEd graphml / PlantUML), turns it into a tree of `Vertex` objects (states, pseudo-states) that own ordered `Behavior`s
(trigger + guard + action + optional transition target), runs a fixed, named list of graph-to-graph "transformation steps" that lower exotic features into
plain behaviors, then an "algorithm" (Balanced1 or Balanced2) walks the lowered graph and prints straight-line code for each (state, event) pair into an
intermediate language called GIL (a restricted C# file). A per-language "transpiler" (Roslyn syntax walker) turns GIL text into C99 / C++ / C# / Java /
JS / TS / Python. Guards and actions are NOT parsed by SS; they are opaque target-language text (with identifier expansion), smuggled through GIL as string
literals. There is no runtime library; the output is self-contained.

---------------------------------------------------------------------------------------------------
## 1. Pipeline and where each stage lives

Top-level orchestration
- `src\Runner\SmRunner.cs` public entry; builds a DI container (`src\Runner\DiServiceProvider.cs`), reads settings, `Run()` -> `SmRunnerInternal.Run()`.
- `src\Runner\SmRunnerInternal.cs` `Run()` order: (a) `SetupAndFindStateMachine` (parse diagram), (b) `smDesignDescriber.DescribeBeforeTransformations`, JSON export
  "before", (c) `inputSmBuilder.FinishRunning()` = run transformer pipeline, (d) describe/JSON "after", (e) `codeGenRunner.Run()`, (f) optional simulator HTML.
- `src\Runner\InputSmBuilder.cs`: Step 1 diagram file -> vertices (picks parser by extension), Step 2 choose the StateMachine, Step 3 `FinishRunning()` ->
  `transformer.RunTransformationPipeline(sm)`.

Parse (stage 1)
- Diagram readers: `src\Input\DrawIo\*` (MxCellParser, MxCellsToSmDiagramConverter), `src\Input\Yed\*`, `src\Input\PlantUML\*` (ANTLR grammar
  `D:\GitHub\_work\research\StateSmith\antlr\PlantUML.g4`, generated parser in `src\Input\PlantUML\antlr4`). All produce neutral `DiagramNode`/`DiagramEdge`
  (`src\Input\DiagramNode.cs`, `DiagramEdge.cs`).
- Label grammar for node and edge text: `antlr\StateSmithLabelGrammar.g4` (generated: `src\Input\antlr4\StateSmithLabelGrammar*.cs`, ~5k-line parser, plus
  hand visitor `StateSmithLabelGrammarVisitor.cs`). A behavior line is `order? ( triggers guard action? vias? | ... )`, e.g. `1.1. EV1 [guard] / action`. `order` is a decimal
  number followed by `.`; `else` is a keyword; `(EV1, EV2)` is a trigger list; `enter`/`exit`/`do` are special triggers.
- `src\SmGraph\DiagramToSmConverter.cs`: builds `Vertex` tree from nodes (`ProcessNode`, `ParseNode`) and attaches `Behavior`s from edges (`ProcessEdgeInner`).
  Edge with empty label = blank transition (becomes an implicit `do` behavior later).

Graph model (in `src\SmGraph`)
- `Vertex.cs` (parent, depth, children, `_behaviors` ordered list, `_incomingTransitions`; `FindTransitionPathTo` computes LCA + toExit/toEnter lists via depth;
  `FirstAncestorThatHandlesEvent(eventName)`), `Behavior.cs` (`order` double, `guardCode`/`actionCode` strings, `_triggers`, `viaEntry`/`viaExit`),
  `State.cs`, `StateMachine.cs` (event set `_events`, `historyStates`, `variables`), pseudo-states: `InitialState`, `ChoicePoint`, `EntryPoint`, `ExitPoint`,
  `HistoryVertex`, `HistoryContinueVertex`, `NotesVertex`, `ConfigOptionVertex`, `OrthoState` (parsed but REJECTED later: `SpecificVertexValidator.Visit(OrthoState)`
  throws NotImplemented; no orthogonal regions).
- Visitors: `src\SmGraph\Visitors\*` (IVertexVisitor, RecursiveVertexVisitor, LambdaVertexWalker...). Compiler-checked exhaustiveness via the visitor pattern.

Transformer passes (stage 2): `src\Runner\StandardSmTransformer.cs`, runner class `SmTransformer.cs` (plain `List<TransformationStep>` with string/enum ids and
`InsertBeforeFirstMatch / InsertAfterFirstMatch / Swap / Remove`; users extend it from a .csx script). Fixed order (enum `TransformationId`):

| # | Id | File | What it does |
|---|----|------|--------------|
| 1 | Standard_RemoveNotesVertices | SmGraph\NotesProcessor.cs | delete `$NOTES` nodes |
| 2 | Standard_TomlConfig | SmGraph\TomlConfigVerticesProcessor.cs | settings embedded in diagram |
| 3 | Standard_SupportRenderConfigVerticesAndRemove | SmGraph\RenderConfigVerticesProcessor.cs | per-language render config nodes |
| 4 | Standard_SupportParentAlias | SmGraph\ParentAliasStateProcessor.cs | `$parent_alias` sugar |
| 5 | **Standard_SupportEntryExit** | SmGraph\EntryExitProcessor.cs | see below |
| 6 | Standard_SupportPrefixingModder | Runner\PrefixingModder.cs | `$prefix` name mangling |
| 7 | Standard_NameConflictResolution | Runner\StateNameConflictResolver.cs | unique state names |
| 8 | **Standard_SupportHistory** | SmGraph\HistoryProcessor.cs | see below |
| 9 | Standard_SupportElseGuard | SmGraph\ElseGuardProcessor.cs | `[else]` guard -> `order = ELSE_ORDER` |
| 10 | **Standard_SupportOrderAndElse** | SmGraph\OrderAndElseProcessor.cs | see below |
| 11 | Standard_SupportAlternateTriggers | SmGraph\SupportAlternateTriggersProcessor.cs | `entry` -> `enter` |
| 12 | Standard_Validation1 | StandardSmTransformer.Validate | SpecificVertexValidator + VertexValidator + StateNameValidator |
| 13 | Standard_DefaultUnspecifiedEventsAsDoEvent | SmGraph\DefaultToDoEventVisitor.cs | behaviors with no trigger get `do` |
| 14 | Standard_TriggerMapping | SmGraph\TriggerMap\* | user-defined trigger renaming/aliasing |
| 15 | Standard_AddUsedEventsToSm | SmGraph\AddUsedEventsToSmClass.cs | collect event set -> `StateMachine._events` |
| 16 | Standard_FinalValidation | same Validate() | validate again after lowering |

Entry/exit points (EntryExitProcessor): for an `EntryPoint` child, every transition into the parent that says `via entry X` is `RetargetTo(entryPoint)`; for an
`ExitPoint`, every parent behavior with `via exit X` is moved with `RetargetOwner(exitPoint)`. After this the points are ordinary pseudo-states (no special
codegen). Validators: `SmGraph\Validation\EntryPointValidator.cs`, `ExitPointValidator.cs`.

History (HistoryProcessor): fully lowered to ordinary behaviors, no special runtime. For each state tracked by `$H`: adds an `enter` behavior to that state
whose action (marked as GIL via `GilCreationHelper.MarkAsGilExpansionCode`) is `this.vars.<hist_var> = <HistEnum>.<STATE>;`, and adds a guarded transition on the
history vertex `[this.vars.<hist_var> == <HistEnum>.<STATE>]`; the default transition is forced to `order = Behavior.ELSE_ORDER`. `EnumBuilder.OutputHistoryIdCode`
emits the extra enum. Deep history = `$HC` vertices adding the tracking behaviors to ancestors' history vars. Lesson: "feature => generated variable + guarded
behaviors" instead of backend support.

Order and else (OrderAndElseProcessor + Behavior.cs):
```
public const double DEFAULT_ORDER = 1e6;
public const double ELSE_ORDER = DEFAULT_ORDER * 10;
...
v._behaviors = v.Behaviors.OrderBy((b) => b.order).ToList();     // stable LINQ OrderBy
```
Validation: at most one `else` per vertex, must be a transition, no guard, no explicit order. Ties keep diagram/edge order (stable sort), so the final order for equal
priority depends on input file order. Perf wart: `SortBehaviorsByOrder(v)` is called INSIDE the `foreach (var b in v.Behaviors)` loop, so it re-sorts once per behavior:
O(n^2 log n) per vertex. Irrelevant at SS scale (a handful of behaviors per state) but a 10,000-rule vertex would hit it. (Inferred from code, not measured.)

Validation details: `VertexValidator` (via-entry/exit only on transitions; a transition cannot have enter/exit trigger; detects yEd hidden self edges),
`PseudoLoopDetector.cs` (DFS through choice/initial chains, throws on a cycle of pseudo-states, i.e. the chain graph must be a DAG), `StateNameValidator`,
`SpecificVertexValidator` (exactly one initial state; no nested state machines; no OrthoState; HistoryContinue must be gone).

Codegen (stage 3)
- `src\Output\GilAlgoCodeGen.cs` (`ICodeGenRunner`): `expansionsPrep.PrepForCodeGen()` -> `gilAlgo.GenerateGil(sm)` -> `transpiler.TranspileAndOutputCode(gilCode)`.
- Algorithm selection: `src\Runner\AlgoTranspilerCustomizer.cs` (DI swaps `IGilAlgo`/`EventHandlerBuilder`; default is Balanced2) and `AlgorithmId.cs`.
- Balanced1: `src\Output\Algos\Balanced1\AlgoBalanced1.cs` + `EventHandlerBuilder.cs` (689 lines, the heart) + `EnumBuilder.cs` + `PseudoStateHandlerBuilder.cs` + `NameMangler.cs`
  (+ Camel/Pascal variants) + `SubtreeIdMapper.cs`.
- Balanced2: `src\Output\Algos\Balanced2\AlgoBalanced2.cs` (derives from AlgoBalanced1; overrides struct fields, dispatch, exit-up-to) + `EventHandlerBuilder2.cs`
  (derives from EventHandlerBuilder; overrides enter/exit/consume/ancestor behaviors).
- Text builder: `src\Output\OutputFile.cs` (indentation-aware StringBuilder wrapper; "RequestNewLineBeforeMoreCode").

---------------------------------------------------------------------------------------------------
## 2. What the generated C# looks like

Files used (all checked in, no need to run anything):
- Balanced1 C#: `ST\Output\Algos\out\Balanced1_CSharp\RocketSm.cs` (283 lines; design: `[*]->group{[*]->g1; g1->g2; g2->g1 : [x > 50]}`, one `do` event).
- Balanced2 C# (single event): `ST\Output\Algos\out\Balanced2_CSharp\RocketSm.cs`.
- Balanced2 C# (multi event, ancestors, guard): `ST\Output\Algos\out2\Balanced2_CSharp\RocketSm.cs` (design in `ST\Output\Algos\AlgoTests.cs`: `g1 --> g2: EV1 [a > 20]`, `g2 --> g1: EV2`, `group --> s1: EV1`).
- Big realistic Balanced2 C#: `src\test-misc\csharp-spec2\Spec2Sm.cs` (13,126 lines, 678 KB, 174 states, 15 events, 505 handler functions; test design with trace guards) and its GIL `Spec2Sm.gil.cs.txt`.
- Balanced2 C99 and C++: `ST\Output\Algos\out2\Balanced2_C99\RocketSm.c/.h`, `StateSmith-examples\cpp-inheritance-1\LightSm.h/.cpp`. Old Balanced1 C: `StateSmith-examples\multi-lang-1\LightSm.h`.
Note: StateSmith-examples\csharp-space-menu-1\Ui\UiSm.cs is an OLD (0.9.2, Balanced1) output; prefer the repo outputs above.

### 2.1 State id enum, event id enum (both algorithms)
```
public enum EventId { EV1 = 0, EV2 = 1 }        // 'do' is forced to 0 when present, others sorted alphabetically
public const int EventIdCount = 2;
public enum StateId { ROOT = 0, GROUP = 1, G1 = 2, G2 = 3, S1 = 4 }
public const int ROOT_SubtreeEndId = 4;  public const int GROUP_SubtreeEndId = 3; ...
```
Ids = DFS pre-order, children visited sorted by NAME (`SubtreeIdMapper.cs`, "to minimize git diffs on user diagram refactoring"). Because ids are
pre-order, a state's subtree is the contiguous id range `[id, SubtreeEndId]` (so `isDescendant` is a range compare; emitted since 0.22.1, issue #538). Ids are a function of the
design (rename or add a state and other ids shift), they are not a stable external contract.

### 2.2 Current state variable
Balanced2: exactly one field, `public StateId stateId;` ("Feel free to inspect, but don't modify"). Balanced1 adds three pointer fields (section 3).

### 2.3 Dispatch (Balanced2: nested switch, no tables)
From `out2\Balanced2_CSharp\RocketSm.cs`:
```
public void DispatchEvent(EventId eventId)
{
    switch (this.stateId)
    {
        // STATE: g2
        case StateId.G2:
            switch (eventId)
            {
                case EventId.EV2: G2_ev2(); break;
                case EventId.EV1: GROUP_ev1(); break; // First ancestor handler for this event
            }
            break;
        ...
```
Codegen: `AlgoBalanced2.OutputFuncDispatchEvent/OutputStateEventHandlerCode`. For EVERY state it emits a case for events the state handles itself and, for events it does not
handle, a case that calls the FIRST ANCESTOR that does (`NamedVertex.FirstAncestorThatHandlesEvent`, computed at compile time). So inheritance is FLATTENED in the dispatch table and
resolved at gen time. A state with no handler anywhere gets `// No events handled by this state (or its ancestors).`. Single-event designs drop the inner switch
(issue #388): `OutputStateSingleEventHandlerCode`.

### 2.4 How an event reaches the parent when the child doesn't consume it
Two different mechanisms:
(a) Child doesn't handle the event at all: direct call to the ancestor handler from dispatch (above).
(b) Child handles it but may decline: handler falls through to the ancestor at the bottom (Balanced2, `EventHandlerBuilder2.OutputStateEventHandlerFunctionBottom`):
```
private void G1_ev1()
{
    bool consume_event = false;
    // g1 behavior
    // uml: EV1 [a > 20] TransitionTo(g2)
    if (a > 20)
    {
        G1_exit(); G2_enter();
        // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
        return;
    }
    // Check if event has been consumed before calling ancestor handler.
    if (!consume_event)
    {
        GROUP_ev1();
    }
}
```
Semantics (verified in `EventHandlerBuilder.OutputNonTransitionCode` and `EventHandlerBuilder2.PreActionCode`): a transition whose guard passes ends dispatch (`return`). A NON-transition
behavior (guard passes) executes its action and sets `consume_event = true` (except for the special `do` event, "not normally consumed"; user action code may assign
`consume_event` itself and is detected by `ActionCodeUsesConsumeEventVar`). At the end, if not consumed and an ancestor handles the event, the ancestor handler is CALLED (nested call;
stack depth = nesting depth, bounded at compile time).
Balanced1 does it with a trampoline instead (no recursion):
```
public void DispatchEvent(EventId eventId)
{
    Func? behaviorFunc = this.currentEventHandlers[(int)eventId];
    while (behaviorFunc != null)
    {
        this.ancestorEventHandler = null;
        behaviorFunc(this);
        behaviorFunc = this.ancestorEventHandler;
    }
}
```
each handler first does `this.ancestorEventHandler = ptr_GROUP_ev1;` ("Setup handler for next ancestor"), and consuming sets it to null.

### 2.5 Guards and the ORDER number (priority)
Within one (state, trigger) handler, `TriggerHelper.GetBehaviorsWithTrigger` returns that state's behaviors already sorted by `order` ascending (lowest number evaluated FIRST).
Each becomes sequential straight-line code:
- guard -> `if (guard) { ... }`; no guard -> bare `{ ... }` block (python needs `if (true)`).
- If a behavior is a transition with no guard, every later behavior is unreachable and is emitted only as a comment (`OutputReachableStateBehaviors`, issue #394):
  `// unreachable behavior: ... due to unconditional transition above` (visible in `Spec2Sm.cs` line ~7031).
- `else` = `order 1e7`, always last, so it is just the final unguarded block.
- Non-transition behaviors all run in order (several may fire); the first passing TRANSITION stops everything.
From `Spec2Sm.cs` (`Test3_BehaviorOrdering`): behaviors `1. EV1`, `1.1. EV1`, `2. EV1 ... TransitionTo(S3)` become three consecutive `if (trace_guard(...))` blocks in
that order (decimal order lets you insert between), the third doing `TEST3_S2_exit(); ... TEST3_S3_enter(); return;`.
Priority conflicts between a CHILD and its PARENT are not numeric: child state always before ancestor (child decides, then optionally falls through if not consumed). A numeric "wins
by higher number" scheme would have to be added by us (SS has lowest-number-first and no duplicate detection).

### 2.6 Exit/enter sequences
`EventHandlerBuilder.OutputTransitionCode` emits fixed 4 steps: (1) exit up to LCA, (2) transition action, (3) enter towards target, (4) complete (set state, return).
- Exit: if the active leaf is statically known and exactly one state must be exited -> direct call `G2_exit();`. Otherwise a loop call `ExitUpToStateHandler(StateId.ROOT);`
  (`CanUseSingleDirectExit`).
- Balanced2 `ExitUpToStateHandler(desired)`: `while (stateId != desired) switch (stateId) { case StateId.G1: G1_exit(); break; ... default: return; }` (the default is a
  corruption guard: prevents infinite loop, issue #391). Each `X_exit()` ends with `this.stateId = <parent>` so the loop walks up. Each `X_enter()` starts with `this.stateId = X`
  (Balanced2 tracks state on enter so pass-through states can be exited again).
- Enter: toEnter list computed top-down by `Vertex.FindTransitionPathTo` (LCA algorithm using depth); each is `X_enter();`; enter behaviors run inside `X_enter`. Initial pseudo-states
  inline their transition, so entering `group` immediately emits `G1_enter()`.
- Balanced1 stores exit handlers as function pointers: `currentStateExitHandler = ptr_G1_exit;` and `ExitUpToStateHandler(Func desired)` loops `currentStateExitHandler!(this)`.

### 2.7 "Tail-chain optimization": not found by that name
I grepped the repo (cs, md, changelog) for tail/tail-chain/tail-call: no such feature. The nearest real optimizations (all in the changelog and code):
1. Pseudo-state de-duplication (`PseudoStateHandlerBuilder.ShouldCreateFunctionHandler`): a choice/initial/history point with >1 incoming transitions (initial counts parent's incoming too) is emitted ONCE as a
   `X_transition()` function and called; with exactly one incoming it is INLINED as nested ifs (see the `TEST10_G_ChoicePoint__transition` nesting in `Spec2Sm.cs`, lines 4457-4540).
   This is how "chains of chains" are lowered: DAG of choice points -> nested if/else, shared sub-DAGs -> shared functions.
2. Transition completes with `return` after the last `enter` (the ancestor call in Balanced2 is the last statement of the function, so it is a call in tail position that a JIT may or may not turn
   into a jump; nothing in SS forces that).
3. Direct-exit vs exit-up-to, single-event dispatch collapse, unreachable-behavior commenting, no root exit handler (0.13.2), "No ancestor handles event. Can skip nulling ancestorEventHandler" (Balanced1).

---------------------------------------------------------------------------------------------------
## 3. Instance storage and runtime allocation

Balanced2 (default): one field per instance.
- C99: `struct RocketSm { RocketSm_StateId state_id; };` (`ST\Output\Algos\out2\Balanced2_C99\RocketSm.h`, enum is `int`-sized unless the user packs it).
- C++ (`cpp-inheritance-1\LightSm.h`): `enum class StateId: uint8_t` -> 1 byte of SS state per instance.
- C#: class with `public StateId stateId;` (4 bytes + object header; `new RocketSm()` is the only allocation; the ctor is empty).
- User variables (`vars` struct/class) are extra; guards/actions reference them (`this.vars.count`, expansion `{VarsPath}`).
- No lookup table, no delegates, no heap use at runtime. The only "table" is the compiler's own jump table for `switch(stateId)` / `switch(eventId)`. Optional `StateIdToString`, `EventIdToString`,
  `GetParentId` are `static` switch functions and can be switched off (`outputStateIdToStringFunction` etc., `AlgoBalanced1Settings.cs`).
Balanced1: per instance (`ST\Output\Algos\out\Balanced1_CSharp\RocketSm.cs`, `StateSmith-examples\multi-lang-1\LightSm.h`): `stateId`, `ancestorEventHandler` (pointer), `currentEventHandlers[EventIdCount]`
(pointer array, one pointer per event; in C# a per-instance heap array `new Func[EventIdCount]`), `currentStateExitHandler` (pointer). So 4 + 8 + 8*E + 8 bytes (+array header in C#), and enter/exit
rewrites the pointer array every transition. Static delegates (`ptr_G1_exit`) avoid per-call allocation. This runtime dispatch table is exactly what the owner wants to avoid.
Handlers are instance methods / take `RocketSm* sm`: state lives inside the instance object, not in an external SoA array, so million-entity use needs a different calling convention (see section 6).

---------------------------------------------------------------------------------------------------
## 4. Templates / expansion system, GIL, adding languages

Expansions (Input\Expansions, Output\UserConfig)
- Identifiers in user guard/action text are expanded textually: `Expander.cs` holds `variableExpansions` (name -> code string) and `functionExpansions` (name -> reflected C# method that
  returns a string). `ExpandingVisitor.ParseAndExpandCode(expander, code)` tokenizes the user's code with a small ANTLR grammar and swaps identifiers. Users define them in a render-config class
  or `UserExpansionScriptBase` (`Output\UserConfig\*`: `IRenderConfig`, `RenderConfig*Vars`, `AutoVars\AutoExpandedVarsProcessor.cs` which turns `count` into `sm->vars.count`).
  `DefaultExpander.cs` is the catch-all template (`{AutoVarName()}`, `{VarsPath}`).
- Expansion functions can use script context (`CurrentNamedVertex`, `CurrentTrigger`, `CurrentBehavior`: see `trace_meta()` in `ST\spec\2\Spec2Fixture.cs`).
- `WrappingExpander.cs`: wraps expanded text so GIL stays valid C#: guard -> `____GilData_VarArgsToBool(____GilData_EchoStringBool("<raw text>"))`, action ->
  `____GilData_EchoStringVoid("<raw line>");`. `$gil(...)` lets the user write real GIL that the transpiler will translate (e.g. a C# enum member that becomes `Enum_Member` in C).
So SS has no code templates in the T4 sense: the algorithm builds text with `OutputFile.AppendIndentedLine` calls; "template-like" parts are expansion strings only for user-visible identifiers.

GIL (Generic Intermediate Language) = a restricted subset of C# source text (see `Spec2Sm.gil.cs.txt`):
```
public class ____GilData_FileTop { }
public class Spec2Sm { public enum EventId {...}  private void TEST3_S2_ev1() {... if (____GilData_VarArgsToBool(____GilData_EchoStringBool("trace_guard(...)"))) {...} } }
public static bool ____GilData_EchoStringBool(string toEcho) { return true; }
```
Helpers in `Output\Gil\GilCreationHelper.cs`, `GilTranspilerHelper.cs`; compiled with Roslyn (`RoslynCompiler.cs`: `CSharpSyntaxTree.ParseText` + `CSharpCompilation` semantic model with system references from
`InMemoryMetaDataProvider`/`FileMetadataProvider`) so that transpilers can ask the semantic model "is this member an enum", "is this a method group" (`MethodPtrFinder.cs` finds `this.X` method pointers to
turn them into function pointers). Constraint on algorithms: they must only emit constructs every transpiler handles (enums, fields, methods, if/while/switch, a method call, a delegate type for function pointers).

Adding a target language (pattern from `Output\Gil\<Lang>\`):
1. `GilToX : IGilTranspiler` (writes files; e.g. `CSharp\GilToCSharp.cs` is 30 lines of glue; `C99\GilToC99.cs` writes .h and .c).
2. `XGilVisitor : CSharpSyntaxWalker` that re-prints GIL syntax in X (C99: 852 lines, C++: 572, Python: 629, JS: 450, TS: 342, Java: 345, C#: 333).
3. `RenderConfigXVars` / `IRenderConfigX` (user config knobs) and `IExpansionVarsPathProvider` (how `vars` is spelled: `sm->vars.` vs `this.vars.`), `IAutoVarsParser` if variable declaration syntax differs.
4. Register in `TranspilerId.cs`, `AlgoTranspilerCustomizer.cs` (sets flags such as `skipClassIndentation`, `useIfTrueIfNoGuard`, `allowSingleLineSwitchCase`, `varsStructAsClass`), `DiServiceProvider`.
5. Add the language to the spec test matrix (section 7).
Cost evidence: Python/Java/TS/C++ currently only support Balanced2 (AlgoTests.cs lines ~60-75). The README says adding languages is "tricky".

---------------------------------------------------------------------------------------------------
## 5. Would this approach scale to ~10,000 rules? What breaks

Data points from the repo (not benchmarks): Spec2Sm design = 174 states x 15 events -> 13,126 lines / 678 KB C# / 505 functions; Roslyn + formatter handled it. Output is ~75 lines per
state, mostly comments (each behavior prints 4 "Step" comment blocks plus `// uml:` text) and the dispatch table (S x events-in-scope entries).

Structural fit
- SS = "which discrete state am I in" -> a rule graph over continuous stats is different: there the guard is data (stat/intrinsic predicate), not current state. SS can be bent: treat
  each event as a "rule chain" and ancestors as rule layers, but state machine concepts (LCA, enter/exit, history, one active leaf) would be dead weight.
- Good fit: ordered guarded behaviors per (scope, trigger), consume/else semantics, DAG of shared sub-chains lowered to nested ifs or shared functions, flattening of inheritance in dispatch.

What breaks at 10k rules (inference from code, nothing measured)
1. Single huge method: if thousands of rules attach to one event handler you get thousands of sequential `if` blocks in one function. .NET JIT/Burst have size limits/heuristics (I recall the CLR JIT switches
   to minimal optimization for methods above roughly 60 KB IL / 2000 basic blocks; NOT verified here) -> must partition handlers (by event x layer x intrinsic-bucket), emit decision structure
   (switch on intrinsic bits first), cap function size, chain partitions by explicit calls/gotos.
2. Source size: with SS's comment-heavy printer 10k transitions ~ hundreds of thousands of lines; C# compiler/Unity domain reload time grows. Emit terse code + a side-car map (rule id -> source location) instead of comments.
3. Generator complexity hot spots: `OrderAndElseProcessor.SortBehaviorsByOrder` called inside the foreach (O(n^2 log n) per vertex); `FirstAncestorThatHandlesEvent` re-walks ancestors and re-scans each ancestor's
   behaviors (`TriggerHelper.HasBehaviorsWithTrigger`) for every (state,event) pair in dispatch gen (O(S*E*depth*B)); `List.Remove`/`IndexOf` based graph edits; string-keyed `HashSet<string>` events. Needs indexes.
4. The GIL/Roslyn round trip: whole output re-parsed with semantic model, then `Formatter.Format` over the full text (C# transpiler). Memory/time super-linear for ~100 MB source. A direct emitter (strings or
   `SyntaxFactory`) for the single target avoids this. SS needs GIL only because it targets 7 languages.
5. Stack depth: Balanced2 recursion depth = hierarchy depth (compile-time known). "Chains with chains" nest as nested ifs (inline) so depth is code nesting, not runtime stack; unbounded chain depth
   would make deeply nested C# (compiler limits on nesting). Use flat early-exit style (`if (blocked) goto end;` / `return`) instead of nesting; share sub-chains as functions when in-degree > 1 (as SS does).
6. Duplicate code through inlining: SS inlines single-incoming pseudo-states but duplicates when the same chain is reachable from several handlers inside different states (it only dedups pseudo-states, not whole
   behavior bodies). With 10k rules and card-added checks you need common-subexpression and shared-chain extraction by hash of the lowered rule body.
7. Stable ids: SS ids depend on the design (sorted by name). Offsets-as-contract require explicit, stored ids.
8. Semantics gap: SS stops at the first passing transition; our rules need "all matching modifiers apply unless blocked". SS's non-transition behaviors (all run in order, `consume_event` gates ancestors) is the right primitive;
   a "blocked" rule = early `return`.

---------------------------------------------------------------------------------------------------
## 6. Ideas worth copying for a rules compiler over stats / intrinsics / events

Architecture
1. Named, orderable lowering pipeline: `SmTransformer`-style list of `(id, Action<Graph>)` with InsertBefore/After, so designers' mods (cards) are passes. Run validation twice: before lowering (user-facing errors) and after (compiler self-check). Copy `StandardSmTransformer.cs`'s structure.
2. Lower exotic features to the plain core before emission (history -> var + guarded behaviors; entry/exit points -> retargeting; `else` -> sentinel order; implicit trigger -> `do`). Keep emission dumb. Our analogs: "blocked while cursed" = guard on a `Blocks` rule class lowered to an `if` at the top of the chain; "heals twice while jumping, higher priority wins" = sorted rule list per event.
3. Keep the IR typed: SS passes guards as opaque strings (cannot analyze, reorder, or dedup them; only regex/ANTLR identifier expansion). For 10k rules store conditions as expression trees over `StatId/IntrinsicId/EventId` so we can constant-fold, merge into bitmask tests, hash for dedup, and prove shadowing.
4. Algorithm class + builder split: `AlgoBalanced1/2` (what to emit) vs `EventHandlerBuilder(2)` (per-handler printing, with virtual hooks `PreActionCode`, `OutputCompleteTransition`, `OutputNextAncestorHandler`). Two algorithm generations sit side by side via DI (`AlgoTranspilerCustomizer`), each with spec tests. We can keep an "interpretable reference backend" and a "fast backend" the same way.
5. GIL is only worth it with multiple targets. We target one (C# / Burst). Skip it; emit C# text directly through an indentation-aware `OutputFile` clone.
6. Generated code carries the source rule text in comments (`// uml: ...`) for traceability. Replace with rule ids + a map.

Priority handling
7. Order as `double` with big default (1e6) and else = 10x: lets designers insert between 1 and 2 with `1.5` without renumbering; stable sort; `else` sentinel. For "higher number wins" invert at compile (`sortKey = -priority`). Add what SS lacks: detect equal priorities among rules that can both fire and fail (or make tie-break explicit, e.g. by rule id), because SS silently uses file order.
8. Shadow detection: SS comments out behaviors after an unconditional transition (`// unreachable behavior`). Do the same as a build warning/error for rules dominated by a higher-priority unconditional blocker.
9. Consume semantic: a per-handler local `consume_event` flag + "call parent if not consumed" gives layered rule sets (base game rules, card rules). Flatten the inheritance at generation time like Balanced2's dispatch ("First ancestor handler for this event") and keep the fall-through only where a rule may decline.

Flattening / codegen shape
10. Nested switch (state, then event) with direct calls and NO runtime table is Balanced2; the exact analog is `switch(eventId)` -> `Event_Damage(ref entity)` where the body is a straight priority-sorted chain. Entities need no per-instance dispatch state at all (only the data).
11. Pre-order ids with contiguous subtree ranges give O(1) `isDescendant`; useful for layers/tags/status hierarchies (`StateIdCount`, `X_SubtreeEndId`), emitted as `const int`.
12. Shared pseudo-state functions when in-degree > 1, inline otherwise: use for shared sub-chains; compute in-degree after all lowering passes (SS fixed a bug where initial-state parent incoming counts were ignored).
13. Optional runtime reflection kept to static switch functions (`StateIdToString`, `GetParentId`) behind settings flags; safe pattern for debug names without strings on the hot path.
14. Safety net `default: return;` in loops that walk by id (prevents infinite loop on corrupted memory); and a build-time check that every visited state had its exit adjustment (`FinalCheck`, issue #391).
15. Use `do` = tick: SS's special event that every ancestor receives and that is not consumed unless a transition happens maps to the timeline tick event.

Testing by simulating
16. SS's `ST\spec` suite: one diagram (`Spec2Sm.graphml`), generated for every language/algorithm, built and RUN as a separate process with a script of events; every behavior/guard/action calls a `trace` hook; the test compares the text trace with expected (`SpecTester.cs`, `Spec2Tests.cs`: e.g. `Test3_BehaviorOrdering` asserts "1 woot!, 2 woot!, 3 woot!" order). Copy it as differential testing: slow reference interpreter over the rule graph vs compiled code on random entity states, comparing final stat/intrinsic arrays + a rule-firing trace.
17. Cheap golden/text tests (`ST\Output\Algos\AlgoTests.cs`: assert generated C contains/excludes `switch (sm->state_id)`), JSON export of the graph before and after transformations (`Output\SmGraphJsonExporter.cs`) for snapshot tests of passes in isolation.
18. In-browser simulator (`Output\Sim\SimWebGenerator.cs`) is the same pipeline with the JS transpiler plus a `TrackingExpander` that records which behaviors/edges fired: a model for a designer-facing "why did this rule not fire" viewer.

Dead ends / things NOT to copy
- Balanced1 runtime function-pointer tables (per-instance pointer array rewritten on each transition) conflict with the "no runtime lookup" goal; only Balanced2 fits.
- Instance-bound handler methods / `sm->` access pattern: for a million entities use static methods taking `ref EntityData` or SoA lanes (offset contract).
- Opaque string guards, name-sorted ids, tie-breaking by file order, comment-heavy output, the sort-in-loop.

---------------------------------------------------------------------------------------------------
## 7. Test suite pointers (for reuse ideas)
- `ST\spec\SpecTester.cs`, `SpecFixture.cs`, `spec\2\Spec2Tests.cs` (abstract; per-language subclasses in `spec\2\CSharp|Cpp|Java|JavaScript|Python|TypeScript|c`), process runner `ST\Processes\SimpleProcess` (e.g. `dotnet bin/Debug/net9.0/Spec2Test.dll EV1 EV2`).
- README says 730+ tests; every language runs the same spec through one trace format. Generated reference outputs are committed (`spec\2\c\Spec2Sm.c`, `src\test-misc\csharp-spec2`).
- `ST\Output\Algos\AlgoTests.cs` generates the full algorithm x transpiler matrix into `out\` and `out2\` (these committed outputs are the best quick-look files).

---------------------------------------------------------------------------------------------------
## 8. Quick file index
- Pipeline list: `src\Runner\StandardSmTransformer.cs`, `SmTransformer.cs`, `InputSmBuilder.cs`, `SmRunnerInternal.cs`
- Priority: `src\SmGraph\Behavior.cs`, `OrderAndElseProcessor.cs`, `ElseGuardProcessor.cs`; docs: `docs\diagram-features.md` ("behavior ordering")
- History: `src\SmGraph\HistoryProcessor.cs`; docs `docs\history-vertex.md`
- Entry/exit: `src\SmGraph\EntryExitProcessor.cs`
- Emission core: `src\Output\Algos\Balanced1\EventHandlerBuilder.cs`, `Balanced2\EventHandlerBuilder2.cs`, `AlgoBalanced1.cs`, `AlgoBalanced2.cs`
- Ids: `src\Output\Algos\Balanced1\EnumBuilder.cs`, `SubtreeIdMapper.cs`; pseudo-state funcs `PseudoStateHandlerBuilder.cs`
- GIL: `src\Output\Gil\*`; transpilers `src\Output\Gil\<Lang>\*`
- Expansions: `src\Input\Expansions\*`, `src\Output\UserConfig\*`
- Validation: `src\SmGraph\Validation\*`, `PseudoLoopDetector.cs`

---------------------------------------------------------------------------------------------------
## 9. What I did NOT verify (be skeptical of these)
- Did not build or run SS or any generated code; every scaling statement in section 5 is inference from reading code and the size of committed outputs, no timing or memory measurements.
- Did not read: the ANTLR-generated parsers, DrawIo/Yed/PlantUML parsing internals, `ExpandingVisitor`, TriggerMap internals, the non-C# visitors (C99/C++/Python/Java/JS/TS bodies), the CLI (`src\StateSmith.Cli`), `SmDesignDescriber`, or most of `HtmlRenderer`.
- The ".NET JIT ~60 KB IL / 2000 basic blocks MinOpts" claim is from my memory of the CLR, not from this repo; test on the actual Unity/Burst version.
- "Tail-chain optimization" is not a name used in this repo; I mapped it to the nearest real optimizations (section 2.7). If you meant something from a different tool or wiki page I did not see it.
- I did not check whether C# method calls in `Balanced2` ancestor fall-through are actually tail-called by the JIT.
- The examples repo outputs are mostly from older SS versions (0.9.x to 0.17); I used the library repo's committed outputs (current HEAD behavior) for all quoted code except struct layouts of Balanced1 in C.
- Doc claims about "wiki" pages (Algorithms, Multiple-Language-Support) were not read (not in the repo).
`````

### notes-quantalib.md

Path: `D:/GitHub/_work/research/notes-quantalib.md`  (22193 bytes)

`````
# QuanTAlib study notes (data and speed side)

Repo: D:\GitHub\_work\research\QuanTAlib (commit c6ffb9f4, 2026-09-27). Read-only study; nothing was built, run, or benchmarked.
Root path used below: `L = D:\GitHub\_work\research\QuanTAlib\lib`.

Scope honesty: I read in full: `core/ringbuffer/RingBuffer.cs`, `core/simd/SimdExtensions.cs`, `core/AbstractBase.cs`, `core/ConfirmOnClose.cs`, `core/CompareOp.cs`, `trends_FIR/sma/Sma.cs`, `trends_IIR/ema/Ema.cs`, `docs/architecture.md`, `docs/benchmarks.md`, `perf/Benchmark.cs` (head and EMA section). Skimmed: `Wma.cs`, `Rsi.cs`, `Bop.cs`, `Relu.cs`, `Sigmoid.cs`, `Jma.cs`, `Cg.cs`, `Alma.cs`, `ErrorHelpers.cs` (parts), `BiInputIndicatorBase.cs`, `MultiInputBase.cs`, `TValue/TBar/TSeries/TBarSeries`, python `Exports.Generated.cs`. About 450 indicator files exist; I did not read most. Numbers about the repo are from grep counts (exact); numbers about memory per instance are my estimates.

## 0. What QuanTAlib is (and is not) relative to our goal

It is a technical-analysis indicator library: ~447 indicators, each a class (`AbstractBase`) with `Update(TValue, bool isNew)` streaming, `static Batch(ReadOnlySpan<double>, Span<double>, ...)` span mode, `Update(TSeries)` series mode, and event chaining (`Pub +=`). It is NOT an entity state store. One instance = one indicator on one series. There is no "many entities in one contiguous block" anywhere. The reusable parts are a few micro-patterns, not the architecture. Double only (zero uses of `float`), managed collections and events everywhere, per-instance heap objects.

## 1. Memory layout

- Time series: Structure of Arrays at the batch layer. `lib/core/types/tseries/tseries.cs`: `protected readonly List<long> _t; protected readonly List<double> _v;`. `TBarSeries` = six parallel `List<>` (t,o,h,l,c,v). Exposed to SIMD via `CollectionsMarshal.AsSpan(_v)` (`Values` property). `docs/architecture.md` states the SoA rationale (cache lines full of one field).
- Single values are AoS structs: `TValue(long Time, double Value)` 16 bytes, `TBar` 48 bytes, both `readonly record struct` with `[StructLayout(LayoutKind.Auto)]` (CLR may reorder fields; NOT a stable binary layout, bad for blit save/load; we need `Sequential`/`Explicit`).
- Batch kernels take separate spans per field: `Bop.Batch(open, high, low, close, destination)` (SoA, one `Vector` load per field per iteration). This is the right shape for "SIMD across entities".
- Per-instance streaming state: small `record struct State(...)` held twice, `_state` and `_p_state` (current and previous). Sma: `State(double Sum, double Compensation, double LastValidValue)` = 24 B x 2. Ema: `State(double Ema, double E, bool IsHot, bool IsCompensated)` x 2 plus `_lastValidValue` x 2. Wma: `State(7 fields)` x 2 (about 56 B each). So scalar state is a few doubles, copied by plain struct assignment.
- Ring buffer: `lib/core/ringbuffer/RingBuffer.cs`, a sealed CLASS over `double[] _buffer = GC.AllocateArray<double>(capacity, pinned: true)`, fields `_head,_count,_sum` + single-slot `_savedHead,_savedCount,_savedSum,_savedValue`. Wrap uses `%` (modulo, not mask). So each Sma instance is at least 4 heap objects: indicator, RingBuffer, pinned array, handler delegate (+ Name string). My estimate: ~300+ bytes per Sma(14), not the "40-60 bytes per indicator instance" claimed in `docs/architecture.md`. At 1M entities this layout (object per thing, pointer chasing) is exactly what we must avoid.
- Alignment: no explicit alignment anywhere (`grep NativeMemory|AlignedAlloc` = 0 hits). `pinned: true` on the POH is only about not moving; it gives no 32/64-byte alignment (my knowledge of .NET, not verified here). The doc comment "Pinned memory for SIMD compatibility" is misleading. All loads are unaligned (`Vector256.LoadUnsafe`, `new Vector<T>(span)`), which is fine on modern CPUs but cache-line splits are possible.
- Only large-state indicator I found: `Jma.cs` has three pinned 128-element arrays (1,536 B) and instead of copying them for rollback it keeps an undo journal (see 3).
- Python/FFI layer (`python/src/Exports.cs`): `private static ReadOnlySpan<double> Src(double* p, int n) => new(p, n);` zero-copy views over native buffers. Good model for exposing our contiguous chunk to other languages.

## 2. SIMD approach

Counts (non-test .cs under lib): ~476 files; only 26 reference Vector/Avx/NEON (RingBuffer, SimdExtensions, ErrorHelpers, Sma, Wma, Alma, Hma, Rsi, Cmo, Bop, Ad, Cmf, Variance, StdDev, Covariance, Correl, Skew, Kurtosis, Jb, Slope, Accel, Jerk, Lineartrans, Relu, Sigmoid, Aberr). Roughly 5%. The "447 vectorized indicators" impression is wrong: most are scalar loops. `Math.FusedMultiplyAdd` appears 1,536 times though, mostly scalar.

Three styles coexist:
1. Portable `Vector<double>` (the majority): guard `if (Vector.IsHardwareAccelerated && len >= Vector<double>.Count)`, loop `for (; i <= len - vectorSize; i += vectorSize)`, then a scalar `for (; i < len; i++)` tail. Width is whatever the runtime picks (2/4/8 lanes), which matters for determinism (section 5).
2. Hand-dispatched intrinsics for a few hot kernels. Dispatch order (Sma.Batch, Wma.Batch, DotProduct): `Avx512F.IsSupported` -> `Avx2.IsSupported` -> `AdvSimd.Arm64.IsSupported` -> scalar/`Vector<T>`. Preconditions in Sma/Wma: `len >= 256 && !source.ContainsNonFinite()` (a full extra SIMD pass to check NaN/Inf), else scalar path with NaN substitution.
3. Fake vectorization: `Sigmoid.CalculateVectorCore` loads a vector, then loops lanes calling `Math.Exp` scalar into a `stackalloc` buffer and reloads it. No transcendental SIMD at all. `Divide` in SimdExtensions is explicitly scalar ("no branch-free SIMD zero-guard worth the complexity").

Tails/remainders:
- Plain scalar tail loop after the vector loop (SimdExtensions all methods, RingBuffer.SumSpanSimd).
- Sma/Wma: `simdEnd = period + ((len - period) / VectorWidth * VectorWidth)` then scalar tail with plain `sum += newVal - oldVal` (non-Kahan, so the tail rounds differently from the vector body and from the scalar-path Kahan version).
- No masked loads/stores, no padding of arrays to a multiple of the vector width. Padding the chunk to a multiple of 8 and skipping tails would be strictly better for us.

Branch-free tricks found (all small):
- `Vector.ConditionalSelect(mask, a, b)` after `Vector.GreaterThan/LessThan`: `Bop.Batch` (`mask = range > epsilon`; `div = body/range` computed for all lanes then masked to 0, relying on no FP traps), `Rsi.Batch` (two nested ConditionalSelect for loss==0 and flat -> 50).
- `Avx.BlendVariable(lastValidVec, relu, isFiniteMask)` in `Relu.cs`, `Avx.Compare(x, x, OrderedEqualNonSignaling)` / `MoveMask` for NaN detection in `ErrorHelpers` (but NaN handling there is "bail out to scalar at first NaN chunk", not blend).
- `Vector.Max(change, 0)` and `Vector.Max(-change, 0)` for gain/loss split (Rsi) instead of branches.
- NaN check `Vector.Equals(v, v)` plus `Abs(v) > double.MaxValue` for infinity (SimdExtensions.ContainsNonFinite).
- Vectorizing a serial recurrence: SMA/WMA batch use a within-vector prefix scan of deltas (Hillis-Steele log-step: AVX-512 uses `PermuteVar8x64` + multiply by 0/1 mask constants; AVX2 uses `Permute4x64` + `Blend`; NEON does it scalar) with the running sum carried between vectors by broadcasting the last lane (`vSumState = Vector512.Create(vSums.GetElement(7))`). Clever but only needed because one series is the unit of work. In an SoA entity store the parallelism is across entities and none of this is needed. Also the mask-multiply trick is NaN-unsafe (NaN*0 = NaN), which is why they pre-scan for non-finite.
- Dot product (`SimdExtensions.DotProduct`): 4 independent accumulators, 4x unrolled (32 doubles per iteration for AVX-512, 16 for AVX2, 8 for NEON), `Avx512F.FusedMultiplyAdd` / `Fma.MultiplyAdd`, `bool useFma = Fma.IsSupported` hoisted out of the loop with duplicated loop bodies, accumulators merged pairwise, horizontal sum via `GetLower/GetUpper` + `Sse2.Add`, scalar tail with `sum += a*b` (non-FMA). Small-n fast path `len <= 3`.
- Lazy non-finite check in `SumSIMD`: compute the sum, only scan for NaN if the result is non-finite.

FMA use: ~1.5k `Math.FusedMultiplyAdd` calls, e.g. EMA `state.Ema = Math.FusedMultiplyAdd(state.Ema, decay, alpha * input)`, RingBuffer `_sum = Math.FusedMultiplyAdd(-1.0, removed, _sum + value)` (an FMA used as a subtraction, no benefit, odd). `Math.FusedMultiplyAdd` is correctly rounded everywhere (hardware FMA or exact software fallback), so the scalar usage is deterministic in value but has a perf cliff without FMA hardware (my knowledge). Explicit `Fma.MultiplyAdd` in AVX2 paths is the same single-rounding result per lane. The deterministic problem is which algorithm/width runs, see 5.

## 3. Streaming vs batch and isNew (pending/confirmed)

Modes (docs/architecture.md): Span (stateless static `Batch`), Batch (TSeries), Streaming (`Update(TValue, isNew)`), Eventing (`Pub`). Priming: `Prime(span)` rebuilds streaming state from history (Sma only replays the last `period` values).

isNew protocol, the canonical form (Ema.cs):
```csharp
if (isNew) { _p_state = _state; _p_lastValidValue = _lastValidValue; }
else       { _state = _p_state; _lastValidValue = _p_lastValidValue; }
double val = GetValidValue(input.Value);
val = Compute(val, _alpha, _decay, ref _state);
```
Meaning: on a new bar, save the current state as "previous"; on a correction (isNew=false) restore the saved previous state, then recompute from the new input. Any number of corrections of the same bar are fine because `_p_state` is only overwritten on the next `isNew=true`. `ConfirmOnClose.cs` is the same idea one level up: `State(TValue Pending, bool HasPending, bool HasConfirmed)`, publishes the pending value only when the next bar opens (always one bar behind).

How windowed state is rolled back:
- Sma: `UpdateNewest` in RingBuffer replaces the newest slot and patches `_sum` differentially (`FMA(-1, old, sum+new)`); then `_state = restoredState with { Sum = _buffer.Sum }`.
- RingBuffer has ONE saved slot (`Snapshot()` before Add saves head/count/sum and the single value about to be overwritten; `Restore()` puts them back). Used by Cg, Jbands, BiInputIndicatorBase etc. (85 call sites). Depth is exactly 1 tick.
- Jma (big state): an "undo journal" (`_undoOldC, _undoOldS, _undoNewS, _undoOldVal, _undoCoreSum, ...`) records only the few cells touched by one update, and `UndoVolatility()` reverts them. That is a delta-based rollback of a 1.5 KB structure instead of a copy.
- Inconsistency: Wma snapshots AFTER an isNew update (`_pState = _state`) and patches with `UpdateNewest`; Sma snapshots BEFORE; Cg snapshots the ring buffer. Each indicator hand-rolls its own variant (no shared mechanism), which is why bugs are possible.

Does it generalize to rewind? Only the principle: "state = small value struct; keep previous copy; revert = struct assignment". It is depth-1 and per-indicator. For our rollback (N ticks, whole world) it generalizes as: (a) flat blittable state, so a snapshot is a memcpy of the chunk; (b) ring of snapshots or a per-tick delta journal like Jma's. QuanTAlib offers no multi-step history store, no serialization, no checksum.

Bit-level caveat (code inference, not run): Sma isNew=true uses the Kahan-compensated `_state.Sum`, while isNew=false sets `_state.Sum = _buffer.Sum` (the ring buffer's non-Kahan FMA running sum). So "tick X then correct to Y" is not bitwise equal to "tick Y directly". Their test `Sma_IterativeCorrections_RestoreToOriginalState` asserts `Assert.Equal(expected, actual, 1e-10)`, i.e. tolerance, never bit equality. For rollback determinism we need revert paths to be bit-identical, so this pattern would hurt.

Batch vs streaming parity: not exact either. `Ema.Batch(span)` for len >= 256 and NaN-free uses `CalculateCleanCore`, which seeds ema with the first value and has no bias compensation, whereas streaming `Compute` uses bias compensation `state.Ema / (1.0 - state.E)`. Different algorithms, so differing values (tests use tolerances / validate against Skender etc).

## 4. Generators and templating

None in the library. Greps: no `.tt/.t4/.scriban`, no `IIncrementalGenerator`/`ISourceGenerator`/Roslyn references, no `[Generator]`. The only generated code is `python/src/Exports.Generated.cs` (3,813 lines, header `// <auto-generated />`), one `[UnmanagedCallersOnly(EntryPoint = "qtl_abber")]` wrapper per indicator with null/length checks and try/catch -> status codes. The generator script is not in the repo (I searched .py/.ps1/.cs; not found), so I only see its output. Code reuse at compile time is done by C# generics instead: `ComparePredicateBase<TOp> where TOp : struct, ICompareOp` with `static abstract bool Eval(...)` and zero-field structs (`AboveOp`, `BelowOp`, `EqualOp`) so the JIT specializes per operator with no virtual call; the net48 branch uses `default(TOp).Eval(...)`. Relevant to us: static abstract interface members need C# 11, not available in Unity C# 9; the `default(TOp).Eval` instance form works in C# 9 and Burst-style generic struct specialization. Boolean results are carried as 1.0/0.0 doubles (predicates are averaged by `BiInputIndicatorBase` with period 1). Nothing here compiles rules from a description.

## 5. Determinism

Not a goal of the library. Evidence:
- `Directory.Build.props` sets `<Deterministic>true</Deterministic>`, which only means reproducible compiler output, not numeric determinism. Tests compare with `1e-10` tolerance and against Skender/TA-Lib/Tulip; no bit-identical test, no cross-ISA test, no forced-ISA test (`grep DOTNET_EnableAVX|COMPlus|PreferredVectorBitWidth` = nothing). They test internal scalar vs vector cores directly (internals visible to the test assembly) but with tolerance.
- Sources of cross-machine bit differences in the code:
  1. `Vector<double>.Count` varies (2/4/8) and horizontal reductions run in lane order (`for j<vectorSize: result += sum[j]`), so `SumSIMD`, `VarianceSIMD`, Alma, Wma vector cores give different bits on different widths. (On .NET 8+ `Vector<T>` stays 256-bit on AVX-512 hardware unless configured: my knowledge, not verified.)
  2. ISA-branching algorithms: Sma/Wma Batch pick AVX-512 (tree prefix), AVX2 (tree prefix, `Fma.MultiplyAdd(vSums, vInvPeriod, vZero)`), NEON (sequential), else scalar Kahan: four different rounding orders, plus `len >= 256` and NaN-presence switching between them. The same input gives different bits depending on length, NaN presence and CPU.
  3. `DotProduct`: AVX-512 (8 lanes x 4 accumulators), AVX2 (4 lanes x 4, FMA or non-FMA depending on `Fma.IsSupported`), NEON, scalar (4 FMA accumulators) = different summation trees; the tail is non-FMA. 
  4. Transcendentals: 49 files call `Math.Exp`, 44 `Math.Log`, 26 `Math.Sin`, 55 `Math.Cos`, 12 `Math.Pow`, 11 `Math.Atan`. These go through the platform math library in .NET and are not guaranteed bit-identical across OS/CPU (my knowledge). Fatal for lockstep multiplayer; we must write our own polynomial/table versions.
  5. Kahan compensated summation is used in Sma/Wma (`y = delta - c; t = sum + y; c = (t - sum) - y`); safe under RyuJIT (no reassociation), but under Burst fast-math or any reassociating compiler it can be optimized away (Burst's default FloatMode I believe is not reassociating, to verify before relying on it).
- What is good: no `float`/`decimal` mixing, no `Parallel`/threads in indicator code (7 files mention Parallel/lock/Interlocked, all outside the hot indicators I looked at), the GBM feed is seeded, `Math.FusedMultiplyAdd` is exact in value everywhere. RyuJIT does not auto-contract `a*b+c` into FMA (my knowledge), so scalar code is stable; the risk is only explicit ISA-specific paths.

## 6. Benchmarks (skeptical reading)

Setup (`perf/Benchmark.cs`, `perf/perf.csproj`, `docs/benchmarks.md`): BenchmarkDotNet 0.15.3, `Job.ShortRun` (few iterations, wide variance), net10 JIT, one machine with AVX-512 (F+CD+BW+DQ+VL+VBMI per the doc), 500,000 bars from a seeded GBM, period 220, input in a pre-filled `double[]` (4 MB), output reused (`_quantalibOutput`), data NaN-free. A second harness `Progressive.cs` runs 1M bars with period 10..5000. Headline: SMA span 287.9 us for 500k = 0.58 ns/value, EMA 423.8 us = 0.85 ns, WMA 294.7 us, claims 1.2-1.7x faster than TA-Lib.

What it really covers / concerns:
- The measured path is the best case: contiguous NaN-free spans, AVX-512 box, inputs hot in L2/L3 (4 MB in + 4 MB out), no per-element object work. The SMA number is an O(1)-per-element prefix-scan kernel at roughly memory/L2 speed: plausible, not magic. "Faster than an L1 access" is marketing.
- EMA is a serial FMA dependency chain (`CalculateCleanCore`, 4x unrolled but still one chain); 0.85 ns is about FMA latency (~4 cycles at ~4.5-5 GHz). So it is at the latency floor, which makes the number believable, but it also means the doc's explanation "SIMD + FMA explains the speedup" is wrong for EMA (no SIMD there). And the EMA batch benchmark takes the "clean" path that skips bias compensation, so it is not computing the same thing as streaming QuanTAlib or TA-Lib (TA-Lib seeds EMA differently); comparison is not apples to apples.
- "TA-Lib (C library via P/Invoke)" per the docs, but `perf.csproj` references `TALib.NETCore 0.5.0` which (to my knowledge, not verified) is a managed C# port, not the native C library.
- ShortRun + a single machine + no cross-hardware data: "relative ratios hold across hardware" is asserted, not shown.
- `Sma_SpanBatch_ZeroAllocation` unit test does not measure allocation ("allocation is measured by BenchmarkDotNet, not unit tests"); zero alloc comes from `MemoryDiagnoser` in the benchmark only for the Span API. Streaming allocates 176 B per run (the instance); Batch(TSeries) allocates 16 MB; Eventing 16.8 MB.
- Streaming: 1,536 us / 500k = ~3 ns per `Update` for EMA with no corrections (`isNew=false` never benchmarked), no event subscribers. That is the one number relevant to "per-entity scalar state update" and it is decent, but it includes the 16-byte `TValue` return and `Pub?.Invoke` null check.
- `docs/architecture.md` tables (SoA vs AoS 7-8x; "0.4 us per bar RSI"; "40-60 bytes per instance") have no benchmark in the repo behind them (searched for the figures; only the doc contains them). Treat as unverified illustrations. `docs/benchmarks.md` Python tables likewise not reproduced.
- Nothing measures rollback, save/load, or 1M independent entities.

## 7. What to copy, and what to avoid

Copy (ideas, with adaptation):
1. Two-slot state with `isNew` flag: `if (isNew) prev = cur; else cur = prev;` as the minimal revise-without-advancing idiom; but in our design replace with whole-chunk snapshot ring (memcpy) or delta journal (Jma pattern: record only touched cells, undo in reverse) so revert is bit-identical.
2. SoA spans + a `Span<double>`-in/`Span<double>`-out kernel signature with no allocation; pointer-to-span zero-copy bridge (`new Span<double>(p, n)`) for FFI/Unity native arrays.
3. Branch-free select: `ConditionalSelect(mask, valueIfTrue, valueIfFalse)` after a comparison mask (Bop, Rsi) and compute-both-then-mask for guarded division. Use `Vector.Max/Min` for clamps and sign splits.
4. Hoist ISA/FMA checks outside loops and duplicate loop bodies (DotProduct's `useFma`), 4 independent accumulators to hide FMA latency, merge pairwise at the end.
5. Lazy NaN check (compute first, validate result) rather than a pre-pass.
6. Zero-field generic struct "operators" for compile-time specialization (`ICompareOp`), using the instance-method form `default(TOp).Eval` for C# 9/Burst.
7. Undo journal for large-state revert (Jma) as a model for per-tick deltas in a rollback log.
8. `[SkipLocalsInit]` on hot classes and `ArrayPool`/`stackalloc` threshold (`StackAllocThreshold = 256` doubles) for scratch buffers.

Avoid / would hurt us:
- Class-per-instance with `RingBuffer` class, pinned array, delegates, events (`Pub`), `Name` strings, `List<double>`: heap allocation per entity, pointer chasing, GC, not blittable, not savable in one memcpy.
- `[StructLayout(LayoutKind.Auto)]` everywhere (layout unspecified), `record struct` equality/ToString overhead; need `Sequential`/`Explicit` with fixed offsets.
- `double` only (we want float stats and int intrinsics); `Vector<T>` width variability; hardware-dependent algorithm selection and length-dependent switching; platform `Math.Exp/Log/Sin/Cos/Pow` (49/44/26/55/12 files).
- `%` for ring wrap (use power-of-two mask), `record struct` `with`-copies, event dispatch inside the update.
- Vectorizing within one series via prefix scan; we vectorize across entities instead (one lane = one entity, state in SoA columns).
- Tail loops with different arithmetic than the vector body (Sma tail uses non-Kahan add): in our library every lane, including remainder, must run the identical operation sequence; pad to the vector width instead.
- `Math.FusedMultiplyAdd` used for plain subtraction (RingBuffer), mixing FMA and non-FMA in the same reduction.
- Tolerance-based "restore" tests; we need bitwise tests (hash the chunk after rollback+replay == hash from straight-through run), run under forced ISA settings (`DOTNET_EnableAVX2=0`, `DOTNET_EnableAVX512F=0`, `DOTNET_PreferredVectorBitWidth`) and ideally fixed-width `Vector128/256`-only code or explicitly width-independent reduction order.

## Not verified / open

- Never executed any benchmark or test; all performance numbers are the repo authors' claims.
- Did not inspect JIT assembly, so which `Vector<T>` calls actually become FMA/blend, and the real Vector<T> width on AVX-512, is from memory of .NET behavior.
- Did not read the remaining ~430 indicators; patterns generalised from ~15. Some may use better or worse conventions (e.g. dedicated SIMD for Hma, Variance, Correl not read in detail).
- Did not find the generator for `Exports.Generated.cs`; may live outside the repo.
- Did not verify TALib.NETCore is managed; did not verify Burst FloatMode behavior; Burst compatibility of the repo code was not checked (it uses `List<T>`, delegates, events, strings, `ArrayPool`, none of which Burst accepts).
`````

### StateSmith generated C# (Balanced2) RocketSm.cs

Path: `StateSmith/src/StateSmithTest/Output/Algos/out/Balanced2_CSharp/RocketSm.cs`  (8332 bytes)

`````csharp
// Autogenerated with StateSmith.
// Algorithm: Balanced2. See https://github.com/StateSmith/StateSmith/wiki/Algorithms

#nullable enable

// Generated state machine
public partial class RocketSm
{
    public enum EventId
    {
        DO = 0, // The `do` event is special. State event handlers do not consume this event (ancestors all get it too) unless a transition occurs.
    }

    public const int EventIdCount = 1;

    public enum StateId
    {
        ROOT = 0,
        GROUP = 1,
        G1 = 2,
        G2 = 3,
    }

    public const int StateIdCount = 4;

    // Subtree meta data generation can be disabled in settings.
    // Details: https://github.com/StateSmith/StateSmith/issues/538
    public const int ROOT_SubtreeEndId = 3;  // State 'RocketSm' subtree extends from itself (id: 0) to state 'g2' (id: 3)
    public const int GROUP_SubtreeEndId = 3;  // State 'group' subtree extends from itself (id: 1) to state 'g2' (id: 3)
    public const int G1_SubtreeEndId = 2;  // State 'g1' subtree extends from itself (id: 2) to state 'g1' (id: 2)
    public const int G2_SubtreeEndId = 3;  // State 'g2' subtree extends from itself (id: 3) to state 'g2' (id: 3)

    // Used internally by state machine. Feel free to inspect, but don't modify.
    public StateId stateId;

    // State machine constructor. Must be called before start or dispatch event functions. Not thread safe.
    public RocketSm()
    {
    }

    // Starts the state machine. Must be called before dispatching events. Not thread safe.
    public void Start()
    {
        ROOT_enter();
        // ROOT behavior
        // uml: TransitionTo(ROOT.<InitialState>)
        {
            // Step 1: Exit states until we reach `ROOT` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `ROOT.<InitialState>`.
            // ROOT.<InitialState> is a pseudo state and cannot have an `enter` trigger.

            // ROOT.<InitialState> behavior
            // uml: TransitionTo(group)
            {
                // Step 1: Exit states until we reach `ROOT` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

                // Step 2: Transition action: ``.

                // Step 3: Enter/move towards transition target `group`.
                GROUP_enter();

                // group.<InitialState> behavior
                // uml: TransitionTo(g1)
                {
                    // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

                    // Step 2: Transition action: ``.

                    // Step 3: Enter/move towards transition target `g1`.
                    G1_enter();

                    // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
                    return;
                } // end of behavior for group.<InitialState>
            } // end of behavior for ROOT.<InitialState>
        } // end of behavior for ROOT
    }

    // Dispatches an event to the state machine. Not thread safe.
    // Note! This function assumes that the `eventId` parameter is valid.
    public void DispatchEvent(EventId eventId)
    {

        switch (this.stateId)
        {
            // STATE: RocketSm
            case StateId.ROOT:
                // state and ancestors have no handler for `do` event.
                break;

            // STATE: group
            case StateId.GROUP:
                // state and ancestors have no handler for `do` event.
                break;

            // STATE: g1
            case StateId.G1:
                G1_do();
                break;

            // STATE: g2
            case StateId.G2:
                G2_do();
                break;
        }

    }

    // This function is used when StateSmith doesn't know what the active leaf state is at
    // compile time due to sub states or when multiple states need to be exited.
    private void ExitUpToStateHandler(StateId desiredState)
    {
        while (this.stateId != desiredState)
        {
            switch (this.stateId)
            {
                case StateId.GROUP: GROUP_exit(); break;

                case StateId.G1: G1_exit(); break;

                case StateId.G2: G2_exit(); break;

                default: return;  // Just to be safe. Prevents infinite loop if state ID memory is somehow corrupted.
            }
        }
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state ROOT
    ////////////////////////////////////////////////////////////////////////////////

    private void ROOT_enter()
    {
        this.stateId = StateId.ROOT;
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state GROUP
    ////////////////////////////////////////////////////////////////////////////////

    private void GROUP_enter()
    {
        this.stateId = StateId.GROUP;
    }

    private void GROUP_exit()
    {
        this.stateId = StateId.ROOT;
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state G1
    ////////////////////////////////////////////////////////////////////////////////

    private void G1_enter()
    {
        this.stateId = StateId.G1;
    }

    private void G1_exit()
    {
        this.stateId = StateId.GROUP;
    }

    private void G1_do()
    {
        // g1 behavior
        // uml: do TransitionTo(g2)
        {
            // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition).
            G1_exit();

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `g2`.
            G2_enter();

            // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
            return;
        } // end of behavior for g1

        // No ancestor handles this event.
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state G2
    ////////////////////////////////////////////////////////////////////////////////

    private void G2_enter()
    {
        this.stateId = StateId.G2;
    }

    private void G2_exit()
    {
        this.stateId = StateId.GROUP;
    }

    private void G2_do()
    {
        // g2 behavior
        // uml: do [x > 50] TransitionTo(g1)
        if (x > 50)
        {
            // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition).
            G2_exit();

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `g1`.
            G1_enter();

            // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
            return;
        } // end of behavior for g2

        // No ancestor handles this event.
    }

    // Thread safe. This function can be disabled with `outputStateIdToStringFunction` setting.
    public static string StateIdToString(StateId id)
    {
        switch (id)
        {
            case StateId.ROOT: return "ROOT";
            case StateId.GROUP: return "GROUP";
            case StateId.G1: return "G1";
            case StateId.G2: return "G2";
            default: return "?";
        }
    }

    // Thread safe. This function can be disabled with `outputEventIdToStringFunction` setting.
    public static string EventIdToString(EventId id)
    {
        switch (id)
        {
            case EventId.DO: return "DO";
            default: return "?";
        }
    }

    // Returns the parent state for a given state. Returns ROOT if input has no parent.
    // Thread safe. This function can be disabled with `outputGetParentIdFunction` setting.
    public static StateId GetParentId(StateId id)
    {
        switch (id)
        {
            case StateId.ROOT: return StateId.ROOT;
            case StateId.GROUP: return StateId.ROOT;
            case StateId.G1: return StateId.GROUP;
            case StateId.G2: return StateId.GROUP;
            default: return StateId.ROOT;
        }
    }
}
`````

### StateSmith generated C# (Balanced1) RocketSm.cs

Path: `StateSmith/src/StateSmithTest/Output/Algos/out/Balanced1_CSharp/RocketSm.cs`  (10295 bytes)

`````csharp
// Autogenerated with StateSmith.
// Algorithm: Balanced1. See https://github.com/StateSmith/StateSmith/wiki/Algorithms

#nullable enable

// Generated state machine
public partial class RocketSm
{
    public enum EventId
    {
        DO = 0, // The `do` event is special. State event handlers do not consume this event (ancestors all get it too) unless a transition occurs.
    }

    public const int EventIdCount = 1;

    public enum StateId
    {
        ROOT = 0,
        GROUP = 1,
        G1 = 2,
        G2 = 3,
    }

    public const int StateIdCount = 4;

    // Subtree meta data generation can be disabled in settings.
    // Details: https://github.com/StateSmith/StateSmith/issues/538
    public const int ROOT_SubtreeEndId = 3;  // State 'RocketSm' subtree extends from itself (id: 0) to state 'g2' (id: 3)
    public const int GROUP_SubtreeEndId = 3;  // State 'group' subtree extends from itself (id: 1) to state 'g2' (id: 3)
    public const int G1_SubtreeEndId = 2;  // State 'g1' subtree extends from itself (id: 2) to state 'g1' (id: 2)
    public const int G2_SubtreeEndId = 3;  // State 'g2' subtree extends from itself (id: 3) to state 'g2' (id: 3)

    // event handler type
    private delegate void Func(RocketSm sm);

    // Used internally by state machine. Feel free to inspect, but don't modify.
    public StateId stateId;

    // Used internally by state machine. Don't modify.
    private Func? ancestorEventHandler;

    // Used internally by state machine. Don't modify.
    private readonly Func?[] currentEventHandlers = new Func[EventIdCount];

    // Used internally by state machine. Don't modify.
    private Func? currentStateExitHandler;

    // State machine constructor. Must be called before start or dispatch event functions. Not thread safe.
    public RocketSm()
    {
    }

    // Starts the state machine. Must be called before dispatching events. Not thread safe.
    public void Start()
    {
        ROOT_enter();
        // ROOT behavior
        // uml: TransitionTo(ROOT.<InitialState>)
        {
            // Step 1: Exit states until we reach `ROOT` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `ROOT.<InitialState>`.
            // ROOT.<InitialState> is a pseudo state and cannot have an `enter` trigger.

            // ROOT.<InitialState> behavior
            // uml: TransitionTo(group)
            {
                // Step 1: Exit states until we reach `ROOT` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

                // Step 2: Transition action: ``.

                // Step 3: Enter/move towards transition target `group`.
                GROUP_enter();

                // group.<InitialState> behavior
                // uml: TransitionTo(g1)
                {
                    // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition). Already at LCA, no exiting required.

                    // Step 2: Transition action: ``.

                    // Step 3: Enter/move towards transition target `g1`.
                    G1_enter();

                    // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
                    this.stateId = StateId.G1;
                    // No ancestor handles event. Can skip nulling `ancestorEventHandler`.
                    return;
                } // end of behavior for group.<InitialState>
            } // end of behavior for ROOT.<InitialState>
        } // end of behavior for ROOT
    }

    // Dispatches an event to the state machine. Not thread safe.
    // Note! This function assumes that the `eventId` parameter is valid.
    public void DispatchEvent(EventId eventId)
    {
        Func? behaviorFunc = this.currentEventHandlers[(int)eventId];

        while (behaviorFunc != null)
        {
            this.ancestorEventHandler = null;
            behaviorFunc(this);
            behaviorFunc = this.ancestorEventHandler;
        }
    }

    // This function is used when StateSmith doesn't know what the active leaf state is at
    // compile time due to sub states or when multiple states need to be exited.
    private void ExitUpToStateHandler(Func desiredStateExitHandler)
    {
        while (this.currentStateExitHandler != desiredStateExitHandler)
        {
            this.currentStateExitHandler!(this);
        }
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state ROOT
    ////////////////////////////////////////////////////////////////////////////////

    private void ROOT_enter()
    {
        // setup trigger/event handlers
        this.currentStateExitHandler = ptr_ROOT_exit;
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_ROOT_exit = (RocketSm sm) => sm.ROOT_exit();
    private void ROOT_exit()
    {
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state GROUP
    ////////////////////////////////////////////////////////////////////////////////

    private void GROUP_enter()
    {
        // setup trigger/event handlers
        this.currentStateExitHandler = ptr_GROUP_exit;
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_GROUP_exit = (RocketSm sm) => sm.GROUP_exit();
    private void GROUP_exit()
    {
        // adjust function pointers for this state's exit
        this.currentStateExitHandler = ptr_ROOT_exit;
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state G1
    ////////////////////////////////////////////////////////////////////////////////

    private void G1_enter()
    {
        // setup trigger/event handlers
        this.currentStateExitHandler = ptr_G1_exit;
        this.currentEventHandlers[(int)EventId.DO] = ptr_G1_do;
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_G1_exit = (RocketSm sm) => sm.G1_exit();
    private void G1_exit()
    {
        // adjust function pointers for this state's exit
        this.currentStateExitHandler = ptr_GROUP_exit;
        this.currentEventHandlers[(int)EventId.DO] = null;  // no ancestor listens to this event
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_G1_do = (RocketSm sm) => sm.G1_do();
    private void G1_do()
    {
        // No ancestor state handles `do` event.

        // g1 behavior
        // uml: do TransitionTo(g2)
        {
            // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition).
            G1_exit();

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `g2`.
            G2_enter();

            // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
            this.stateId = StateId.G2;
            // No ancestor handles event. Can skip nulling `ancestorEventHandler`.
            return;
        } // end of behavior for g1
    }


    ////////////////////////////////////////////////////////////////////////////////
    // event handlers for state G2
    ////////////////////////////////////////////////////////////////////////////////

    private void G2_enter()
    {
        // setup trigger/event handlers
        this.currentStateExitHandler = ptr_G2_exit;
        this.currentEventHandlers[(int)EventId.DO] = ptr_G2_do;
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_G2_exit = (RocketSm sm) => sm.G2_exit();
    private void G2_exit()
    {
        // adjust function pointers for this state's exit
        this.currentStateExitHandler = ptr_GROUP_exit;
        this.currentEventHandlers[(int)EventId.DO] = null;  // no ancestor listens to this event
    }

    // static delegate to avoid implicit conversion and garbage collection
    private static readonly Func ptr_G2_do = (RocketSm sm) => sm.G2_do();
    private void G2_do()
    {
        // No ancestor state handles `do` event.

        // g2 behavior
        // uml: do [x > 50] TransitionTo(g1)
        if (x > 50)
        {
            // Step 1: Exit states until we reach `group` state (Least Common Ancestor for transition).
            G2_exit();

            // Step 2: Transition action: ``.

            // Step 3: Enter/move towards transition target `g1`.
            G1_enter();

            // Step 4: complete transition. Ends event dispatch. No other behaviors are checked.
            this.stateId = StateId.G1;
            // No ancestor handles event. Can skip nulling `ancestorEventHandler`.
            return;
        } // end of behavior for g2
    }

    // Thread safe. This function can be disabled with `outputStateIdToStringFunction` setting.
    public static string StateIdToString(StateId id)
    {
        switch (id)
        {
            case StateId.ROOT: return "ROOT";
            case StateId.GROUP: return "GROUP";
            case StateId.G1: return "G1";
            case StateId.G2: return "G2";
            default: return "?";
        }
    }

    // Thread safe. This function can be disabled with `outputEventIdToStringFunction` setting.
    public static string EventIdToString(EventId id)
    {
        switch (id)
        {
            case EventId.DO: return "DO";
            default: return "?";
        }
    }

    // Returns the parent state for a given state. Returns ROOT if input has no parent.
    // Thread safe. This function can be disabled with `outputGetParentIdFunction` setting.
    public static StateId GetParentId(StateId id)
    {
        switch (id)
        {
            case StateId.ROOT: return StateId.ROOT;
            case StateId.GROUP: return StateId.ROOT;
            case StateId.G1: return StateId.GROUP;
            case StateId.G2: return StateId.GROUP;
            default: return StateId.ROOT;
        }
    }
}
`````

---

## Appendix G. The previous senior plan

### plan-sv.md (previous senior plan)

Path: `D:/GitHub/_work/plan-sv.md`  (67040 bytes)

`````
# plan-sv - sv serves 1,000,000 timelines x ~5 connections per tick

Written 2026-11-12 from the consultation text and its appendices only. Nothing was looked up, built or run.
Number tags: **[measured]** quoted receipt, **[derived]** arithmetic on measured, **[projected]** my estimate, **[target]** gate.
Projected numbers carry about +/-50 % error until step V7 replaces them with measurements.

## 0. Assumptions (stated once, used everywhere)

| # | Assumption | If false |
|---|---|---|
| B1 | A *connection* is one load or one store of a 4-byte per-row column, made by one active timeline in one tick (the shape of Appendix A) | re-cost section 4 |
| B2 | Modifier and event connections fire at clip edges, not every tick. Edge rate = 2 / clip duration. Default duration 300 ticks | M cost scales linearly; see section 4 |
| B3 | One `[TlState]` struct maps to exactly one ECS component type per world in tl.unity | sv columns must be declared in one shared unit; probe V0-e |
| B4 | A tl kind combines its entries in registers and stores once per (row, kind), as Appendix A shows | accumulate cost rises for P2 and P3 |
| B5 | Chunk-parallel streaming on the reference machine moves about 30 GB/s [derived: tl S5 touch job, 480-770 MB in 15.5 ms] | fold cost scales inversely; probe V0-c |
| B6 | 11 workers act as about 7 cores (from the brief) | divide differently |
| B7 | The sparse stat machine may run as managed code on Unity's main thread | step V5 kill rule |
| B8 | Timelines write 2 of the 4 intrinsics by default; the worst case is 4 | fold cost doubles; shown as "worst" |
| B9 | Reactions lag one frame (Weave D5, D7). Deltas written in tick t are folded at the start of tick t+1 | none; it is a standing decision |
| B10 | Entities 6.7.0 API names not present in Appendix A or the tl reports are unverified | probe V0-e |

## 1. Verdict

1. **Yes at 30 Hz, on both hosts.** sv projects to 1.6-4.1 ms per tick with no cross-row writes, and up to 7.6 ms with 50 % cross-row writes, against the 8.3 ms gate [projected]. P ~ 0.75 for P1, P2 and P3 at cross-row <= 10 %.
2. **60 Hz is borderline.** P1 projects 3.4-4.1 ms against 4.2 ms. It fails with 4 timeline-written intrinsics or 50 % cross-row writes. P ~ 0.45. Step V12 holds the reserves.
3. **The design.** A connection is one load or one store on a per-row column. sv never runs a call per visit. sv runs one fold per tick over the chunks that changed, and one sorted batch for clip edges.
4. **Bit-identity comes from three laws, not from care:** frozen reads, row ownership, integer-only cross-row sums. P ~ 0.9 for the hot path on both hosts and any worker count.
5. **RAM:** about 100 B per entity plus 40 B per live clip-bound modifier [derived]. That is 9-37 % below today's sv-on-Unity path (285 B [derived from measured]). It still adds 100-260 B to tl's 320 B.
6. **Biggest risk:** the stat machine (sets, blocks, modifiers) must compile for Unity at C# 9. Second: float fold bits under Mono. Third: assumptions B3 and B10.
7. **What dies:** sv calls per visit; the 40-byte Command for dense traffic; the dense clear of clause 13(b); `SvChunk`, `SvRange` and `Reconcile` in Unity; drift storage and mirrors on the Unity hot path; `delegate* unmanaged` as the portable core; "5 us at 1M" as a gate.

## 2. The architecture

### 2.1 Terms

- **Store column**: a per-row column with one writer, sv. Stat outputs (float) and intrinsic values (int). tl only reads it.
- **Accumulate column**: a per-row column that tl kinds add into (`+=`). It has exactly one consumer.
- **Delta column**: an int accumulate column whose consumer is the fold of one intrinsic.
- **Inbox**: an sv-owned `long` array indexed by slot. It receives integer deltas sent to *another* row.
- **Fold**: the one pass that turns `value`, `delta` and `inbox` into the new `value`, clamps it, clears `delta` and `inbox`, and reports which rows changed.
- **Word**: 64 consecutive rows. On .NET a word of the slot space. In Unity a word of one chunk (a chunk has at most 2).
- **Condition bit**: one bit per row that says "this check is true now" (for example hp below 30 %). sv keeps it. An **edge** is a tick where the bit flips.
- **Sink**: a list of `(slot, stat, bits)` that `Recalculate` fills when the host's stat storage is chunked.
- **StatOp**: one modifier add or remove, 24 bytes, produced at a clip edge.
- **Remote**: touching a row other than the timeline's own row.

### 2.2 The five laws (everything else follows)

1. **Frozen reads.** During the timeline step nobody writes a store column. Every kind, on every worker, reads the same snapshot. Reads need no order.
2. **Row ownership.** Accumulate cells of a row are written only by the worker that holds that row. Inside a row the order is (system order, kind declaration order, entry order). tl already fixes all three.
3. **Remote writes are exact integers.** A remote write is an atomic add into the inbox. Integer addition commutes, so arrival order cannot change a bit. Floats never cross rows.
4. **One fold per cell per tick.** `after = clamp(before + saturate32(delta + inbox))`. All deltas of a tick are summed first and clamped once. The result is a function of the multiset of deltas.
5. **Sparse things are sorted.** StatOps and events carry a total key built from slot, source and schema ids. The key never mentions a worker, a chunk or an arrival time.

Delete laws 3 to 5 and you still have a correct single-row engine. Delete the fold and the kinds still run. Each law removes one class of race instead of guarding it.

### 2.3 Mode table

ns per operation is wall time on the reference machine with 11 workers [projected].

| Mode | What the kind does | Data home, .NET | Data home, Unity | Writer | Order law | ns/op | Why it is parallel-safe |
|---|---|---|---|---|---|---|---|
| **R** read | one load | flat column, row = slot (Weave pinned array; sv `BindOutput`) | chunk component | sv, before the timeline step | law 1 | 0.15-0.3 | read-only in the step |
| **K** check | load + compare, or nothing when the check is a kind's `Enabled` bit | same column, or sv condition words | same column, or an enableable bit | sv fold | law 1 | 0-0.3 | read-only in the step |
| **A** accumulate | one `+=` per (row, kind) | flat effect column | chunk component | the tl kind that owns the row | law 2 | 0.1-0.5 | one worker per row |
| **D** delta | one `+=` into the delta column; the fold does the rest | flat delta column, folded by 64-row words | chunk delta component, folded per changed chunk | tl kind, then sv fold | laws 2 and 4 | A, plus 0.5-0.8 per folded cell | one worker per row; fold owns the chunk |
| **D remote** | one atomic add | `long inbox[slot]` | the same sv-owned array | any worker | law 3 | 1-2, worse on one hot target | atomic, commutative |
| **M** modifier | nothing per tick. A clip-edge pulse becomes a StatOp | per-worker lists, merged and sorted | tl's per-entity event buffer, collected and sorted | sv, main thread | law 5 | 50-300 per edge | single writer |
| **E** event | one store into a signal column, or nothing (the fold's edge bits are the signal) | condition words | enableable bits | sv derives events from bits | law 5 | 1 store; 5-10 per event | events are derived after the jobs |

On .NET, row = slot = entity index holds literally (Weave D3). In Unity the kind's row is the chunk row, and the slot rides in the `SvSlot` component, as sv's adapter already does. The identity is carried by the entity.

### 2.4 One picture

```
tick t, one clock (Frame.Tick = sv Game.Tick)

 main thread, sparse, O(edges)              workers, dense, O(changed rows)
 -----------------------------              -------------------------------
 1 Commands  journal -> StatOp, inbox
 2 Expire    wheel -> StatOp
 3 Apply     sort StatOps -> Board.Apply
 4 Recalc    sets, blocks -> StatWrite list
 5 Scatter   StatWrite list -> [stat] ------> [stat]  store, frozen from here
                                            6 Fold   [value] <- clamp([value] + [delta] + inbox, [stat])
                                                     [delta], inbox <- 0 ; condition bits, edge bits, digest
                                            7 Kinds  read [stat] [value] [bits]
                                                     write [delta] [effect] [signal] ; remote -> inbox
 8 Derive    edge bits -> events (cap, sort by slot) ; pulses -> StatOps for t+1
 9 Swap, digest, tape
```

Steps 1-5 cost microseconds to a fraction of a millisecond. Steps 6 and 7 move the memory. sv owns step 6. tl owns step 7 and does not change.

## 3. Answers

### SV-Q1. Access architecture

**Decision: option (d).** Its dense half is (c) with two repairs. Its sparse half is a sorted batch. Inline functions (a) serve reads and checks only. Command streams (b) die for dense traffic.

| Option | Cost of one write connection | sv ms at P1 / P2 / P3 [projected] | Verdict |
|---|---|---|---|
| (a) inline `Step` on the shared intrinsic | read-modify-write 8 B, bounds 4-8 B, ~5 ns CPU | 0.3-0.6 / 0.5 / 0.7 | Reserve only. The result depends on kind order. It cannot serve remote writes, so a second path is needed anyway |
| (b) per-worker command stream, sorted, folded | 12-40 B append, a 4-pass sort, an apply | >= 10 / >= 10 / >= 15 | **Kill first.** 5M x 12 B is 60 MB per tick before the sort. Keep it only for M (clip edges) |
| (c) dense effect columns, dense clear, fold every row | 1 store, plus a fold and a clear over all rows | 3.5-4.0 / 0.9-1.0 / 0.03 | The right law, the wrong sweep |
| (d) accumulate, consumer clears, fold only changed chunks, sorted batch for edges, atomic inbox for remote | 1 store, plus a fold over changed chunks | 1.6-3.2 / 0.4-0.8 / 0.02 (fold only) | **Build this** |

**The two repairs to (c).**
- *Consumer clears.* The fold reads `delta[row]` and writes 0 back while the line is in cache. No dense clear exists.
- *Change filter.* In Unity the fold visits a chunk only if its delta or bound component changed since the last fold (`chunk.DidChange`, the test tl's S4 already uses). tl's generated job requests a read-write pointer only for chunks with live kinds, so idle chunks cost nothing. On .NET the window mask is `playing | changed(bounds)` in 64-row words.

**Why the fold and not inline.** With 150 timelines on a row (P3) the fold does 150 plain adds and one clamp. Inline does 150 clamps. With remote writes, inline needs the inbox and a fold anyway. And law 1 (frozen reads) is what lets kind jobs run in any order.

**The fold law (scalar; every wider path must equal it).**

```csharp
public static class IntrinsicLaw
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Step(int before, int delta, int floor, int ceiling)
    {
        var low = floor < ceiling ? floor : ceiling;
        var moved = (long)before + delta;
        return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Applied(int before, int after) => Saturate((long)after - before);
}

public unsafe struct FoldWord
{
    public int* Value;
    public int* Delta;
    public float* Floor;
    public float* Ceiling;
    public int FloorConstant;
    public int CeilingConstant;
}

public static unsafe class Fold
{
    public static ulong Word(in FoldWord word, int rows)
    {
        var changed = 0ul;
        for (var row = 0; row < rows; row++)
        {
            var floor = word.Floor == null ? word.FloorConstant : Bcl.BoundOf(word.Floor[row]);
            var ceiling = word.Ceiling == null ? word.CeilingConstant : Bcl.BoundOf(word.Ceiling[row]);
            var before = word.Value[row];
            var after = IntrinsicLaw.Step(before, word.Delta[row], floor, ceiling);
            word.Value[row] = after;
            word.Delta[row] = 0;
            changed |= (after != before ? 1ul : 0ul) << row;
        }
        return changed;
    }
}

public static unsafe class Inbox
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Send(long* inbox, int targetSlot, int amount) => Interlocked.Add(ref inbox[targetSlot], amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Take(long* inbox, int slot, int local)
    {
        var total = IntrinsicLaw.Saturate(local + inbox[slot]);
        inbox[slot] = 0;
        return total;
    }
}
```

`Step` equals sv's existing `clamp(saturate(before + delta))` with ceiling-wins bounds (atoms 50 and 90). `Bcl.BoundOf` is the clause-16 conversion that atom 101 proved on .NET, Mono and Burst. Production loops hoist the null tests with the struct-strategy pattern of atom 24; the law above is the reference.

**Cost model [projected].** T = active timelines, N = rows, W = writes per timeline, Iw = intrinsics that timelines write.
- reads: 0.2 ns x (5 - W) x T
- accumulates: 0.35 ns x 0.95 x W x T at P1; 0.2 ns at P2; 0.1 ns at P3 (assumption B4)
- fold: 24 B per (row, folded intrinsic) at 30 GB/s = 0.8 ms per 1M rows per intrinsic
- digest: 0.17 ns per folded cell (one mix, 7 cores)
- M: 0.05 x W x T x (2 / 300) StatOps per tick x 0.3 us (Mono) or 0.05 us (.NET)
- remote write: 1.6 ns each at P1 and P2, 0.8 ns at P3, plus 0.1-0.3 ms for the dense inbox visit
- remote read: about 4 ns through an entity lookup; about 1.1 ns through a remote column (SV-Q2)

**Floor.** At P1 the kinds must move 10 MB of reads and 20 MB of read-modify-writes, and the fold 48-96 MB. If the fold shared the kind job's cache lines (reserve V12a) the floor is about 1.5-2.5 ms [projected]. The baseline is 3.4-4.1 ms. A remote write is a read-modify-write outside the stream; no design makes it cheaper than about 1 ns wall.

**Steps:** V2 (fold), V6 (Unity fold job), V8 (inbox), V12 (reserves). **Measure:** `fold1m` in sv-bench, then the Unity bench split by stage. **Kill:** in section 7. **Files:** new `src/Sv/Fold.cs`, `src/Sv/Inbox.cs`; `src/Sv/Kernel.cs`, `src/Sv/Simd.Chunk.cs`, `src/Sv.Burst/Kernels.cs`; new `packages/com.sv.unity/SvFoldJob.cs`.

### SV-Q2. Layout and RAM

**Where things live.**

| Data | .NET | Unity | Why |
|---|---|---|---|
| Stat outputs (8 floats) | flat arrays, bound with `BindOutput` | chunk components | tl's generated job already binds chunk components. No tl change |
| Intrinsic values (4 ints) | flat arrays | chunk components | same |
| Delta columns (2-4 ints) | flat arrays | chunk components | written by the row's owner |
| Condition and edge bits | words | enableable components | a kind can use the bit as its `Enabled` mask |
| Inbox (per remote-written intrinsic) | `long[slot]` | sv-owned native `long[slot]` | must be reachable from any worker |
| Slot state, dirty masks, modifier records, sets, blocks, wheel | sv-owned native | sv-owned native, keyed by stable slot | sparse; touched only at edges |
| `Entity` of slot | not needed | sv-owned, 8 B per slot | the scatter needs slot -> entity |

**How a Burst kind job reaches an sv column without a copy.** It does what it does today: `chunk.GetRequiredComponentDataPtrRO(ref handle)[row]`. The column *is* the chunk component. sv gets its values there in two ways:
- *Stats (sparse):* `Recalculate` appends `(slot, stat, bits)` to the sink instead of writing through one pointer. One Burst job on the main thread scatters the list through a component lookup. Cost O(changes), about 30-60 ns per write [projected; probe V0-d].
- *Intrinsics (dense):* the fold job writes them in place, chunk by chunk.

`BindOutput` (one pointer, one stride) stays the .NET path. The sink is the chunked path. Both carry the same canonical bits.

**Pointer stability (clause 12).** In Unity no pointer outlives a job. Each job resolves its chunk pointers itself. A structural change moves the component data with the entity and costs sv nothing: no renumber, no reconcile, no contiguity rule. Slots never move. `UnityPopulation.Reconcile` (64.8 ms at 1M [measured, Mono]) is deleted. The price is 8 B per slot for the `Entity` array and the lookup in the scatter.

**Spawn waves.** A wave of N x 8 sink entries would be 96 MB at 1M. So template spawn writes nothing to the sink: the host instantiates a prefab that already holds the template's stat values, and `Board.Spawn(template, count, slots)` assigns block ids (3.3 ms per 1M [measured, .NET]). One job writes the new `SvSlot` values.

**Remote reads.** A kind that reads another row's stat or intrinsic uses tl's lookup (`AnchorLookup` in Appendix A). That is about 30 ns of CPU per read. If probe V0 shows more than 5 % of reads are remote, add a *remote column*: an sv-owned flat `[slot]` copy of that one column (4 B per slot), refreshed by the scatter and the fold. A 1M-row copy is 4 MB and mostly stays in L3.

**Feature status.**

| Feature | Status | Reason |
|---|---|---|
| Blocks, sets, template spawn (atoms 60-66) | mandatory | stat identity in 8 B per slot |
| Modifier record pool (atom 63) | mandatory | 40 B per live modifier |
| Dirty masks, sparse change rows (62, 68) | mandatory | they drive the sink |
| Timing wheel (83, 96) | optional, lazy | only for sv-native timed modifiers; clip-bound modifiers end by pulse |
| Stat digest pages (72) | mandatory | plus one digest sum per (chunk, intrinsic) |
| Owned stat columns, mirrors (64, 34) | dead in Unity, optional on .NET | the chunk component is the column; the fold calls `BoundOf` inline |
| Per-pair state | already dead (atom 66) | - |
| Drift segments (89, 95, 97) | dead for delta-linked intrinsics | 32 B per slot per intrinsic would add 128 B per entity; regen is a timeline that writes a delta |
| Undo log (73-76) | optional, off by default at this scale | SV-Q5 |
| Bulk pool (81) | .NET only | Unity has the job system |
| `SvRange`, `SvChunk`, `SvChanged`, `Reconcile`, `RenumberSlots` | dead in Unity; kept for .NET and Frent | no contiguity needed |

**Setup time.** The sv components join tl's `ComponentTypeSet` in the same structural pass. Entity size grows 320 -> 380 B, so chunk capacity falls 49 -> 42 [derived] and tl visits 17 % more chunks. Board creation is 3 ms and template spawn 3.3-4.8 ms per 1M [measured, .NET]. Gate: setup no worse than tl's own setup plus 10 ms per 1M rows [target].

**Gain:** bytes table in section 4. **Measure:** `Board.MemoryBytes`, archetype report, process private bytes, at 1M and 4M. **Kill:** scatter above 0.5 ms at 10,000 writes -> use a slot -> chunk table rebuilt on order change. **Files:** `src/Sv/Board.Stats.cs` (sink), new `packages/com.sv.unity/SvScatterJob.cs`, `SvSlot.cs` kept, `SvChunk.cs`, `SvRange.cs`, `SvChanged.cs`, `SvSystem.cs` deleted.

### SV-Q3. Determinism under parallel access

**The exact law.** A tick's result is a pure function of (state at tick start, journal for the tick). It does not depend on worker count, chunk order or host, because:

| Thing | Rule | Why any schedule gives the same bits |
|---|---|---|
| Reads | store columns are frozen in the timeline step (law 1) | no writer exists |
| Row-local float accumulation | order = (system, kind, entry); one worker per row (law 2) | the order is data, not timing. Floats are never reduced across rows |
| Row-local int accumulation | wrapping 32-bit add; each contribution must satisfy abs <= 2^20 and at most 2^10 writer kinds per column; checked builds trap | wrapping add is a commutative group, so even a violation is deterministic |
| Remote accumulation | atomic 64-bit add into the inbox (law 3) | exact and commutative; 2^32 hits of 2^31 still fit |
| Fold | `Step(before, Saturate(delta + inbox), floor, ceiling)` once per cell (law 4) | a cell is touched by one worker; the input is a sum |
| StatOps | sorted by (slot, op, source, stat, kind, value bits); remove before add | total key (law 5) |
| Events | per channel, sorted by slot | total key (law 5) |
| Slot assignment | lowest free slot first (atom 65), in journal order, on the main thread | a function of the live bitmap and the journal |
| Digest | sum of `Key(column, slot, bits)` over live cells | addition commutes; chunk layout is invisible |

Saturating integer adds are **not** used for accumulation: `sat(sat(MAX + 1) - 1)` differs from `sat(MAX + (1 - 1))`. Sums are exact; saturation happens once, in the fold.

Sum-then-clamp is a semantic choice. Heal +10 and hit -20 on 95/100 give 85, not 80. Order-dependent sequences (shield, then spill into hp) stay on the sparse path: a StatOp-style command batch folded in arrival order through `ClampShift.Then` (atom 97). That path is for tens to thousands of operations per tick.

**The only float math in sv** is the stat fold, the derived-source multiply and the curve bucket. It runs on one thread, once per new set. It is put behind one static class (`StatMath`) so one compiled function serves each host. See the Mono pitfall in section 6.

**Clause changes:** 3, 4, 7, 12, 13 and 17, plus new 19 and 20, in section 5.

**Tests.**
- *Worker sweep:* the NorthStar2 scenario at 1, 2, 4, 7 and 11 workers gives one digest per tick and one event byte stream.
- *Chunk-order sweep (Unity):* the same journal on worlds built with different creation orders and forced archetype moves gives the same slot-keyed digest.
- *Shuffle:* contributions to a delta cell and sends to an inbox cell applied in 100 random orders give identical bits.
- *ISA legs (.NET):* default, `DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0` (the knobs atom 30 already used), and the `netstandard2.1` asset.
- *Host leg:* the baked NorthStar2 digest on .NET, on Mono in the editor and under Burst (the atom 101 method, with the disable-control run).

**Kill:** any digest difference between two worker counts stops the step; report the first divergent column and slot. **Files:** new `tests/Sv.Tests/FoldLawTests.cs`, `tests/Sv.Tests/ParallelLawTests.cs`, `tests/Sv.NorthStar2/`.

### SV-Q4. Unity, Burst and netstandard2.1

**Three tiers.** What must be Burst is small. What must merely run in Unity is larger. The rest stays .NET-only.

| Tier | Contents | Unity form | Size |
|---|---|---|---|
| 0, hot | fold law, `Bcl`, `Bits`, condition bits, cell digest, inbox, link structs | Burst, C# 9, no allocation | about 600-900 lines, 3 of the files exist (`src/Sv.Burst`) |
| 1, sparse | `Board` stats, `SetTable`, `BlockTable`, wheel, modifiers, schema, codec, sink, StatOp batch | managed, main thread, C# 9, `netstandard2.1` | most of `src/Sv` |
| 2, .NET only | `Simd*.cs`, `Bulk`, tape file IO, `Sv.Compose` (frozen), Frent adapter, generators | not compiled by Unity | - |

Evidence that tier 1 may stay managed: under Mono at 1M rows, expiry plus crossings took 4.1 us and a 128-change `Recalculate` 13.9 us [measured, atom 105]. The 33.8 ms of that receipt was the dense intrinsic passes, which move to tier 0.

**One source, two targets, no fork.** `src/Sv` multi-targets `net10.0;netstandard2.1` with `LangVersion` 9 for the whole project. .NET-only APIs sit behind `#if NET`. A fork would drift; a transform script hides errors until the editor runs. The compiler becomes the gate.

| Blocker (section 5.1 of the brief) | Action |
|---|---|
| `System.Runtime.Intrinsics` | `Simd*.cs` compile under `#if NET`. The scalar twins of `Sv.Burst/Kernels.cs` become the portable kernel. Rewrite of the dispatch only |
| `static abstract` (`IStateCodec.Reading`) | instance property on the struct codecs, on every target (the transform atom 105 recorded) |
| `delegate* unmanaged` | struct makers (`IEventMaker<T>`, atom 31) are the portable core. Function-pointer emitters move behind `#if NET` |
| `IsExternalInit`, records, `record struct`, primary constructors | plain classes and structs with constructors in `Schema.cs`, `Schema.Attributes.cs` |
| `BitOperations` | `Bits.Tzcnt/PopCount/Lzcnt/RotateLeft`; forwards to the BCL under `#if NET` (extends `Bcl.cs`) |
| `NativeMemory` | `Memory.Alloc/AllocZeroed/AllocAligned/Free`: `NativeMemory` under `#if NET`, else `Marshal.AllocHGlobal` with manual clear and 64-byte alignment. Never called from a job |
| `BitConverter` bit helpers | `Bits.OfSingle/ToSingle` by pointer cast |
| `Environment.TickCount64`, `OperatingSystem.IsLinux` | `Bulk.cs` and the `madvise` call are `#if NET` |
| `ObjectDisposedException.ThrowIf` | `Guard.NotDisposed` |
| `Interlocked` over `nint` | `long` or `IntPtr` overloads |
| managed `Board` inside `SvChunk` | `SvChunk` is deleted. Jobs take pointers and plain structs |

**SIMD.**
- *net10:* the existing width dispatch (256, 128, scalar). The fold is added as one more strategy. No `Vector512` path: stream kernels are bandwidth-bound (atom 81 measured 1.1-1.3x from 23 more cores), so the expected gain is under 5 % [projected]. Killed before it is built.
- *Unity:* branch-free scalar loops under `[BurstCompile(FloatMode = FloatMode.Strict)]`. Burst vectorizes them for the build target. No `Vector<T>`, no `System.Runtime.Intrinsics`, no `Unity.Burst.Intrinsics` unless the Burst Inspector shows a scalar fold loop.
- *netstandard2.1 without Burst:* the scalar twins. It is a correctness path, not a speed path.
- *Identity:* every hot kernel is integer math, compare-and-select, or the pinned `BoundOf`. Every width gives the same bits by construction. The existing receipts already pin this: one digest on AVX2, 128-bit and scalar [measured, atom 30], and one on .NET, Mono and Burst [measured, atom 101].

**Testing the no-AVX path on an AVX machine.** Each leg first asserts the flags it expects, then runs the pinned digests.

| Leg | Setting | Must observe |
|---|---|---|
| default | none | `Vector256.IsHardwareAccelerated` true |
| no-AVX2 | `DOTNET_EnableAVX2=0` | 256 off, 128 on |
| scalar | `DOTNET_EnableHWIntrinsic=0` | `Vector128.IsHardwareAccelerated` false |
| ns2.1 | test project references the `netstandard2.1` asset | scalar twins run |
| Burst editor | Burst on, probe = 1 | digest equal |
| Burst SSE2 | player build with the SSE2 target, digest scene | digest equal |

**Effort [projected]:** tier 0 two days; tier 1 three to five days, unknown until the census of step V0-a. **Risks:** hidden C# 10-12 syntax across `src/Sv`; Mono float behaviour; allocation by Mono where .NET does not. **Kill:** tier 1 not compiling in Unity 7000.0.0a16 with all tests green by the end of day 9. Then the hot path ships on tier 0 alone, the stat machine ships through the recorded transform pipeline of `tests/Sv.Editor` (promoted to `eng/unity-sync.sh`, sha-pinned), and the owner decides whether its language-version override is acceptable. **Files:** `src/Sv/Sv.csproj`, every `src/Sv/*.cs` that the census names, new `src/Sv/Bits.cs`, `src/Sv/Memory.cs`, `src/Sv/StatMath.cs`.

### SV-Q5. Dense versus sparse

**What is actually dense.** Only intrinsic deltas (and the tl effect columns). Stat changes stay sparse: a modifier changes at a clip edge, so its rate is `links x 2 / duration`, about 800 per tick at the default point (assumption B2). The O(change) machinery keeps its job. It just stops being asked to carry per-tick traffic.

**Breakeven.** Use the dense path for a unit (a chunk in Unity, a 64-row word on .NET) when
`changed_cells x c_sparse > cells x c_dense`.
With c_sparse about 8 ns (a random read-modify-write) and c_dense about 0.5 ns (a streamed cell), the switch is at 6 % changed [projected]. The fold's unit test is cheaper than the formula: *fold the unit if anything in it changed*. Inside a unit the loop is always dense.

**The per-column switch** is static, from the link table. An intrinsic with a delta link is a *dense intrinsic*: the fold owns it. An intrinsic without one is *sparse*: it is written only through `Kernel.Run` windows and command batches, as today.

| Machinery | When changes are few | When changes are about N | Rule |
|---|---|---|---|
| Stat recalc (dirty masks) | O(changes) | unchanged; stat changes are edge-bound | keep; waves go through template spawn |
| Digest | add `Key(new) - Key(old)`: 2 mixes per change | recompute the unit's sum on the fold visit: 1 mix per cell | switch at 50 % per unit; the total is the sum of unit sums on both paths |
| Tape | inputs, commands, digest | the same | the tape stores causes, not effects |
| Undo log | (address, old bytes) per write | off for dense columns | dense columns are host snapshots; SV-Q5 note below |
| Keyframes | dirty pages | sparse state by dirty pages; dense columns whole | cadence in seconds, not ticks |
| Timing wheel | O(due) | only sv-native timed modifiers | a clip-bound modifier is removed by its End pulse, never by both |
| Changed masks | sparse word indices | the fold returns the bits it computed | conditions consume the bits |
| Drift | closed form | not used | SV-Q2 |

**Rollback note.** An 8-frame undo window over dense columns means copying them every tick: 16 MiB per tick for 4 intrinsics at 1M rows, 128 MiB held, about 1.6 ms per tick at 10 GB/s [derived]. It is off by default. Weave's sandbox forks by replay (D17) and is designed for 100,000 rows. If the owner needs frame rollback at 1M, it is one flag and that cost.

**Atoms.** Keep: 50, 60-68, 70, 72, 77-79, 82-88, 90-94, 96, 98-101, 103, 104. Keep but leave unwired in Unity: 73-76 (undo), 81 (bulk), 89, 95, 97 (drift; atom 97's sort is reused for StatOps and events). Kill in Unity: 102 and the adapter half of 105. Reorder: Roadmap 6.1 (unmanaged core) is no longer a precondition; the fold needs only pointers.

**Gain:** the 4 dense passes, the applied scan and the effect clear become one pass over changed chunks. **Measure:** fold ms and digest ms as separate timers at 0 %, 6 %, 50 % and 100 % changed units. **Kill:** if the per-unit digest costs more than 0.7 ms at P1, compute it every K ticks and on demand (owner picks K). **Files:** `src/Sv/Fold.cs`, new `src/Sv/CellDigest.cs`, `src/Sv/Board.Stats.cs` (`DigestKey` reused).

### SV-Q6. The tl <-> sv seam

**The seam is data: two tables and three record types.** Neither library references the other. The glue (Weave) owns the tables.

```csharp
public enum LinkMode : byte { Read = 0, Check = 1, Accumulate = 2, Delta = 3, Modifier = 4, Event = 5 }

public enum LinkScope : byte { Self = 0, Remote = 1 }

public enum ColumnRole : byte { StatOutput = 0, Intrinsic = 1, Delta = 2, Inbox = 3, Effect = 4, Signal = 5, Condition = 6, Sense = 7 }

public enum LinkFault : byte { None = 0, UnknownColumn = 1, ReadOfAccumulate = 2, WriteOfStore = 3, RemoteFloat = 4, SecondConsumer = 5, RemoteWithoutInbox = 6 }

public readonly struct ColumnDef
{
    public readonly ulong Name;
    public readonly ushort Schema;
    public readonly ushort Consumer;
    public readonly ColumnRole Role;
}

public readonly struct Link
{
    public readonly ushort Kind;
    public readonly ushort Column;
    public readonly LinkMode Mode;
    public readonly LinkScope Scope;
}

public readonly struct StatWrite
{
    public readonly int Slot;
    public readonly uint Bits;
    public readonly ushort Stat;
}

public enum StatOpKind : byte { RemoveSource = 0, Add = 1 }

public readonly struct StatOp
{
    public readonly long ExpiresAt;
    public readonly int Slot;
    public readonly int Source;
    public readonly uint ValueBits;
    public readonly ushort Stat;
    public readonly ModifierKind Kind;
    public readonly StatOpKind Op;
}
```

- **Declaring connections.** A column is a plain struct the kind already declares as tl state. The glue lists each column once (`ColumnDef`: name hash, role, schema id, consumer) and each use once (`Link`: kind, column, mode, scope). Nothing new is written in a kind.
- **Resolving ids.** At load, names resolve through `Schema.TryStat` and `Schema.TryIntrinsic`. The schema is fixed at build time (Weave D20). `Links.Validate` is a total function that returns a `LinkFault`; it enforces "one column, one writer kind", "one consumer per accumulate column", "remote writes are integers with an inbox". `Links.Hash` is pinned beside `Schema.ContentHash`.
- **Pointer stability.** .NET: flat arrays allocated once at warmup. Unity: resolved per chunk per job. The inbox and all sv-owned arrays have fixed capacity and never move.
- **Time.** One clock. tl's Horizon/Glide skips state-machine work between edges, but kinds still evaluate every tick, so deltas arrive every tick and sv needs no time skipping. A modifier that belongs to a clip is added at the Start pulse and removed with `RemoveSource` at the End pulse (including an interrupted End). It gets `ExpiresAt = long.MaxValue` and no wheel entry, because pause, reverse and rate make the end tick unknowable at the start. The wheel serves only modifiers with their own duration. Source id for a clip: `0x40000000 | timeline << 8 | playbackSlot`.
- **Sparse clear.** Clause 13(b) is replaced: the consumer restores zero for the rows it consumes (section 5). No pass exists whose only job is clearing.
- **Internal traffic is not journal traffic.** Weave's 40-byte Command (D8) stays for mutations that come from outside the simulation, at tens per tick. Pulse-derived StatOps are 24 bytes, are never journaled, and are rebuilt by replay.

**sv signatures (all additions).**

```csharp
public static unsafe class Kernel
{
    public static void Fold(Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed = default);
}

public sealed unsafe partial class Board
{
    public void BindSink(StatId stat);
    public int PendingBroadcast { get; }
    public int TakeBroadcast(Span<StatWrite> destination);
    public int Apply(ReadOnlySpan<StatOp> canonical);
}

public static class StatOps
{
    public static void Canonicalize(Span<StatOp> ops, Span<StatOp> scratch);
}

public static class Links
{
    public static LinkFault Validate(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns);
    public static ulong Hash(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns);
}

public static class CellDigest
{
    public static ulong Key(int column, int slot, uint bits);
}
```

`TakeBroadcast` returns writes in ascending (slot, stat) order and removes them. `Apply` returns the number of operations that changed state; a stale or dead target is skipped, never thrown.

**tl signatures (additions only).**
- None for R, K, A and D. The generated job already loads and stores chunk components.
- None for M and E. Pulses already reach `TimelineEvent<T>` buffers (Appendix A).
- Optional, only if step V0-e finds no column metadata in the baked library: a read-only `KindColumn { ushort Kind; ushort Column; byte Access; }` table, so `Links.Validate` checks against tl's truth instead of a hand-written list.
- Reserve V12a only: one `partial void ChunkBegin(in ArchetypeChunk chunk)` call at the top of each generated job. An unimplemented partial method compiles to nothing, so tl's output and its 13 hashes do not change.

**Delete.** In `packages/com.sv.unity`: `SvChunk`, `SvRange`, `SvChanged`, `SvIntrinsicsSystem`, `UnityPopulation.Reconcile`. In the contract: host zeroing in 13(b). In Weave's plan: effect columns "zeroed once per frame" (D5a) becomes "cleared by their consumer". Nothing is deleted from sv's core; `Sv.Compose` stays frozen.

**Gain:** zero per-visit calls; 24 B instead of 40 B per edge operation. **Measure:** `statop10k` bench (ns per op), sink-versus-bound twin-board bit equality. **Kill:** batch `Apply` slower than the same calls one by one -> keep the API, implement it as the loop. **Files:** new `src/Sv/Link.cs`, `src/Sv/StatOp.cs`, `src/Sv/Board.Sink.cs`; `src/Sv/Board.Stats.cs`.

### SV-Q7. Checks and events at scale

**Checks cost nothing where nothing changed.** A check has three forms, cheapest first.

1. *A kind's gate.* If the check decides whether a kind runs at all, the condition bit is the kind's `Enabled` component. The generated job already does `active & enabled` per chunk. Cost per visit: zero.
2. *A value test inside a kind.* One load and one compare on a frozen column. No sv work.
3. *An edge-triggered rule with hysteresis* (Weave D9). sv keeps one state bit per row per condition. The fold already holds `after` and the bound for every row it visits, so it computes the bits there.

```csharp
public static class Condition
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Below(int value, int whole, int numerator, int denominator) => (long)value * denominator < (long)whole * numerator;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Next(ulong state, ulong enter, ulong stay) => (~state & enter) | (state & stay);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Edges(ulong state, ulong next) => state ^ next;
}
```

`enter` holds the rows below the trigger threshold (30 %), `stay` the rows still below the re-arm threshold (40 %). The comparison is exact integer math. So 4M reads become: zero work in unchanged chunks, one compare per (row, condition) in folded chunks, and one event per flipped bit. Stat-driven checks use `ChangedWordIndices` (atom 68) the same way.

**Events are a view of edge bits.** The fold never appends events. After the jobs, a derive step walks only the units whose edge word is non-zero and emits `(slot, payload)` through sv's existing mask walkers, then sorts each channel by slot (atom 97's stable radix sort). On .NET the words are already in slot order.

**The death wave (1M rows cross at once).** Policy, per channel, decided by one number:

1. Each fold job counts its edges. The counts are summed (commutative).
2. If `count <= Cap` (default 65,536 [assumption]; set per channel at build): derive and deliver every event, sorted.
3. If `count > Cap`: deliver **no** per-row events. Publish one `Dense { channel, tick, count }` event. The edge bits stay set until the next fold, and every handler of that channel must also offer a mask form that takes the bit words as a window.

There is no partial delivery, so no handler ever sees "the first 65,536 deaths". The decision depends only on `count`, so it is identical on every host. This respects clause 17: a dense signal travels as a column (here a bit column), never as a million make-calls. Derive cost at the cap: about 0.3-0.5 ms [projected]. Above the cap: zero.

**Signals from kinds (mode E)** use the same path: the kind stores a non-zero payload into a signal column; the derive step emits from the non-zero rows of changed units and clears them. One signal per (row, channel) per tick; a kind that needs several keeps using tl's per-entity event buffer.

**Gain:** condition work is O(changed units); event work is O(events) with a hard ceiling. **Measure:** derive ms at 0, 1,000, 65,536 and 1,000,000 edges; the last must take the dense branch and allocate nothing. **Kill:** sort above 0.5 ms at the cap -> lower the default cap to 16,384. **Files:** new `src/Sv/Condition.cs`, `src/Sv/EventStreams.cs` (derive entry over words), new `packages/com.sv.unity/SvDeriveJob.cs`.

### SV-Q8. Proof

| Claim | Test | Pass condition |
|---|---|---|
| Fold is the law | `FoldLawTests`: every width and the Burst twin against `Fold.Word`, and against `EagerOracle.FrameStep` with per-row deltas; lattice {MIN, -1, 0, 1, MAX} x crossed bounds x NaN and infinite stat bounds x all mask shapes | bit equality of value, delta (all zero) and changed bits |
| Partition invariance (L9) | one window versus shuffled disjoint sub-windows | identical |
| Worker invariance | NorthStar2 at 1, 2, 4, 7, 11 workers, .NET and Unity | one digest per tick, one event stream |
| Chunk-order invariance | SV-Q3 chunk-order sweep | one digest |
| ISA legs | SV-Q4 table | pinned digests on every leg; each leg asserts its flags first |
| Host identity | NorthStar2 baked digest on .NET, Mono, Burst; Burst proven by the disable-control run and a `[BurstDiscard]` probe in every job | equal |
| 0 B, .NET | `GC.GetTotalAllocatedBytes(true)` over 2,000 warm frames, workers included; `NativeArena.Live` unchanged; native allocation call count 0 | exactly 0 |
| 0 B, Unity | (1) main thread: `GC.GetAllocatedBytesForCurrentThread` delta around the update is 0 over 1,000 ticks; (2) workers: every job's Burst probe reads 1, and Burst code cannot allocate managed memory; (3) null window: the same process-wide window with the sv and tl systems disabled, reported as `window - null window` | (1) 0, (2) all 1, (3) 0. The unattributed 5-6 KB then lands in the null window or is attributed |
| North-star against tl 4.0 | new `tests/Sv.NorthStar2`: 3 ticks plus replay through the frame of section 2.4, with tl 4.0 as a test-only reference (clause 8). The old `Sv.NorthStar` stays pinned to tl 1.3.0 as a frozen legacy suite | live = replay; .NET = Unity |
| No test loosened | the 446 existing tests run unchanged on net10; the count only grows | 446 + new |
| Performance | owner's machine; 5 fresh processes per arm, ABAB order; 32 warm-up ticks; 6 rounds of 32 ticks, first round dropped; median of process medians with the range; Unity 7000.0.0a16, 11 workers, Burst on, safety checks, job debugger and leak detection off, all recorded | section 4 gates |
| Kill rules | each step's rule is a script exit code, not a judgement | section 7 |

Every result line carries: shape, W, cross-row share, worker count, ISA leg, Burst probe, and the split sv-main / scatter / fold / tl tick / tl kinds / derive.

### SV-Q9. Order of work

The table, the acceptance gates and the rules for agents are in section 7.

### SV-Q10. Room for grid-influence and needle-cs

Six decisions, settled now so neither needs sv rework later.

1. **`ColumnRole.Sense` exists from day one.** A sense column is a store column whose writer is not sv. It is integer (`int` or `short`), written before sv's part of the frame, frozen like every store column. Conditions accept any integer store column as input, so `field_above` is the same code as `stat_below`.
2. **Stats do not read sense columns.** A sense value changes a stat only through a StatOp issued by a rule. This keeps sets immutable and interned.
3. **Column ids are 16-bit, in module blocks of 256** (the pattern of Weave D7 and D8). The digest key takes `(column id, slot, bits)`, so a new module's columns enter the digest without a format change.
4. **Column digests are slot-keyed sums, never byte streams.** Weave D18 hashes "bytes of every column". In Unity, byte order is chunk order, which is not canonical. `CellDigest.Key` is the one export both later modules use. grid-influence keeps its own `LayerHash` beside it.
5. **Every external integer write enters through the inbox.** A journaled command from needle-cs (`ApplyDelta`, `SetIntrinsic` as a delta, D10) becomes an inbox add in the Commands phase and is folded with everything else. One write semantic for intrinsics, whatever the source. External modifier changes become StatOps in the same sorted batch.
6. **Model output never touches a column.** It becomes a journaled Command at tens per tick (D8, D12). Nothing on the dense path knows needle exists, and replay needs no model.

One caution for the grid-influence round: its reads lag one frame behind its `Process`. With law 1 that is automatically consistent, but a rule that mixes a stat (this tick's recalc) with a sense value (last tick's field) should say so in the designer guide.

## 4. Two tables

### 4.1 Bytes per entity (8 materialized stats, 4 intrinsics)

"Before" is today's sv on Unity as measured in atom 105: a 16-stat board of 244,008,916 B at 2^20 rows = 233 B [measured], which includes 2 wheel-timed modifiers per entity, plus 8 bound stat columns (32 B), 4 intrinsic components (16 B) and `SvSlot` (4 B) = **285 B** [derived]. `m` = live modifiers per entity.

| Item | Before | After, Unity | After, .NET | What changed |
|---|---:|---:|---:|---|
| Stat outputs, 8 x 4 B | 32 | 32 (chunk) | 32 (flat) | home only |
| Intrinsic values, 4 x 4 B | 16 | 16 (chunk) | 16 (flat) | home only |
| Delta columns, 2 x 4 B (worst 4 x 4 B) | 0 | 8 (16) | 8 (16) | new; replaces per-call writes |
| `SvSlot` | 4 | 4 | 0 | - |
| Mirrors, 4 x 4 B | 16 | 0 | 0 | `BoundOf` inline in the fold |
| Slot state + dirty mask | 12 | 12 | 12 | - |
| `Entity` of slot | 0 | 8 | 0 | pays for no-reconcile |
| Inbox, 1 remote-written intrinsic | 0 | 8 | 8 | new |
| Condition and edge bits, live bits, masks | ~3 | ~0.5 | ~0.5 | bits instead of words per stat |
| Shared set and block tables (<= 64k blocks) | ~11 | ~11 | ~11 | amortized at 1M; ~3 at 4M |
| Modifier, wheel-timed | 80 x m | 80 x m | 80 x m | 40 B record + 40 B wheel entry |
| Modifier, clip-bound | - | 40 x m | 40 x m | no wheel entry |
| Not itemized in the measured board (pool slack, masks for 16 stats) | ~31 | 0-31 | 0-31 | V11 measures it |
| **Total, m = 0** | ~125 | **~100** | **~88** | |
| **Total, m = 2 wheel-timed** | **285** | ~260 (-9 %) | ~248 | |
| **Total, m = 2 clip-bound** | - | **~180 (-37 %)** | ~168 | |

| Scale | After, Unity [derived] | Note |
|---|---|---|
| 1M rows, m = 0 / 1 clip-bound | 100 MB / 140 MB | tl's archetype is 320 MB beside it [measured: 320 B] |
| 4M rows, m = 0 / 1 clip-bound | 365 MB / 525 MB | shared tables amortize to ~3 B |
| 10k players x 150 timelines | 1.0-2.6 MB | sv bytes follow rows, not timelines |

Setup: one structural pass shared with tl; chunk capacity 49 -> 42 [derived]. The largest remaining line is the modifier record. Step V11 offers a 16 B record for clip-bound modifiers (no id, no expiry, no wheel link); it is optional and has a kill rule.

### 4.2 Milliseconds per tick for sv (Unity, 11 workers) [projected]

"Before" is the per-call path of the brief: 5M x 20-40 ns over 7 cores = **14-29 ms** for P1 and P2 and **21-43 ms** for P3 [derived]. What actually exists in Unity today is slower: 33.8 ms for four dense passes at 1M rows under Mono [measured], with no Burst path.

"After" uses the cost model of SV-Q1 with Iw = 2. Columns are the cross-row share of writes. The gate is 8.3 ms at 30 Hz and 4.2 ms at 60 Hz [target].

| Shape | W | Before | After, 0 % | After, 10 % | After, 50 % | Worst case (Iw = 4), 0 % |
|---|---:|---:|---:|---:|---:|---:|
| P1: 1M rows x 1 | 1 | 14-29 | 3.4 | 3.9 | 4.5 | 5.3 |
| P1 | 2.5 | 14-29 | 3.8 | 4.5 | 6.1 | 5.7 |
| P1 | 4 | 14-29 | 4.1 | 5.0 | 7.6 | 6.0 |
| P2: 250k rows x 4 | 1 | 14-29 | 1.7 | 2.0 | 2.6 | 2.2 |
| P2 | 2.5 | 14-29 | 1.8 | 2.3 | 3.9 | 2.3 |
| P2 | 4 | 14-29 | 2.0 | 2.7 | 5.3 | 2.5 |
| P3: 10k rows x 150 | 1 | 21-43 | 1.6 | 1.7 | 2.2 | 1.6 |
| P3 | 2.5 | 21-43 | 1.6 | 1.9 | 3.1 | 1.6 |
| P3 | 4 | 21-43 | 1.6 | 2.1 | 4.0 | 1.6 |

Reading the table:
- **Counting only the 80 % of timelines that touch sv** scales reads, accumulates and M by 0.8. P1 at W = 2.5 goes 3.8 -> 3.5 ms. The fold does not shrink, because it is paid per row.
- **Where the time is at P1, W = 2.5:** fold 1.6, accumulates 0.8, reads 0.5, digest 0.3, StatOps on Mono 0.25, fixed job overhead 0.25, events 0.1.
- **30 Hz:** every cell passes. **60 Hz:** P2 and P3 pass up to 10 % cross-row; P1 passes only at 0 % with Iw = 2, and misses by up to 3.4 ms at 50 % cross-row and W = 4.
- **Remote reads** are not in the table. Each costs about 4 ns through a lookup or 1.1 ns through a remote column. 250,000 remote reads add 1.0 ms or 0.3 ms.
- **On .NET** the fold is single-threaded on sv's existing kernel: about 0.25 ms per 1M rows per intrinsic at 4 G rows/s [projected from the 2.9-6 G rows/s receipts]. sv is not the bottleneck there; tl's sequential tick with kinds is (81 ms at 4M x 4 [measured]).
- **Per access:** 3.8 ms / 5M = 0.76 ns wall, against the brief's 1.7 ns budget at 30 Hz and 0.8 ns at 60 Hz.

## 5. Contract amendments

**Procedure.** The agent-bus is unreachable, so the owner approves. `contract/` holds verbatim copies and is never edited: add one new file, `contract/CONTRACT-v2.1.0.md`, with a header line "owner-approved, bus unreachable", and vendor it into the README. `AGENTS.md` needs one owner-approved line saying the owner replaces the bus until it returns.

**Step A0, ratify what is already built (no code change, no digest moves).** The code is ahead of the vendored v1.0.3: the v1.1.0 batch (bus 0036), clause 18 (saturating add, bus 0042), the seven v1.2 items of `issues/0097`, and the implemented v2.0 items (history-free fold, `Cap` kind, indirect stats, crossings on the wheel). Their pins already moved at atoms 90 and 98. v2.1.0 starts by folding that text in as written in `issues/0097`.

**Step A1, the new text.**

> **Clause 3, appended.** The canonical order of a modifier set is (op-kind, value bits) over the kinds Added, Additive, Multi, Cap; insertion ids are removal handles only. Accumulation into columns is ordered by clause 13, not by this clause.

> **Clause 4, appended.** A host whose stat storage is chunked binds a sink instead of a column. Recalculate appends one (slot, stat, value bits) record per changed cell in ascending (slot, stat) order, and the host stores every record before the intrinsic fold of the same frame. A record carries the canonical bits a bound column would have received. Template spawn appends nothing; the host fills spawned rows from the template.

> **Clause 7, replaced.** Frozen frame order: input edges -> commands and expiry, as modifier operations in canonical order -> stat recalc + broadcast -> intrinsic fold -> timeline step -> kernels -> condition and event derivation -> swap. Mid-frame structural changes apply next frame. Store columns (stat outputs, intrinsic values, condition bits, sense columns) are read-only from the end of the fold to the end of the timeline step. Intrinsic values change only in the fold, in event-drain handlers and in condition responses. The fold reads the bounds broadcast in the same frame; drain handlers read the previous broadcast. Accumulate columns written in the timeline step of frame t are consumed by the fold of frame t+1.

> **Clause 12, appended.** On chunked hosts no pointer into host storage outlives the job that resolved it. sv retains only slot-indexed arrays that it owns; they are fixed in capacity and address from warmup to dispose.

> **Clause 13, replaced.** Accumulate columns (`+=`): (a) one column, one writer kind, one consumer; store-writers and accumulate-writers never share a column; (b) every cell of an accumulate column is zero when the timeline step begins; the consumer restores zero in each cell it consumes, and a column without a registered consumer is zeroed by the host; (c) a row's cells are written only by the worker that holds the row, in (system, kind declaration, entry) order; (d) integer accumulation is exact 32-bit wrapping addition; each contribution is bounded (magnitude at most 2^20, at most 2^10 writer kinds per column) so that no sum wraps, and checked builds trap a wrap; (e) a contribution to another row is an integer, is added atomically to that row's 64-bit inbox cell, and is consumed by the same fold; (f) float accumulate columns are row-local.

> **Clause 17, appended.** Events raised by the fold are derived from edge bits after the parallel step, per channel in ascending slot order. A channel has a build-time capacity. When a frame's edges exceed it, the channel delivers one Dense record carrying the count and no per-row events; the edge bits stay readable until the next fold, and every handler of the channel provides a mask form. The portable maker is the struct maker.

> **Clause 19, new. Intrinsic fold.** For each live row of a dense intrinsic, once per frame: after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling), computed without intermediate overflow; delta and inbox become zero; applied = saturate32(after - before). The result is a function of the multiset of the frame's contributions. An implementation of any width, on any host, equals the scalar reference bit for bit.

> **Clause 20, new. Connections are data.** Every use of a column by a kind is declared as (kind, column, mode, scope), with mode in {Read, Check, Accumulate, Delta, Modifier, Event} and scope in {Self, Remote}. The table is validated at load against the schema, and its hash is part of state identity. Modifier and Event connections act at clip edges through canonical batches; modifier operations are ordered by (slot, operation with remove before add, source, stat, kind, value bits).

> **Portable-surface rule, replaced.** Unity-visible sources are C# 9 on netstandard2.1. The portable core is static functions over pointers and plain structs, with struct-generic strategies. `static abstract` members, `delegate* unmanaged` and `System.Runtime.Intrinsics` are .NET-only front doors over the same core.

**What this breaks.** The frame loop of `Sv.Compose` (frozen, not updated). Hosts that rely on host zeroing of effect columns. Hosts that emit one event per row from `applied` at full density. The Unity adapter v2 of atom 102. Weave plan lines D5(a) ("zeroes once per frame") and 4.4 step 4 need the same one-line edit.

**Digests.**

| Pin | Moves? |
|---|---|
| Cross-host scenario digest `A2ACAA69E55F79D8` | No. Existing kernels keep their semantics |
| Burst kernel scenario `2B43BC1549AAE751` | No. That scenario is frozen; the fold gets a second scenario with its own new pin |
| North-star migration digests (tl 1.3.0 on `Sv.Compose`) | No. Frozen legacy |
| tl's 13 pinned hashes | No. tl's behaviour does not change |
| NorthStar2 digest, fold scenario digest, link-table hash | New pins, baked once on .NET, then asserted on every leg |
| `SvState` and keyframe magics | Only if the 16-byte clip-bound record of step V11 lands; then one bump |
| Tape v4 | No |

## 6. Pitfall checklist

**Unity and Burst**
- [ ] A job that ran managed is measured as Burst. Put a `[BurstDiscard]` probe in the fold, scatter and derive jobs; a run with any probe at 0 is void.
- [ ] `chunk.DidChange` compares against the *fold system's* last version. The fold's own read-write request must not make every chunk look changed next tick. Test: three ticks with no kinds live fold zero chunks on ticks 2 and 3.
- [ ] tl's job requests read-write pointers for every bound column of a live chunk, so the fold gets false positives. They cost time, never correctness. A missed chunk would be a bug: every delta writer must go through a read-write handle.
- [ ] The inbox has no change version. On a tick with any send to an intrinsic, its fold ignores the filter and visits every chunk.
- [ ] Chunks hold at most 128 rows; enable bits beyond `chunk.Count` are garbage. Mask with the count before any popcount.
- [ ] Write enableable bits through the Entities enabled-mask API, not by raw bit writes: Unity caches per-chunk disabled counts. [API name unverified, B10]
- [ ] `Interlocked.Add` on `long*` needs 8-byte alignment. Allocate the inbox 64-byte aligned. Mark the pointer `[NativeDisableUnsafePtrRestriction]` and the job as allowed to write it in parallel.
- [ ] A generic job (`IntrinsicFoldJob<TValue, TDelta, TCeiling>`) must be registered with `RegisterGenericJobType` in the glue assembly, or it runs managed.
- [ ] `FloatMode.Strict` on every job that reads a stat as a bound. Never `(int)float`; always `BoundOf`.
- [ ] No allocation, exception, managed object or mutable static reachable from a job. All sv arrays are sized at warmup; exceeding a capacity is a deterministic failure reported after the job.
- [ ] Adding the sv components in a second structural pass doubles setup. They join tl's first `ComponentTypeSet`.
- [ ] The first editor session after any sv change runs managed copies (Burst cold start). Bench in a second launch, as tl does.

**Mono, IL2CPP, netstandard2.1**
- [ ] **Mono may keep float intermediates in double** (the north-star notes say "Mono contracts/promotes"). The stat fold runs on Mono in tier 1. Test 1M random sets: Mono bits versus .NET bits. If any differ, compile `StatMath` with Burst as a function pointer (the atom 101 mechanism) and call it from managed code; it runs once per new set.
- [ ] Mono may allocate where .NET does not (boxing through constrained calls, `stackalloc` spans, closures). The 0 B proof must run under Mono, not only on .NET.
- [ ] C# 10-12 syntax leaks: file-scoped namespaces, `record struct`, primary constructors, `init`, collection expressions, raw strings, `required`, list patterns. `LangVersion` 9 on the whole project makes them build errors.
- [ ] `System.Runtime.CompilerServices.Unsafe` and `SkipLocalsInit` are not in netstandard2.1. Use pointers; gate the attribute with `#if NET`.
- [ ] `NET` is defined only for net5+. Unity's compile must take every `#else`.
- [ ] IL2CPP: every generic instantiation must be statically reachable; no generic virtual methods on the hot path.
- [ ] `Vector256.IsHardwareAccelerated` is a JIT constant but a build-time constant under AOT. Test AOT builds as their own legs if shipped.

**Determinism**
- [ ] Never use chunk index, `Entity.Index`, worker index or arrival order as a key. Slot only.
- [ ] Slot assignment must happen on the main thread in journal order on both hosts, or digests differ although values agree.
- [ ] `Acquire` zeroes the slot's inbox cells and condition bits. A stale remote send to a dead slot must not reach the next occupant; Weave's one-frame quarantine (D25) covers the gap.
- [ ] A delta cell must be non-zero only in rows the fold will visit. A kind that writes through a cached pointer bypasses the change filter.
- [ ] A bound change with no delta still needs a fold (re-clamp). The scatter bumps the bound's change version; the .NET mask ORs in `ChangedWords`.
- [ ] Saturating adds are not associative. Accumulate exactly; saturate once in the fold.
- [ ] A clip-bound modifier is removed by pulse *or* by wheel, never both. Two End pulses for one Start (rewind, interrupt) must be idempotent: `RemoveSource` of an absent source is a no-op.
- [ ] Same-tick End and Start on one (slot, source): remove sorts before add, so a re-trigger refreshes.
- [ ] The sink must carry canonical NaN bits (atom 48), exactly like a bound column.
- [ ] The digest key includes the column id. Two intrinsics with equal values must not cancel.
- [ ] Sum-then-clamp is not sequential clamping. Content that needs "heal, then hit" uses the ordered command batch.
- [ ] `DOTNET_` knob names changed between .NET versions. Each ISA leg asserts the flags it observes before it runs.

## 7. Order of work (SV-Q9)

Two weeks, two agents. One step is one PR and one sv atom (106 onward) with its issue file and receipts. Days overlap where a second agent can work alone.

| Step | Day | Change | Files | Expected gain | Measurement | Kill rule | Contract |
|---|---|---|---|---|---|---|---|
| V0 | 1 | Probes, no engine change: (a) C# 9 / netstandard2.1 error census of `src/Sv`; (b) sv-bench on the owner's machine; (c) Unity touch job over the fold's bytes at 1M and 4M; (d) lookup-write cost at 128, 10k and 1M writes; (e) tl.unity facts: B3, read-write pointer behaviour, column metadata, enabled-mask API; (f) Mono cost of 1k and 10k modifier operations plus recalc; (g) `Interlocked.Add` in Burst on random targets and on one hot target; (h) Mono versus .NET stat-fold bits on 1M random sets | `benchmarks/Sv.Bench`, `tests/Sv.Editor`, an untracked Unity harness; report in `issues/0115` | none; it replaces B3, B5, B7 and B10 with facts | one numbers table | none | no |
| V1 | 2 | Tier 0 portable: `Bits`, the scalar kernel as shared source, multi-target build for tier-0 files, ISA legs | `src/Sv/Bits.cs`, `src/Sv.Burst/*`, `src/Sv/Sv.csproj` | enabler | pins `A2ACAA69E55F79D8` and `2B43BC1549AAE751` unchanged on every leg | none | no |
| V2 | 2-3 | Fold: `IntrinsicLaw`, `Fold.Word`, `Kernel.Fold` (scalar, 128, 256), condition bits, `CellDigest`, `Inbox.Take` | new `Fold.cs`, `Condition.cs`, `CellDigest.cs`, `Inbox.cs`; `Kernel.cs`, `Simd.Chunk.cs`; `FoldLawTests` | one pass replaces four passes, the clear and the applied scan | `fold1m`: 1M rows x 4 intrinsics, one thread, <= 1.5 ms [target] | not 10 % faster than `Kernel.Run` plus a masked clear -> ship the fold as those two calls | clause 19 |
| V3 | 3-4 | Sink, StatOp batch, link table | new `Board.Sink.cs`, `StatOp.cs`, `Link.cs`; `Board.Stats.cs` | 24 B per edge; chunked hosts served | twin boards: sink bits = bound-column bits; `statop10k` <= 60 ns per op [target] | batch slower than the same calls in a loop -> implement it as the loop | clauses 4, 20 |
| V4 | 4-5 | .NET frame of section 2.4, NorthStar2 (.NET leg), `frame1m` bench for P1, P2, P3 | new `tests/Sv.NorthStar2`, `benchmarks/Sv.Bench/Frame1mBench.cs` | first end-to-end numbers | live = replay; workers 1 to 11 equal; ms per stage | a digest differs by worker count -> stop and report | clauses 7, 13 |
| V5 | 5-9 | Tier 1 at C# 9 / netstandard2.1, fixed at source; `StatMath`; `Memory` | the files V0-a names | Unity can run the stat machine | 0 errors on both targets; 446 tests green on net10; netstandard2.1 test leg green | not done by the end of day 9 -> transform pipeline plus owner decision (SV-Q4) | portable-surface rule |
| V6 | 6-9, second agent | Unity adapter v3: link registry, scatter, fold job, derive job, frame system; delete v2 | `packages/com.sv.unity/*`, `tests/Sv.Unity.Facade`, `tests/Sv.Unity.Tests` | a Burst path exists | compiles in 7000.0.0a16 with 0 `error CS` and 0 Burst errors; every probe 1; facade tests green | B3 false -> stop and report | clause 12 |
| V7 | 9-10 | Unity gate: NorthStar2 digest equals .NET; bench P1, P2, P3 across W; RAM report | harness only | replaces the projections of section 4 | section 4 tables, measured | fold above 3.5 ms at P1 with Iw = 2 -> V12a; total above 8.3 ms -> stop and report | no |
| V8 | 10 | Remote: inbox sends; remote columns if V0 calls for them | `Inbox.cs`, fold job, glue | cross-row works and is order-free | 10 % and 50 % cross-row; the adversarial one-target case | one-target case above 3 ms -> per-worker streams partitioned by slot range | clause 13(e) |
| V9 | 11 | Events from edge bits: cap, sort, dense branch; conditions as kind gates | `EventStreams.cs`, new `SvDeriveJob.cs` | O(events), bounded | 0, 1,000, 65,536 and 1,000,000 edges | sort above 0.5 ms at the cap -> cap 16,384 | clause 17 |
| V10 | 11-12 | Determinism sweeps: workers, chunk order, shuffles, ISA legs, hosts | tests only | the proof | everything equal | any difference -> stop and report | no |
| V11 | 12-13 | RAM: no mirrors or owned columns in Unity; clip-bound modifiers without wheel entries; optional 16 B clip-bound record | `Board.Stats.cs`, `Board.Wheel.cs` | section 4.1 | `MemoryBytes` and private bytes at 1M and 4M | the 16 B record saves under 10 % of sv bytes -> skip it | state magic bump only if it lands |
| V12 | 13, only if a gate failed | Reserves, in this order: (a) run the fold in the kind job's chunk prologue through tl's optional partial hook; (b) digest every K ticks; (c) inline `Step` for intrinsics with no remote writer | glue; one line in tl.unity's emitter | (a) fold 1.6 -> about 0.5 ms at P1 [projected] | A/B, 5 processes per arm | (a) under 25 % drop of fold plus kind time -> revert | (c) changes semantics: owner decision and a new clause |
| V13 | 14 | Final gate, 5 runs, all shapes; report | - | - | the list below | - | - |

### "sv complete" (all must hold)

1. **Bit-exact.** NorthStar2 and the fold scenario give their pinned digests on .NET (default, no-AVX2, scalar, netstandard2.1 asset), on Mono and under Burst.
2. **Worker-invariant.** One digest and one event stream at 1, 2, 4, 7 and 11 workers, and under the chunk-order sweep.
3. **0 B.** .NET: 0 process-wide managed bytes over 2,000 warm frames, 0 native allocation calls. Unity: main thread 0, all Burst probes 1, `window - null window` = 0.
4. **Performance.** Unity, measured by the method of SV-Q8: sv <= 8.3 ms per tick at P1, P2 and P3 for W in {1, 2.5, 4} and cross-row <= 10 % [target]. The 60 Hz and 50 % cross-row cells are reported, not gated.
5. **RAM.** sv bytes per entity at 1M rows no higher than section 4.1's "after" column plus 10 %; setup no worse than tl's own plus 10 ms per 1M rows [target].
6. **Builds in Unity 7000.0.0a16 under C# 9**, from a fresh clone, with 0 `error CS`, 0 `warning CS` and 0 Burst errors.
7. **North-star green** against tl 4.0 on both hosts; the legacy suite still green against tl 1.3.0.
8. **No existing test loosened.** 446 tests unchanged and green; tl's 13 hashes untouched; the two existing sv pins unmoved.
9. **Contract v2.1.0 vendored**, with every amendment of section 5 approved by the owner.

### Kill order (summary)

1. Per-worker command streams for dense traffic: killed before any code.
2. A `Vector512` path: killed before any code.
3. Dense clear and whole-population fold: replaced in V2 and V6.
4. Drift, mirrors, reconcile in Unity: not built.
5. Inline `Step`: only as V12c, only by owner decision.

### Rules for agents

- One step per PR. A step that trips its kill rule is reverted, not tuned for more than one day.
- Never edit a test expectation, a pinned digest or an existing test body. New tests only. The frozen eager oracle changes only with an approved clause.
- Never build or benchmark while another agent holds `bench.lock`. Take `bench.lock`, then `unity.lock`.
- A number without its tag and run settings (shape, W, cross-row share, workers, ISA leg, safety checks, Burst probe) is rejected.
- Production code carries no comments. The FMA grep runs on source and on Burst assembly.
- Stop and report, do not decide: B3 false; a digest that differs between hosts or worker counts; Mono float bits that differ; any need to change tl beyond the two optional additions of SV-Q6; anything that needs V12c.

### Owner decisions this plan needs

1. Approve contract v2.1.0 and the line that makes the owner the approver while the bus is unreachable.
2. Accept sum-then-clamp as the per-tick semantic of a dense intrinsic (clause 19).
3. Accept that floats never cross rows (clause 13 e, f).
4. Confirm the gate: 25 % of a 30 Hz frame. Say whether 60 Hz must pass.
5. Digest every tick in release builds, or every K ticks.
6. Frame rollback at 1M rows: needed or not (SV-Q5).
7. Default event capacity per channel (65,536 proposed).
8. If V5's kill rule fires: allow a language-version override for the vendored stat machine, or wait.

### Not verified (V0 settles each)

- Every [projected] number in this file.
- tl.unity's generator facts: one component per state struct (B3); which pointer requests bump change versions; whether kinds store or accumulate on `Apply`; whether the baked library exposes kind-to-column metadata.
- Entities 6.7.0 names for the enabled-mask and lookup APIs (B10).
- How much of `src/Sv` uses C# 10-12 syntax.
- Mono's float behaviour for the stat fold, and Mono allocations on warm paths.
- The 30 GB/s streaming figure for sv's own bytes, and `Interlocked.Add` cost under Burst.
`````

---

## Appendix H. Unity CLI skill notes (Unity 7 facts and the rules agents follow)

### unity-cli-skill SKILL.md

Path: `unity-cli-skill/SKILL.md`  (4844 bytes)

`````
---
name: unity-dots
version: 4.0.0
description: |
  Unity DOTS/ECS work through the `unity-cli exec` / `unity-cli list` tool, with BovineLabs-style
  strict Data-Oriented Design and a 6-assembly layout. Inspect first, mutate second, verify before
  claiming success. For batch mode, test runs, disk hygiene and ECS/baking facts read
  skills/unity-cli-field-notes/SKILL.md first.
---

# unity-dots

## Rules that never bend

1. **Inspect before you mutate.** Before touching a scene object, report the active scene path, the root objects, and whether the target lives in the parent scene or in a referenced subscene asset. Reuse what exists; create only after absence is confirmed.
2. **Measure, don't assume.** Done = compile clean (`error CS` = 0) + the requested state read back directly (object at its path, component on it, entity query count > 0). A count of 0 is a failure, whatever the tests say.
3. **One fix per cycle.** One change, compile, verify. After two failures from quoting, escaping or context confusion, stop writing code and only inspect until you find the mistake.
4. **No guessed APIs.** Probe a type or namespace with a minimal compile-safe query. If a guess fails once, read the asmdef or the source.
5. **No helper scaffolding to dodge a failed query.** Simplify the query instead. Editor scripts, menu items and converters only when the person asked for permanent code. Remove anything temporary.
6. **Never delete scene or prefab content to silence an error.** Diagnose and report.
7. **Honest report**: inspected, changed, compile result, direct verification, what is still unverified.

## `unity-cli exec`

- Non-trivial C# (quotes, generics, lambdas, several statements) goes through stdin:
  `echo 'var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(); return s.path;' | unity-cli exec`
- After editing `.cs`: `unity-cli editor refresh --compile` before Play mode or the next `exec`.
- After text-editing `.prefab/.unity/.asset/.mat`: `unity-cli reserialize <path>` at once.
- `exec` caches compiled payloads: keep each short, one concern, and check the output is yours.
- `unity-cli console` can return `[]` while `Logs/Editor.log` holds the errors. Read the log.

## SubScenes

- The parent-scene GameObject with `Unity.Scenes.SubScene` and the referenced `.unity` asset are two things. Verify the parent scene from the parent scene.
- Don't open the subscene asset with `OpenSceneMode.Single` unless editing it is the task. If it is: note the previous scene, edit, save, restore, re-verify from the parent.
- The type is `Unity.Scenes.SubScene` (not `Unity.Entities.SubScene`). Reuse the existing container; never add a second one.
- Manually created entities do not prove authoring + baker + subscene works. Only a query on the loaded world does.

## Traps that cost hours

- **Stale bakes**: force reimport (`ImportAsset(p, ForceUpdate | ForceSynchronousImport)`) touched assets, then confirm one baked component exists in the live world before trusting a play test.
- **Stale domain**: one compile error anywhere keeps old assemblies running. Prove new types with `Type.GetType`.
- **Ask the Editor, not the YAML**: inherited prefab components and variants are invisible in files. Cross-prefab references serialize to `{fileID: 0}` silently.
- **`SaveAsPrefabAsset`** can write a stale in-memory copy. Force-reimport first; afterwards diff the file and read the deletions.
- **Prefab overrides address base arrays by index**: deleting rows at the front of a base array rebinds every override after it. Array-size overrides hide appended rows.
- **Filter worlds by name** (`ServerWorld`, `ClientWorld`): flags also match the Editor's conversion worlds, which hold stale data.
- `EditorApplication.delayCall` never fires headless; registrars that rely on it need a direct call.
- Entities baked with `TransformUsageFlags.None` have no parent chain at runtime; resolve owners in the Baker from the authoring hierarchy.

## Code standards

- `OnUpdate` stays trivial; logic lives in stateless pure functions with tests.
- Zero allocation in hot paths, Burst and jobs, native collections only.
- Components are normalized pure data; utilities are stateless extensions; engine glue is never mixed with logic in one file.
- No comments: name and extract instead. Small single-purpose files. Constants for magic numbers.
- UI Toolkit for UI. No drive-by refactors.

## Six assemblies per feature

`Scripts.Data` (unmanaged components, no logic), `Scripts` (systems, pure functions), `Scripts.Authoring` (MonoBehaviours and Bakers, runtime assembly referencing `Unity.Entities.Hybrid`), `Scripts.Debug` (gizmos, dev diagnostics), `Scripts.Editor` (editor-only tools), `Scripts.Tests` (tests first). Templates: `ScriptStructure.md`. Change asmdef references one at a time and compile after each.
`````

### unity-cli-skill field notes

Path: `unity-cli-skill/skills/unity-cli-field-notes/SKILL.md`  (44335 bytes)

`````
---
name: unity-cli-field-notes
description: Field-tested rules for AI agents working on Unity projects from a terminal - pick batch mode or the live Editor, run tests and builds headless with one script, reuse one scratch project, clean up disk, avoid false greens, and the ECS/baking facts agents get wrong. Use before compiling, testing, building or probing any Unity project, with the official unity-cli skill for live-Editor commands. Verified on the newest Unity 7 editor (Linux and Windows 11), Entities 6.7.0 and 7.0.0, Unity CLI 1.0.0-beta.13 to 15.
---

# Unity field notes for agents

Read once, top to bottom. Every line cost someone hours.

## Owner rules (every agent, every brief starts with rule 1)

1. **Before any work** pull this repo (`git -C <clone of IAFahim/unity-cli-skill> pull --ff-only`, clone it if missing; see `references/new-pc-setup.md`) and read this file. **Before you finish**, push what you learned back to `main` here (one verified line in the right section; `CONTRIBUTING.md`).
2. **Newest Unity 7 editor only.** Install a newer one by name with the CLI (`unity install 7 --yes --accept-eula`); never test, pin or mention older versions. Scripts pick the newest installed editor.
3. **Finish and test locally first.** CI is not a priority; do not work on CI or runners unless asked.
4. **Direct pushes to `main` are authorized** in IAFahim repos (`git pull --rebase origin main`, `git push origin HEAD:main`, never `--force`).
5. **Manage, delegate, right-size**: strongest model for performance and diagnosis, a small one for mechanical work. One brief per agent with file ownership, own worktree. Detail: `references/workflow.md`.
6. **One Unity process at a time**: take the lock directory (`mkdir _work/unity.lock` fails if held), remove it when Unity exits.
7. **Short reports** (commands, counts, shas, what is unverified). **No code comments** in these repos.
8. **Never hand-write or hand-edit Unity-serialized files** (`.asset`, `.unity`, `.prefab`, `.mat`, `.meta`, ProjectSettings YAML): it silently corrupts references (`fileID: 0`) and causes slow reimport loops. Create them through Unity: an editor script in the scratch project's `Assets/Editor` (never the package) run with `-batchmode -nographics -quit -executeMethod`, or a headless Editor driven by `unity command eval`. Verified recipes: `references/headless-assets.md`.
9. **Unity ECS: `ISystem`, `IJobEntity`/`IJobChunk`, Burst, unmanaged data only.** Never `SystemBase`, managed components, GameObject lookups or other managed or hard-coded workarounds. The whole Tl hot path and every lib we use is unmanaged; managed code only in editor tooling, authoring and bakers. Needing a workaround means Tl or the lib lacks something: stop, `gh issue create` in that repo (minimal repro, missing API), tell the owner. Detail: `references/unity-ecs-rules.md`.
10. **Designers wire references in the Inspector** (drag and drop on authoring components and ScriptableObjects). Never by name or path in runtime or authoring flow: no `AssetDatabase.Load*`, `Resources.Load`, Find-by-name. Only tests and editor-only tooling may.
11. **Any Unity task**: make sure the project's own `AGENTS.md`/`CLAUDE.md` has a "Start here" pointing to this skill plus these hard rules; add it if missing, commit, push.
12. Stuck after two honest attempts: `scripts/ask.sh "<question>" body.md` files a `question` issue (versions, repro, exact error, what you tried). The repo is public: no secrets or personal paths.
13. **Cross-machine work goes through GitHub issues, never the owner as relay.** Before starting, check open issues labelled with your OS (`gh issue list --repo IAFahim/<repo> --label <windows|linux>`). On a cross-machine problem open or comment on one with repro, expected vs actual, logs and the command that proves it; claim by commenting; post root cause and commit shas when fixed; the other machine verifies and comments the result. Example: IAFahim/tl#576 (Linux demo step 10).
14. **Code you show a person must read top to bottom without comments.** Assume they read only the code. Order it the way it runs (input, first step, next step, output), so no name points at something defined further down. Name every type, method and parameter by its role (`totalPushThisTick`, not `total`; `WindClipToPushThisTick`, not `Evaluate`). Give one complete file that compiles, never fragments, and run it before you show it: paste the real output, not what you expect. Where the library hides an order or a rule (who runs first, what must be on a different track), say it in the code or the names, then verify it. A vocabulary change (a rename) is done only when every place that uses the word changes together (attribute, JSON keys, docs, tests); decide the full list first, or do not start.
15. **Decide, do not ask.** The owner is not a relay or a tie-breaker for things a developer would simply do. A decision that is reversible, in scope and keeps public API and pinned hashes unchanged is yours: pick the safer option, write one line on the issue, continue. Ask only for tags, releases, visibility, deleting data you did not create, force pushes, public API or pinned-hash changes, budget waivers and licence. Act and report; do not leave easy work for the owner.
16. **A new implementation becomes viable through small developer fixes, then a gate.** Profile first (bounds checks, per-tick copies and allocations, rebuilding per tick what changes per membership change, indirect loads, managed arrays on a runtime path, JIT tier-up, quadratic setup). A rewrite replaces the old code only when, in the same harness and in Unity with Burst, it is at least 2x faster at the owner's scale, setup and memory are no worse, warm allocation is 0 B, hashes are bit-exact, no test was loosened, the source budget does not grow, a fresh clone compiles, and a second agent reran the numbers. Failed goals are never ticked as done. Detail and the 5.0 evidence: `references/new-implementation-viability.md`.
17. **You own issue lifecycle; the owner never babysits it. Talk before you file.** Issues come from agent work: a finding, a failed check, a design problem or a proof gap an agent hit (sv: `issues/NNNN-*.md` with a `Status: open|closed` line; other repos: `gh issue`). Never file an issue from a chat answer or a guess, and never because the owner described what they want: talk with them until the problem is understood, say what is or is not possible, and file only once it is clear. The owner does not read issues; the conversation comes first. When the work is done and a second run or agent has verified it, close it yourself: `Status: closed`, commit sha, receipt (test counts, digest, numbers with tag and settings). Reopen it yourself when a later change, a failing run or a new finding touches it. Before ending a task list what you closed, opened and reopened in one line; never ask the owner to open, close or check an issue.18. **Know the thing before you touch it or manage others on it.** Before editing a library, or briefing an agent on it, you must be able to say in your own words what it is for, what it stores and where, how data moves through it, and what the owner means to build with it. Get that from the project's `AGENTS.md`, README, design and contract docs, the code, and the owner. Never from file names, a third party's plan alone, or a guess. A green test suite and a plan's checklist do not prove it is the thing the owner wants: check the work against the owner's intent. If you cannot state the purpose, or `AGENTS.md` does not say what the project is building and what it is, stop, say so, and get the owner's statement first. Every project's `AGENTS.md` opens with what it is, what it builds, the shape of its data and who uses it how. Agent briefs carry that understanding; an agent never starts from the task text alone.

## 1. First minute: look before you launch

```bash
pgrep -af '[U]nity/Hub/Editor|Editor/Unity' | cut -c1-200   # who is running, on which -projectPath
ls <project>/Temp/UnityLockfile 2>/dev/null                 # exists = that project is open
```

- **One Editor per project path, ever.** A second launch on the same path aborts ("another Unity instance is running"). Never kill an Editor you did not start; it is the person's.
- **Never open a windowed Editor yourself.** You cannot see it and it waits for focus. Run headless: batch mode (section 2), or a resident `-batchmode` Editor you drive with the CLI (section 7).
- Drive an Editor the person already has open (`unity command ...`) only when the task is about that Editor: its scene, its Inspector, Play mode they watch.

## 2. Batch mode: compile, test, build, run code

`scripts/unity-batch.sh <project> <op>...` wraps this and prints one line per op: `exit errorCS passed/failed`.

```bash
U=<newest ~/Unity/Hub/Editor/*/Editor/Unity>
$U -batchmode -nographics -projectPath /abs/p -runTests -testPlatform EditMode -testResults /abs/r.xml -logFile /abs/r.log   # NO -quit with -runTests
$U -batchmode -nographics -quit -projectPath /abs/p -executeMethod Ns.Class.Method -logFile /abs/m.log                     # -quit for executeMethod
$U -batchmode -nographics -quit -projectPath /abs/p -logFile /abs/c.log                                                  # compile only
```

- Exit codes: 0 ok, 2 tests failed, 1 error (compile error, exception, license). Read them directly; **`| tail` eats the exit code.**
- **Always absolute paths** for `-testResults` and `-logFile`. A relative `-testResults` is resolved against the project, writes elsewhere, and you read a stale XML.
- Proof = `grep -c 'error CS' log` is 0 **and** the XML `<test-run total passed failed>` counts are what you expect. "exit 0" alone is not proof.
- First import of a fresh project takes minutes; later runs take ~1 min. Run long ops in the background and do other work; don't poll in a loop.
- PlayMode tests run in the same batch Editor. Frames are ~1 ms there: **wait on wall-clock time** (`Time.realtimeSinceStartup`), never on a frame count, for subscene streaming or FixedStep ticks.
- CoreCLR player (Unity 7): `PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.CoreCLR)` (enum values on 7000.0.0a16: `Mono2x, IL2CPP, WinRTDotNET, CoreCLR`); the build folder has `Managed/` and `CoreCLR/`, an IL2CPP one has `GameAssembly.dll`. Both Windows variations ship with the editor. SubScenes bake at build time with no extra step (Force demo verified on both backends).
- IL2CPP player: `BuildPipeline.BuildPlayer` from an `-executeMethod`, then run `./Game.x86_64 -batchmode -nographics -logFile /abs/p.log` and grep your own result line; call `Application.Quit(code)` in the player.
- CLI form: `unity test <project> --editor-path <Editor/Unity> --mode EditMode --filter Class.Method --output /abs/r.xml -- -logFile /abs/l.log` (exit codes in `references/windows-and-ci.md`).

Windows, CI and runner notes: `references/windows-and-ci.md`. How releases publish: `references/release.md`.

## 3. One scratch project, reused, cleaned

- Make **one** test project per Unity version, and reuse it. Point it at the package you are changing with `"com.x": "file:/abs/path/to/checkout/com.x"` plus `"testables": ["com.x"]` in `Packages/manifest.json`. **Never copy the package** into the project: copies drift and you test old code.
- Switch branches by repointing the `file:` path; the warm `Library/` makes the next run fast.
- Scratch helper scripts go in the scratch project's `Assets/`, never in the package. Delete them when done.
- **Test the package the way a user installs it**: a fresh project whose only dependency is the package. If it compiles only because something sits in your scratch `Assets/`, the package is broken. Ship what it needs, for example a vendored assembly with an asmdef and a sync script that has a `--check` mode.
- A vendored dll's `--check` must not compare rebuilt bytes: the same sources give different bytes per SDK, OS and checkout line endings (`core.autocrlf`). Commit a stamp of the pinned submodule commit, a CR-stripped `git hash-object --stdin` hash of the sources and the dll hashes, and compare that. Merging two branches that both rebuilt such dlls (cherry-pick conflict on `*.dll` + stamp): `git checkout --ours` the dlls and stamp, rerun the build script (it rewrites the stamp from the merged sources), then `--check`; regenerate auto-merged generated snapshots with the repo's tool and compare with `diff --strip-trailing-cr` (`cmp` differs under `core.autocrlf`). Never hand-merge either (tl.unity perf/int-unity, S4+S5).
- A new git worktree has **empty submodules**: `git submodule update --init --reference <local clone> <path>`; the reference must be the main clone, not a linked worktree ("reference repository ... as a linked checkout is not supported yet"). If the pinned commit exists only locally (unpushed), use `git -c protocol.file.allow=always -c submodule.<path>.url=<local clone> submodule update --init <path>` (without `protocol.file.allow`: `transport 'file' not allowed`). `git submodule status` then still prints `-` (the url lives only in `-c`, not in `.git/config`) although the pinned commit is checked out; check with `git -C <path> rev-parse HEAD`.
- **Clean up before you finish**: delete `Builds/`, scratch projects you created, stale copies, and IL2CPP output (`Library/Bee`, `Library/Il2cppBuildCache`). Check `df -h /tmp ~` first and last. A test project is ~3 GB; an IL2CPP build adds 1-2 GB.

## 4. False greens (each one has fooled an agent)

- **Stale domain**: one compile error anywhere keeps the old assemblies loaded, so old code runs and old test counts appear. Check `error CS` = 0 before trusting anything.
- **Console is not the log**: `console get` can be empty while `Editor.log` (or your `-logFile`) has the errors. Read the log.
- **Stale bakes**: subscenes serve old baked data. After changing a baker or authoring asset, force reimport (`AssetDatabase.ImportAsset(p, ForceUpdate | ForceSynchronousImport)`) and check one baked component exists in the live world.
- **Hand-built worlds prove little**: a test that builds a `World` and calls `system.Update()` by hand skips auto-created systems, group order and singletons. Prove the default world at least once (a PlayMode test that loads a scene with a SubScene).
- **Ask the Editor, not the YAML**: inherited prefab components and overrides are invisible in files. Cross-prefab references serialize to `{fileID: 0}` without a warning.
- Never delete scene or prefab content to silence an error.
- **A kind's `Idle` method** runs in Unity since tl.unity 939a8fb (generator pin tl 434d729, IAFahim/tl#590); before that it was silently ignored (no error, no warning) and a "can double jump" flag stayed true after its clip while the .NET host reset it. With an older tl.unity use the portable form: a clip with the "off" value from the end of the first clip to the end of the timeline (nothing writes the column after the last clip). Verified both ways in Unity 7000.0.0a16 PlayMode.
- **A README example that was only read is unverified.** The Hello Tl sideways-move recipe did not compile in Unity as first written (the sample's `foreach` has no braces, so the added line left scope) and the scene's `y` is 0.5, not 0. Run every documented example on every host and paste the real output.
- Restoring a file with `mv file.bak file` (or `cp -p`) keeps the backup's older mtime, so the next incremental `dotnet build` skips it and the tests run the stale dll; `touch` the file after restoring it. Take the Unity lock with `mkdir lock && <unity ...>`, never `mkdir lock; <unity ...>` (the `;` launches Unity even when the lock is held).
- Two tl.unity samples in one project interfere: with the EcsDemo (old `TimelineLibraryBootstrap` path) and the Force demo (zero-config path) both imported, the Force PlayMode test ends at X 4.9889 instead of 4.0333 and a combined run logs `this world already has a timeline library`; moving `Assets/EcsDemo` away makes it 4.0333 again. Test one sample per project.
- A Universal 3D (URP) project without the Editor UI: `<Editor>/Unity -batchmode -nographics -quit -createProject <dir> -cloneFromTemplate <Editor>/Data/Resources/PackageManager/ProjectTemplates/com.unity.template.urp-blank-*.tgz` (no CLI needed), or `unity projects create <name> --path <dir> --editor-version <v> --template com.unity.template.urp-blank --no-cloud` (`--template 3d` is "Unknown template" on 7000.0.0a16). `-createProject` alone makes a Built-in project, where Entities Graphics draws nothing from a SubScene. Import a package sample headless from an `-executeMethod`: `UnityEditor.PackageManager.UI.Sample.FindByPackage(id, null).First(s => s.displayName == ...).Import(Sample.ImportOptions.OverridePreviousImports)`, then compile again.
- A project whose `Packages/manifest.json` lists a package under `testables` also compiles and runs that package's own test assemblies in every `-runTests`; their generated types can collide with your scene's (e.g. a kind registered twice throws in a system, the run prints no result, exit 2 with 0 `error CS`). Read the XML `<test-suite type="Assembly">` names, and remove the package from `testables` for scene/demo runs.
- Linux shell quirk: `dotnet --list-sdks` can print nothing when `DOTNET_ROOT` points at an empty dir (e.g. a mise `dotnet-root`) while the SDK sits in `~/.dotnet`; export `DOTNET_ROOT=$HOME/.dotnet` and prepend it to `PATH`.
- Unity 7: the editor runs CoreCLR (NUnit XML `clr-version` 10.0.x) and `GC.GetAllocatedBytesForCurrentThread()` is exact, so `Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread()-before)` reads 48 B even for allocation-free code (it boxes its arguments before the second read). Compute the delta into a local first, then assert: that reads 0 B.
- Unity test code that never ran fails three ways the C# compiler or an unrelated green suite will not warn you about: writing an element of a `using var` `NativeArray` is CS1654 (build it from a managed array instead); an NUnit `[TestCase(1, 5)]` on `ushort` parameters fails at run time with `Object of type 'System.Int32' cannot be converted to type 'System.UInt16'` (take `int` and cast); a test that hand-runs an `IJobChunk` with `.Run(query)` after updating systems throws "the previously scheduled job ... writes to ..." unless it calls `EntityManager.CompleteAllTrackedJobs()` first (verified 7000.0.0a16). A gate script must remove the lock directory whatever the body returns (`mkdir lock || exit; body; rmdir lock`), not with `set -e` before `rmdir`.
- `[Licensing::Client] Error: HandshakeResponse` and `Failed to handshake to channel` appear in the Editor log of passing batch runs too (it then connects to its own licensing client). Judge by the NUnit XML counts and the exit code, not by those lines.
- Count Burst errors with `grep -c 'error BC'`, never a case-insensitive `BC[0-9]{4}` or `burst.*error`: cache hashes and GUIDs in a clean compile log match those (196 false hits on a 0-error fresh project, 7000.0.0a16). A second-agent rerun from a fresh clone + fresh `-createProject` reproduced sv adapter v3 pins and the P1 bench within 4 % (sv 2.48-2.62 ms vs 2.53), so builder receipts made this way are trustworthy.

## 5. ECS and baking facts agents get wrong

- Every `SystemBase`/`ISystem` in every loaded assembly is **auto-created in the default world** unless `[DisableAutoCreation]`. A system that calls `GetSingleton<T>()` must `RequireForUpdate<T>()` in `OnCreate`, or it throws every frame once its query matches.
- Broad queries catch your entities too. To exclude an **enableable** component use `Absent` (`WithAbsent<T>()`); `None` also matches entities where it is present but disabled.
- Order your systems explicitly (`[UpdateInGroup]`, `[UpdateAfter]`); group membership is not order.
- Authoring `MonoBehaviour`s must live in a **runtime** assembly (Editor-assembly scripts cannot be added to GameObjects). `Baker<T>` needs a reference to `Unity.Entities.Hybrid`. Bakers may live in Editor assemblies.
- Unity's standard `.gitignore` line `**/Library/` also ignores a package folder named `Library` (for example `Runtime/Library/`); add `!/<path>/Library/` or new files never reach git.
- Folders ending in `~` (`Tools~`, `Samples~`, `Documentation~`) are **never compiled** by Unity.
- Baker has only the generic `SetComponentEnabled<T>`; for runtime `ComponentType`s set enable bits in a `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system, which also sees every baker's output (good for validation).
- `[BakingType]` components are stripped from the game world; runtime subscene entities have **no names** (`GetName` returns ""). Find entities by components, not names.
- Subscene loading is asynchronous. Baking in an EditMode test without scenes: reflect `Unity.Entities.BakingUtility.BakeGameObjects(world, roots, settings)` (internal; `BakingSettings(flags, blobAssetStore)`).
- On 7000.0.0a16 `Unity.Mathematics` (`float3` and the rest) ships in the engine (`Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll`), not as a package: code meant to also build in plain .NET cannot use it; give such libraries their own unmanaged math types.
- On Unity 7, core Entities types (`EntityArchetype`, `EntityQuery`) live in the engine. Their source is not in the package cache, so check the API by compiling.
- Inside a namespace like `Tl.Unity`, the name `Unity.X` binds to `Tl.Unity.X`. Write `global::Unity.Entities...` (source generators hit this too).
- Unity 7 analyzers want cleanup semantics on statics (UAL0010/UAL0013). With domain reload off, statics survive between Play sessions: reset or dispose them in `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, mark the type `[NoAutoStaticsCleanup]` under `#if UNITY_7000_0_OR_NEWER`, and silence the analyzers only for vendored pure-.NET code via its `csc.rsp` (`-nowarn:`).

- Entities 7.0.0 API gaps: `EntityManager` has no `GetComponentLookup`/`GetBufferLookup` (take them from `ref World.Unmanaged.ResolveSystemStateRef(handle)`); `ArchetypeChunk` has `GetEnableableBits` but no public setter (copy a mask per entity through `EnabledMask[i]`); an `IJobParallelForDefer` job struct must itself hold the deferred `NativeList` (as `AsDeferredJobArray()`) or it throws at schedule time. `EntityManager.GetComponentDataRawRO`/`SetComponentDataRaw` are not public (CS1061): editor tooling that only has a `Type` calls a small generic `GetComponentData<T>`/`SetComponentData<T>` helper through `MakeGenericMethod`.

- Source-generated `ScriptableObject`/`TrackAsset`/`PlayableAsset` types (7000.0.0a16) show in `TypeCache` and menus but save with `m_Script: {fileID: 0}` and load as `null` after restart. Keep the Unity object class in a real file and put generated data in a plain `[Serializable]` class behind `[SerializeReference]` (survives restart). A generated never-saved `TrackAsset` stub plus a `TrackEditor.OnCreate` that swaps it for the real track puts a per-type entry in the Timeline `+` menu. The stub must be a real `.cs` file named after its class (Timeline logs "appears to be invalid" and "No script asset" otherwise; a hint-name file per class from the generator does not help): details in `references/ecs-baking-gotchas.md`.
- An analyzer dll (label `RoslynAnalyzer`, no platforms) inside a runtime asmdef's folder runs on that assembly, every assembly referencing it and `Assembly-CSharp`; no marker asmdef needed. `csc.rsp` next to an asmdef can raise `-langversion` (Unity passes 9.0 first; records need an `IsExternalInit` polyfill). Editor code compiles against `unity-4.8-api` + `BCLExtensions/netstandard2.1` (has `System.Text.Json`, no `System.Runtime.Intrinsics`, no `System.Threading.Lock`); check vendored .NET code offline with Unity's `DotNetSdk` `csc` and the references from a `Library/Bee/artifacts/*/<asm>.rsp`. A missing namespace hides method-body errors, so recount after fixing it (Tl.Gen.Tlb: 13 became 24: `Utf8.IsValid`, `Rune`, `Stopwatch.GetElapsedTime`, `SHA256.HashData`, `Convert.ToHexString`, `CreateDelegate<T>`, `BitOperations`, `OperatingSystem`). 7000.0.0a16 does not define `NET`: guard .NET-only code with `#if NET`.
- Prove a baker's `DependsOn` headless: reflect `Unity.Scenes.EntityScenesPaths.GetSubSceneArtifactHash(sceneGuid, default, true, ImportMode.Synchronous)` before and after editing the dependency.

- Your own Roslyn generator can add `IComponentData` to a user's `partial struct` (plus a `TypeManagerV2` registration called from an ILPP): an `IJobChunk` with `ComponentTypeHandle<T>` and `SystemAPI.Query<RefRW<T>>` work in the same assembly, and `IJobEntity` works in a referencing assembly, all in Burst. An `IJobEntity` over that type in the same assembly fails `SGJE0010 ... is not supported` (Unity's generators never see another generator's output) (verified 7000.0.0a16, IAFahim/tl.unity#22).
- A UPM package folder can also be a .NET project: put `BaseIntermediateOutputPath` `obj~\` and `BaseOutputPath` `bin~\` in a `Directory.Build.props` (above the package folder) so Unity never imports build output; Unity ignores the `.csproj` for compiling but imports it as an asset (commit its `.meta`), and it does not read `<Nullable>`: add `-nullable:enable` to a `csc.rsp` next to the asmdef or every `?` warns CS8632.
- A plain-field `float3` (own struct, per-component operators) is bit-identical to `Unity.Mathematics.float3` and as fast in Burst over 100k entities (3.9 vs 3.8 ns/entity single-thread, 0.88 vs 0.85 parallel). One run right after a recompile was 4x slower for one job in every round and did not repeat: rerun before trusting a Burst micro-bench.

More baking, editor and benchmark gotchas, including raw component bytes that work in IL2CPP, scene players and editor hooks that outlive a reload: `references/ecs-baking-gotchas.md`.
Burst/ECS performance recipes (safety-off benchmarking, Burst-fallback probe, memory-floor touch job, archetype bytes) with measured wins and dead ends: `references/burst-performance.md`.

- Live baking is testable headless: an EditMode `[UnityTest]` that calls `SubSceneUtility.EditScene(subScene)` then `yield return new EnterPlayMode()` gets a running default world; editing an asset through `SerializedObject` patches the runtime entity within 10-40 ms. The patch rewrites only components and buffers whose baked value changed (a rebaked buffer arrives whole, with your runtime-written fields reset); enable bits and components that bake the same keep their runtime values (verified 7000.0.0a16, IAFahim/tl.unity#22).
- `DefaultWorldInitialization.GetAllSystemTypeIndices(flags, bool)`: the bool is `requireExecuteAlways`; pass `true` and you get only a handful of systems. Use `type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true)` to walk group ancestry; `AddSystemsToRootLevelSystemGroups` needs the parent groups (`SimulationSystemGroup`, `FixedStepSimulationSystemGroup`) in the list or it logs "group was not created".
- Timeline Edit-mode preview from a test: `director.time = t; director.Evaluate();` runs every track mixer's `ProcessFrame` in batch Edit mode. Write scene values only inside `AnimationMode.StartAnimationMode()`, register each property first with `AnimationMode.AddPropertyModification(binding, modification, true)` (what `TrackAsset.GatherProperties` + `driver.AddFromName` do in the window); `StopAnimationMode()` restores them and the scene stays clean.
- `Camera.Render()` in Edit mode does not draw the GameObjects of an open SubScene (Entities gives that scene a Scene-view-only culling mask; `Camera.overrideSceneMask` does not exist on 7000.0.0a16). For a picture, set `EditorSceneManager.SetSceneCullingMask(subScene, EditorSceneManager.DefaultSceneCullingMask)` around the render and put the old mask back.
- Tl (com.tl.unity, read from source on main 9328184): `TlClipData.TimelineOf(asset)` only works in the Editor (its resolver is set by editor code), so a runtime UI cannot turn a `TimelineAsset` into a `TlTimeline`; bake the refs and names into a SubScene buffer and let the UI send indices. Rewind (`TimelineStep.Forward`) and speed (time scale) apply to the whole world, there is no per-playback request for them, and a held (`Ended`) or paused playback does not move in either direction.
- A `MonoBehaviour` whose class name differs from its file name can still be added by a builder (`AddComponent<T>()`), but the saved scene points at no script: SubScene baking in the import worker logs `[Worker0] The referenced script is missing on <GameObject>` (a warning) and its baker never runs. One authoring class per file, named after it.
- A `MonoBehaviour` reading ECS data through `EntityQuery.GetSingleton<T>()` does not complete running jobs (`EntityManager.GetComponentData` does): call `query.CompleteDependency()` first, or PlayMode tests fail with "must call JobHandle.Complete() on LocalToWorldSystem...". A `RefRW` from `SystemAPI.GetSingletonRW` is invalid after `EntityManager.Instantiate`/`DestroyEntity` in the same `OnUpdate`: copy the value, change it, `SystemAPI.SetSingleton` after. `IJobEntity.ScheduleParallel(query)` throws unless the query has every component `Execute` takes (verified 7000.0.0a16).
- In an editor builder, a `TimelineAsset` made with `AssetDatabase.CreateAsset` earlier in the same method can read as `null` after `EditorSceneManager.NewScene(..., NewSceneMode.Single)` when a prefab was saved in between; assigning it then saves `m_PlayableAsset: {fileID: 0}` with no warning and the director bakes nothing. Reload every asset by path after `NewScene`, before wiring it into the scene (7000.0.0a16).
- See what a Roslyn source generator emitted in a Unity assembly: copy `Library/Bee/artifacts/<dag>/<Asm>.rsp`, drop its `-out:`/`-refout:`, add `-out:<tmp>/x.dll` and `-generatedfilesout:<tmp>`, and run `<Editor>/Data/DotNetSdk/dotnet.exe <Editor>/Data/DotNetSdk/sdk/<v>/Roslyn/bincore/csc.dll @x.rsp` from the project folder.
- Iterate on a package's `Samples~/<Name>` without copies: a directory junction (`mklink /J <scratch>/Assets/TlSamples/<Name> <checkout>/com.x/Samples~/<Name>`) compiles it like an imported sample; Unity writes `.meta` files and builder output (scenes) back into the checkout, so ignore the output folders in git.
- Tl zero-config (verified tl 5bb60ca6, tl.unity 968ce7c): `Events<T>` raised in `OnClipStart`/`OnClipEnd` of an `ITlTrack` land in a `TimelineEvent<T>` buffer Tl adds to the entity (read it after Tl in `TlSimulationGroup`); before tl 123f56b9 they never fired because the generator spelled generic track keys `TlNoSettings<..>` instead of the baked `TlNoSettings`1<..>`. Each auto kind now adds only its own Apply/feeder columns, after all bakers, keeping a value a game baker already set; an entity fed only by game pulses must add the kind's column itself (plus `<Kind>Kind.Types`).
- Tl (verified tl f731c1de, tl.unity 1a375ff): pair and marker keys hash the full type name (namespace, name, `+` for nested) without the assembly name, so `tlb library --assembly` against any dll with the same types gives the same bytes; two types with one full name in a bake or kind catalog fail with both assembly names. One `Tl.LibraryAssembler` (Tl.Core) writes every `game.tlib`, from per-timeline `LibraryPart`s and `Tl.Gen.Tlb.LibraryBake.KindPayload`. `Tl.Gen.Unity` skips any assembly that already contains a `TlUnity.*.g.cs` file (checked-in snapshots such as `Tests/Kinds/Generated`); regenerate those with `Tools~/Tl.Gen.Unity.Snapshot` (`dotnet <dll> Core Tests/Kinds <out> Tl.Unity.Kinds`; `KindTypes.g.cs` is the committed `Kinds.g.cs`).
- Tl byte fixtures (verified tl 0b35052e, tl.unity feat/22-portable-json): a TLB or `game.tlib` byte change is caught by state hashes only if logic moved; the bytes live in many pinned copies that must all be re-pinned in the same change: tl `tools/Tl.Bake.Tests/golden/**` (fixtures, `kinds/game.tlib`, `introspection.json`), `tests/Tl.Systems.Tests/{KindLibrary,GroupLibrary,TwoAssembly}` (`TL_UPDATE_SNAPSHOT=1` rewrites `push.tlib` and the kind snapshot, `TL_UPDATE_BENCH=1` the bench one), `tests/Tl.Packs.Tests/<Pack>/*.tlib` (`TL_UPDATE_SNAPSHOT=1`) plus the base64 in `packs/<Pack>/Tests/<Pack>LibraryBytes.cs`, `tests/tlb_cli/config.json` hashes; tl.unity `Tests/TwoAssembly/Tests/PushLibraryBytes.cs` (`eng/check-two-assembly.sh`), `Tests/Kinds` via `TL_TLB=<Tl.Bake.dll> eng/pack-kinds-fixture.sh <probe>/Library/ScriptAssemblies` (its `game.tlib` is also tl's `KindLibrary/unity/game.tlib`), `Tests/Generated/Direct/Game/DirectLibraryBytes.g.cs` and `Tests/Plain/Sample/game.bytes` (`tlb library` over the fixture JSON against `Tl.Unity.Direct.dll` / `Tl.Unity.Plain.dll` from a probe's `Library/ScriptAssemblies` plus `<Editor>/Data/Managed/UnityEngine/*.dll`; the plain one is the six Kinds timelines with namespace `PlainGame`). Confirm a reconstructed recipe by baking it with the previous main's `Tl.Bake.dll` and `cmp` against the committed bytes before re-pinning.
- Tl kinds are identified by full type name since tl#582: `KindStore.IndexOf`/`Pulses<T>("Game.Damage")`, `tl.project.json` `kinds` keys, `KindIndex.IdOf` and the Unity `KindRegistry` all take `Namespace.Name`; a pack's `Tl.Pack.Force.ForceKind` and the Force demo's global `ForceKind` now run in one project (`TL_UNITY_PACKS=1 eng/test-com-tl-unity.sh` runs the pack tests next to `Tests/ForceContract`).
- Run a package sample's own Editor/PlayMode tests (asmdef `defineConstraints: UNITY_INCLUDE_TESTS`) without `testables`: junction the sample into `Assets/`, drop `testables` from the manifest (the package's own test assemblies would register the same kind twice, `Kind 'ForceKind' is registered twice`) and set the define once with a scratch `-executeMethod` (`PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "UNITY_INCLUDE_TESTS")`); delete the junction with `rmdir` (never `rm -r`) before switching back to a `testables` gate run. A cold project's first EditMode run can fail a "ran as Burst code" assertion (Burst not compiled yet): rerun before debugging (verified 7000.0.0a16). Every manifest change (e.g. adding packs) makes the next run cold again, and the managed fallback also allocates (a 0 B warm-allocation assertion read 4.8 KB): run each manifest twice and judge the second run.
- Tl preview/sim loops that stop at `playback.Tick >= target` mis-handle the end of a timeline: Tl hands over to the `Next` timeline in the step where `Tick + 1 >= Duration`, so Tick never reaches Duration, the new timeline's tick 0 is already applied in that step, and the loop runs on into it (Force Push at 3.0 s previewed x 0.89 instead of 4.0333). Stop when the playback's `Timeline` changes and keep the previous step's state (IAFahim/tl.unity b841389).
- Engine-free rules shared with Burst (verified 7000.0.0a16): write them as static generic methods over `where T : struct, IMyStorage` in the plain .NET assembly and let each host pass its storage as a struct (Unity: a struct holding the `DynamicBuffer`'s `GetUnsafePtr()` and length, refreshed after `Add`). Burst and the JIT each build one direct copy; moving Tl's kind tick this way kept the K bench in band (A 7.4-8.4, B 1.9-2.2, B0 0.6-0.7 ns/entity). Pass the hot loop a raw element pointer and count rather than the slot interface. When another workstream moves a vendored library's generator sources ahead of the package's pin, any pin move breaks `build-generators.sh` and the old generator dll emits calls to removed API (`CS1061` in `Library/Bee/artifacts/*/Tl.Gen.Plain.*.g.cs`): land your pin after theirs.
- A package sample whose asmdef references one of the package's test assemblies breaks compile (CS0246) when a user imports it without `testables`: the missing reference is dropped, the sample still compiles. Gate such samples with an extra `defineConstraints` symbol that only test projects set.

- Unity 7 component/codegen facts (verified 7000.0.0a16): `builder = builder.WithAny(ref list)` is CS8347 (call `builder.WithAny(ref list);` as a statement); `URPMaterialPropertyBaseColor` exists only with URP installed (asmdef `versionDefines` on `com.unity.render-pipelines.universal`); a clip mirror field of a struct type is saved only if that struct is `[Serializable]`; any namespace under `Tl` (not just `Tl.Unity`) breaks Unity's `IJobEntity` codegen, so adapters use a top-level namespace; an optional package's adapter asmdef compiles only when the package exists via `versionDefines` + `defineConstraints`. `KindGame.KindBench.RunBatch` appends to its `-kindsBench` file: read the last three lines.

- Tl (verified tl.unity 61336db): clip mirrors and menu stubs carry the namespace (`Tl.Pack.Force.ForceClip` -> `Tl_Pack_Force_ForceClip_TlData` / `_TlMenu.cs`; global clips keep `ForceClip_TlData`), so a pack and a game clip with one short name compile together. `IMarker<T>` markers get `<T>_TlMarkerData` mirrors and `<T>_TlMarkerMenu` stubs; a `TlMarker` on the marker row or any track bakes as a Tl marker.
- Two assemblies whose generated code both declares `public static partial class` in one shared namespace (e.g. `Tl.Generated.TimelineJobs`) give `warning CS0436` in the one that references the other; make per-assembly generated helpers `internal` (verified 7000.0.0a16).
- The `unity.lock` directory can vanish while you work (another agent's cleanup removes it): check it still exists before a benchmark. A K bench that overlapped another Unity run read A 10.5 / B 2.4 ns; the same code with the lock held read 8.2 / 2.0, equal to the baseline.

- Git Bash on Windows has no `setsid`. Start a long gate with the harness `run_in_background` (or `nohup bash script &` and poll the log file); a chained `cmd &` subshell can be cut off when the tool call returns. After `git -C <submodule> checkout <sha>` of a commit pushed a moment ago, run `git -C <submodule> fetch` first and check `rev-parse HEAD`: the checkout fails quietly otherwise and the next Unity run tests the old pin (the test count in the XML is the tell).
- tl columns in Unity: an entity with no Playable Director gets no column component at all (a `RefRO<Column>` query never matches it); on the plain .NET host the column exists for every row. `Tl.Playback` is ambiguous in game code; write `Tl.Unity.Playback`.
- Plain .NET host, recycled player rows: `StopAll(row)` fires the interrupted End pulse of a mid-clip playback on the next `Tick` (documented, `Interrupted` flag), so a new player put into the same row in the same step receives the old player's come-back. Reset and `Start` one step later, or ignore `Interrupted` ends. Unity does not leak (a new entity is a new row). IAFahim/tl#594.

- Unity 7000.0.0a16 ships **no Mono player**: `Data/PlaybackEngines/windowsstandalonesupport/Variations` has only `*_coreclr` and `*_il2cpp`; `SetScriptingBackend(Standalone, Mono2x)` reads back `CoreCLR` and the player runs .NET 10 without a warning. Check the player run's NUnit `clr-version` before claiming a Mono result.
- Free per-entity resources on destroy (verified 7000.0.0a16): a second `ICleanupComponentData` next to your normal component, a query `WithAll<Cleanup>().WithAbsent<Normal>()` each frame (a destroyed entity keeps only cleanup components), release, then `RemoveComponent<Cleanup>(query)`. `Instantiate` does not copy cleanup components, so instances move archetype once when you add it; `EntityManager.AddComponent(NativeArray<Entity>, ComponentTypeSet)` inside an `ISystem.OnUpdate` is fine after `state.Dependency.Complete()`.
- FMA proof for Burst code without the Inspector: `objdump -d` (WinLibs mingw) over `Library/BurstCache/JIT/*.dll` and grep `vf(n)?m(add|sub)`; `strings` of the matching `.pdb` names the job. Keep a `FloatMode.Fast` probe job as the positive control (it shows `vfmadd132ss`); `FloatMode.Strict` jobs showed none (sv, 146 dlls).
- Tl generated kind jobs at 1,048,576 rows, W 2.5 `Feeds` per row into one kind: 8.3 ms per tick at 11 workers, 18.1 ms at 2 (Ryzen 5 8500G, Burst, safety off). The job takes write access to every kind column in every chunk each tick, so a downstream `DidChange` filter on those columns always passes (a zero-scan still reads the column).

## 6. Small tools that save hours

- Linux Unity CLI beta 15 can trigger recurring Polkit password dialogs through `systemd-creds encrypt/decrypt --name=unity-auth-broker-seal`; identify them in `journalctl -t polkitd`, then try `UNITY_NO_AUTH_BROKER=1 unity auth status --format json` (verified successful without credential authorization events), which bypasses the broker for the OS credential store.
- `unity-cli exec` (if used) caches compiled payloads: keep each payload short and check the output is yours.
- Read `Packages/manifest.json` and asmdef references before guessing a type's assembly. If a guessed API fails to compile once, open the source or compile a probe; don't guess twice.

## 7. Live Editor (official CLI), condensed

- Connect: `unity --no-banner status --json`, then `unity --no-banner pipeline editors`. No package: `unity pipeline install --project-path <p>`, then the **window must be focused** before it connects (Linux: minutes of silence otherwise).
- Call: `unity --no-banner command --caller plugin --skill <skill> <group> <verb> --flag v`; discover with `unity command` and `unity command <group>`. Use `--result-only` for JSON.
- Recompiles drop the connection mid-call (`Invalid response format`, `No Pipeline instance`). Verify the effect, wait with `scripts/wait-ready.sh` (exit 2 = compile failure), retry only if it did not apply. Add packages one at a time.
- `script eval --code '...; return x;'` with fully qualified names; `script eval_file --file f.cs` for longer probes.
- Headless Editor to drive (verified): `unity pipeline install`, launch the editor with `-batchmode -nographics` and no `-quit`, then `unity command eval ... --project-path <p>`; commands in `references/headless-assets.md`.
- Simulate editor clicks without a window: against a resident `-batchmode` Editor, `unity command eval --project-path <p> --result-only "$(cat probe.cs)"` runs multi-statement C#; `Undo.IncrementCurrentGroup()` around a change then `Undo.PerformUndo()/PerformRedo()` reproduces Ctrl+Z/Ctrl+Y, and Timeline's internal `TrackExtensions.Duplicate` (reflection, args `track,null,null,null`) is Ctrl+D. Ready check is `unity status --json` (`state: ready`); this CLI has no `unity list`.
- Drag the Timeline playhead without a mouse: `TimelineEditor.GetWindow()`, reflect its internal `state` (`WindowState`), set `previewMode = true`, set `editSequence.time = t`, then call `Evaluate()` and `EvaluateImmediate()`; read `TimelineEditor.inspectedDirector.transform`. On Wayland (Hyprland) `hyprctl dispatch focuswindow address:<addr>` focuses the Editor so the Pipeline server loads, and `grim` captures it; there is no click tool without `ydotool`.
- Unity CLI beta 15 prints `unity editors --installed --json` rows under `data.items` (older builds: `data` was the list); scripts must accept both. A self-hosted runner on a public repo runs fork PRs on your machine: guard the job with `github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository` and set `PUT repos/<o>/<r>/actions/permissions/fork-pr-contributor-approval -f approval_policy=all_external_contributors`.
- A Linux self-hosted runner installed as a systemd service starts with a mise-wrapped environment: `dotnet` then finds no SDK ("No .NET SDKs were found", exit 155 in `gate-v3.sh unity`) because `DOTNET_ROOT` is mise's empty `dotnet-root`. Put `DOTNET_ROOT=$HOME/.dotnet` in the runner's `.env` and `$HOME/.dotnet` first in its `.path`, then restart the service (both files are read only at start). The first EditMode run after a manifest change fails the known cold Burst checks (tl.unity 9f8dccb packs mode: 8 of 87, including a 0 B allocation test under managed fallback); the second run is 87/87 EditMode, 78/78 PlayMode (Unity 7000.0.0a16).
- Details and evidence: `references/corrections.md`.

## Done means

You ran it, you read the counts, the person's own walk works (open the scene, press Play, see it), nothing of yours is left in their scenes or disk, and you said plainly what you did not verify.
`````

### field notes reference: unity-ecs-rules.md

Path: `D:/GitHub/unity-cli-skill/skills/unity-cli-field-notes/references/unity-ecs-rules.md`  (2116 bytes)

`````
# Unity ECS rules (owner, binding)

1. Latest Entities on the newest Unity 7: `ISystem`, `IJobEntity`/`IJobChunk`, Burst, unmanaged data only. Never `SystemBase`, managed components, class-based systems or GameObject lookups in game or library code.
2. The whole hot path of Tl and of every lib we use is unmanaged: no allocation, no managed references, Burst-compilable. Managed code is allowed only in editor tooling, authoring and bakers.
3. If a task seems to need `SystemBase`, a managed type, a static or hard-coded id, or any hot-path workaround, Tl or the lib in use is missing something. Stop. Do not work around it. File an issue in that lib's repo with `gh issue create` (minimal repro, the API that is missing) and tell the owner.
4. Designers work in the Inspector. References and values are set by drag and drop on authoring components and ScriptableObjects, never looked up by name in code. `AssetDatabase.Load*`, `Resources.Load`, Find-by-name and string paths are forbidden in runtime and authoring flow; they are allowed only in tests and in editor-only tooling that is not designer workflow. A designer never edits code or types names to wire things.
5. Unity-serialized files (`.asset`, `.unity`, `.prefab`, `.mat`, `.meta`, ProjectSettings) are produced by Unity, never written by hand.
6. A project that does Unity work keeps a "Start here" in its `AGENTS.md` and `CLAUDE.md` pointing at this skill and repeating these rules.
- Burst in the Windows editor cannot call `Marshal.AllocHGlobal`/`NativeMemory` ("Unable to load plugin kernel32.dll", then a silent managed fallback; check with a `[BurstDiscard]` side effect). Burst matches `[BurstDiscard]` by name, so an engine-free lib can declare an internal `Unity.Burst.BurstDiscardAttribute` to keep an allocation path out of Burst.
- `state.RequireForUpdate(query)` on a query with an enableable tag still runs the system once every entity has the tag disabled (Entities 6.7.0, Unity 7: a register system ran every frame, 264 B/frame). Return early on `query.IsEmpty` in `OnUpdate`; that respects enable bits and keeps the warm frame at 0 B.
`````

### field notes reference: burst-performance.md

Path: `D:/GitHub/unity-cli-skill/skills/unity-cli-field-notes/references/burst-performance.md`  (14562 bytes)

`````
# Burst / ECS performance: recipes, wins, dead ends

Verified on Unity 7000.0.0a16, Entities 6.7.0, Burst 1.8.25, Ryzen 5 8500G (6C/12T, DDR5), 11 job workers. Numbers are medians of 5 fresh editor processes unless marked.

## Measuring honestly

- Editor batch runs compile Burst jobs with safety checks on by default. Set `BurstCompiler.Options.EnableBurstSafetyChecks = false`, `JobsUtility.JobDebuggerEnabled = false`, `NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled` at the top of the `-executeMethod`, before any job, and log them with every result. Tl 4M entities x4 kind fixture: 41.2 ms/tick [40.3-43.0] with them off (tl.unity 3b44b20) against about 52.9 ms with editor defaults on the same kind job (earlier harness): compare arms only under identical settings.
- Prove a generated job you cannot edit ran under Burst: call a `[BurstDiscard] static void Mark() { Hits = 1; }` (managed static) from a user method the job inlines (a feeder/Evaluate), and fail the run if `Hits != 0` after timing. Burst drops the call; a managed fallback sets it.
- Memory floor of a job: a Burst `IJobChunk` with read-only handles that sums exactly the bytes the job reads (enable bits, buffer headers and used elements, columns), timed the same way (schedule + complete, 32 steps). Tl kind job at 4M x4: touch 15.5 ms vs kind job 26.3 ms (59 %), so roughly 10 ms is compute above the floor; at 10k x150 it is 0.55 vs 2.15 ms (compute-bound).
- Bytes per entity: `archetype.ChunkCapacity` and `TypeManager.GetTypeInfo(type.TypeIndex).SizeInChunk` over `archetype.GetComponentTypes(Allocator.Temp)`; a buffer's SizeInChunk is header 16 B + inline capacity x element size. Tl scale archetype: 320 B/entity, 49 per chunk, of which `TimelineRequest` inline (8 x 16) is 128 B the kind job never reads.
- Before a Unity run, diff what a generator change emits: `dotnet Tl.Gen.Unity.Snapshot.dll <pkg>/Core <domain dir> <out> <AssemblyName>` emits the jobs for any domain folder in seconds (tl.unity `Tools~/Tl.Gen.Unity.Snapshot`).

- Cache per-entity derived state across ticks without user cooperation (Entities 6.7, tl.unity perf/s4-unity-horizon): in an `IJobChunk` read `chunk.DidChange(ref playbackRW, LastSystemVersion)` BEFORE the first RW accessor call (that call bumps the version) and clear the chunk's cache when true. The system's own writes from its previous update never trip it; a `EntityManager.GetBuffer<T>(e)` RW access from outside between updates does, even without a value change (PlayMode test, 7000.0.0a16). Do the clear before any idle early-out, or a chunk skipped while idle keeps a stale cache. Keep the cache column optional (`chunk.Has`) so existing archetypes stay on the reference path.

- Change versions (Entities 7.0.0 built-in, probed with a filtered `IJobChunk` per tick, sv V0): `GetRequiredComponentDataPtrRW` bumps the chunk's version even when nothing is written; `...PtrRO` never does; a `ComponentLookup` write (`lookup[e] = v`) bumps only the written chunks; enable-bit writes (`lookup.SetComponentEnabled`, `chunk.SetComponentEnabled(ref h, i, b)`, `chunk.GetEnabledMask(ref h)[i] = b`) bump only the enableable component's version; a system's own RW access never trips its own `DidChange(ref h, state.LastSystemVersion)` next update. A Tl generated kind job takes RW pointers to every writable column of any chunk with a live kind, so a DidChange filter behind it sees false positives, never misses.
- Manual-world probe harness without the default world: `[DisableAutoCreation] partial struct S : ISystem` with public config fields, `var h = world.CreateSystem<S>()`, set fields through `ref world.Unmanaged.GetUnsafeSystemRef<S>(h)`, tick with `h.Update(world.Unmanaged)`. `[BurstCompile(CompileSynchronously = true)]` on every job makes even the first launch of a fresh project run Burst (all `[BurstDiscard]` probes 1).

- Untyped columns in Burst (Entities 7.0.0 / Collections 7.0.0 / Burst 2.0.0, the 7000.0.0a16 built-ins; sv adapter v3): a library that must not know the game's component types takes `state.GetDynamicComponentTypeHandle(ComponentType)` and uses `chunk.GetDynamicComponentDataArrayReinterpret<int>(ref h, 4)` (+ `NativeArrayUnsafeUtility.GetUnsafePtr`), `chunk.DidChange(ref h, version)`, `chunk.Has(ref h)`, `chunk.GetEnableableBits(ref h)`, `chunk.SetComponentEnabled(ref h, row, b)`, `chunk.IsComponentEnabled(ref h, row)`; all compile and run in Burst. Untyped writes by entity: `state.GetEntityStorageInfoLookup()`, `info.Chunk` + `info.IndexInChunk`, then the same reinterpret (RW, bumps that chunk's version). A fixed set of handle fields (no arrays of handles) works; fill unused slots with a real handle (e.g. read-only of a component every chunk has) and call `.Update(ref state)` on every slot each tick, or the first structural change throws "...DynamicComponentTypeHandle ... has been invalidated by a structural change". A scheduled `IJob` that only used dynamic handles later broke world teardown ("previously scheduled job ... You must call JobHandle.Complete()") although its dependent handle was completed; running that small job with `.Run()` removed it (root cause not found).
- A managed main-thread host (sv's `Board`) behind an `ISystem`: store `GCHandle.ToIntPtr(GCHandle.Alloc(host))` in an unmanaged singleton, read it in `OnCreate` with `state.GetEntityQuery(...).GetSingleton<T>()`, and resolve it in a non-Burst `OnUpdate` with `GCHandle.FromIntPtr(p).Target` (0 B per frame). Create such systems after the singleton exists (`[DisableAutoCreation]`, `world.CreateSystem<T>()`, `group.AddSystemToUpdateList`, `SortSystems`).
- Test a library against a real Tl generated kind job without a SubScene: game asmdef with `[TlState]` columns, `[TlKind]`, `[TlClip]` and a track (`versionDefines` on `com.tl.unity` + that define in `defineConstraints`, so the test compiles out without Tl), a `TimelineAsset` built in code with `TlTrack`/`TlClip` and the generated `<Ns>_<Clip>_TlData`, bake with the internal `BakingUtility.BakeGameObjects` by reflection, then update `TlLibrarySystem`, `TlTimelineRegisterSystem`, `TlPlayOnAwakeSystem`, `TimelineKindTickSystem` and `TlKindSystem<Tl.Generated.TlAuto_<Asm_With_Underscores>_Jobs>` by hand. A clip at 0.5 s for 0.5 s feeds ticks 30-59.
- `GC.GetTotalAllocatedBytes` is missing from the Unity editor's netstandard profile (CS0117) though the CoreCLR runtime has it: `(Func<bool,long>)Delegate.CreateDelegate(typeof(Func<bool,long>), typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) }))`.
- A per-asmdef `csc.rsp` with `-nullable:enable` next to an embedded package's asmdef takes effect on 7000.0.0a16 (0 CS8632 warnings for sources full of `?`).

- Cross-host identity of a .NET Tl scenario in Unity without a SubScene (tl.unity aeb87e9, sv NorthStar2, verified bit-equal at `JobsUtility.JobWorkerCount` 1 and 11): copy the .NET kinds file into an editor asmdef with the same namespace (kind keys hash full type names, not assemblies), bake the same `TypedTimeline`s in the editor with the vendored `Tl.Gen.Tlb.LibraryBake` + `Tl.LibraryAssembler.Assemble(parts, 60, LibrarySettings.Default)`, load with `TlLibrary.FromBytes(bytes).Publish(manager)`, add the systems with the private `TlSceneRun.AddSystems(world)` (reflection), spawn entities like `TlSceneRun.Spawn` (Playback, TimelineRequest, TimelinePlaying, TimelineHorizon, the `[TlState]` columns, each kind's Entry/Active/Enabled with Active and TimelinePlaying disabled), start with a `TimelineRequest { Slot = -1, Op = RequestOp.Start }`, set the `TimelineStep` singleton, then `group.Update()` per tick. `JobsUtility.JobWorkerCount` can be set at run time for worker sweeps. Tl's Burst probe is `TimelineKindTick.ManagedRuns` on a system entity: query it with `EntityQueryOptions.IncludeSystems`. In a test, write `global::Tl.Unity.Playback` (`Tl.Playback` also exists).

## Measured wins

- Gate after a generated-job change (tl.unity b4d70b3): a test added under `Tests/Kinds/Tests` runs in PlayMode, not EditMode (that asmdef is a runtime test assembly); counts packs 87/72, default 71/72 on the warm second run; the cold first run fails only the Burst-ran-managed assertions (8 packs, 7 default).

- Replace a per-entry search over library tables with a precomputed direct pointer when the build can prove it returns the same answer (tl.unity c922218, `KindIndex.StepInWindow`: one step pointer per window that lies in one stage of a timeline with ascending stage ends; otherwise the old stage search). Tl kind job 4M x4: 25.7 -> 21.6 ms (-16 %), 10k x150: 2.04 -> 1.67 ms (-18 %), hashes identical. Dependent loads per entry, not arithmetic, were the cost; the gain shrinks as the job nears its memory floor (touch job 14.5 ms).

- Skip per-row rule work between known edges ("horizon glide", tl.unity perf/s4-unity-horizon 9f8dccb, tl d0b77476): a 1-byte per-row count of steps until the next edge, recomputed only at edges, turns the Tl kind tick into `tick += dir` for 4 slots. Tick job 4M x4: 14.1 -> 10.3 ms (D=3000) and 14.5 -> 10.0 ms (D=300); 10k x150: 1.08 -> 0.45 ms; totals 40.7 -> 36.9 / 40.7 -> 36.4 / 2.93 -> 2.27 ms, hashes identical, 0 B main thread over 1000 ticks. The 4M job stays ~10 ms with every row gliding because it still reads and writes every 48 B in-chunk playback buffer: it is near the memory floor, so only not touching the bytes (deferred ticks) would go lower. Plan projections of 6-8 ms ignored that floor; measure a touch job first.
- S4+S5 together (tl.unity perf/int-unity, tl d0b77476; 5 procs ABAB, safety off): 4M x4 D3000 38.15 -> 31.40 ms (1.22x; tick 14.0 -> 9.9, kind 24.9 -> 22.0), D300 39.80 -> 31.74, 10k x150 2.87 -> 1.94 (1.48x); with editor-default safety 48.4 -> 39.1 (1.24x). A second agent's rerun from fresh clones (aeb87e9 + harness vs 3b44b20 + harness, own probe, fixtures rebaked with both tl pins: byte-identical) read 37.77 -> 31.25 (1.21x), D300 1.25x, 10k x150 1.44x, same hashes: the old arm is stable across sessions to ~1 %, the 10k ratio to ~3 %. The two wins add (tick and kind are separate jobs), but neither reaches 2x: both jobs sit near their memory floor. Shrinking the kind's inline capacity to its p99 occupancy (user const `KindCapacity.<Kind> = 4` instead of 128 B / 12 B = 10) cut 4M x4 a further 13.8 % (31.15 -> 26.85, tick too: fewer chunks) and 370 MiB, and does nothing at 10k x150; it is a fixture knob, so give the old arm the same capacity before quoting a ratio.
- A setup cost to watch: a derived per-library table built in managed code at bind (2 bytes per timeline tick, 6 MB for 1000 x 3000) plus the extra column raised ready setup 624 -> 685 ms at 4M x4.

- Memory-floor and atomics numbers (sv V0, 11 workers, safety off, 3 processes, ~316 B entities, 48/chunk): a RO touch of 24 B/row reads 1M rows in 0.77 ms and 4M in 3.3 ms (29-33 GB/s); a clamp-and-clear fold of 2 int columns 1.38 / 5.74 ms; a `DidChange`-filtered pass that skips every chunk still costs 0.12 ms at 21,846 chunks (~5 ns/chunk). `ComponentLookup` writes from one Burst `IJob`: 8.5-10 ns each in entity order, 25-44 ns in random order (10k / 1M writes), ~4 us fixed for 128. `Interlocked.Add(ref p[i], 1L)` on a 64 B-aligned `long*` in an `IJobParallelFor` costs the same as a plain random `+=` (0.75 ms per 1M sends, exact); all sends to one target serialize at ~10.4 ns each (1M sends 11 ms).

- A shared reference loop with per-row null tests of optional pointers (`word.Floor == null ? constant : BoundOf(word.Floor[row])`) is not unswitched by Burst 2.0: sv's `Fold.Word` over 1M rows x 2 int columns in an `IJobChunk` took 3.8 ms; the same law as one loop per bound strategy (pointer vs constant chosen once per chunk), bits unchanged, took 1.6 ms (11 workers, safety off). Keep the scalar reference for tests and dispatch to strategy loops in hot jobs; prove equality with a pinned digest, not by reading the loop.
- Measure the bench host before blaming the library: Burst probes can all read 1 while the main-thread stage still climbs cell after cell; sv's `Board` leaked freed sets (Apply + Recalculate 0.3 -> 20 ms over 10 cells of add/remove churn) until fixed. Build a fresh world per bench cell, or run each cell twice and compare.

## Dead ends and traps

- Mono float bits: Unity's bundled MonoBleedingEdge 6.13 (`<Editor>/Data/MonoBleedingEdge/bin/mono.exe`, JIT, also `-O=float32`, `-O=-all`, `--llvm`) keeps float intermediates wider than float32: a chained add/multiply stat fold over 1M random sets differed by 1-2 ulp in 5.9 % of sets from .NET 10, the Unity 7 editor (CoreCLR) and Burst `FloatMode.Strict`/`Default` (all equal, and equal to a strict IEEE reference). Writing `(float)(a + b)` on every operation makes Mono bit-equal. `FloatMode.Fast` differs. Run such legs by compiling with `mono.exe <..>/lib/mono/4.5/csc.exe -unsafe+ -optimize+` and running the exe with `mono.exe`; `--interpreter` segfaulted. NaN payload/sign bits differ between hosts even when values agree: canonicalize NaN before hashing.

- Copying job fields (a pointer-table struct, a bool) into locals once per chunk inside a Burst `IJobChunk` gave -1 % at 4M x4 (inside noise, reverted by the 3 % kill rule) and -3.5 % at 10k x150 (tl.unity 72fe89f): Burst already keeps them in registers; do not expect hoisting to pay.
- An A/B session's old arm drifts by 3-4 % between sessions (kind job 25.9 vs 24.9 ms, same code): normalise each new-arm median by the same session's old arm before judging a 3 % kill rule.

- `BurstCompilerService.GetDisassembly(MethodInfo, options)` (Unity 7: in `UnityEngine.BurstModule`, reachable by reflection) on `JobChunkProducer<T>.Execute` with hand-written options `--dump=Asm --float-mode=Strict ...` returns "Unable to compile method in BurstCompilerService::CompileAsync" instead of throwing; build the options with Burst's own internal `BurstCompiler.Options.TryGetOptions(jobType, out string, ...)` instead (Burst Inspector does this).
- Typed `BufferAccessor<T>` (Entities 6.7) exposes only `this[]` and `Length`; raw `GetUnsafePtrAndLength(index, out length)` exists only on `UnsafeUntypedBufferAccessor` (needs a `DynamicComponentTypeHandle`). With safety checks off a `DynamicBuffer` per row is just a header pointer, so "raw runs" has little to remove.
- Lane-first (skip frame work when a baked fold lane exists) and row-major multi-kind passes cannot help a fixture whose feeders have no `Fold` and whose job has one kind: check the generated job first (Snapshot tool); if it is byte-identical the gain is 0 by construction.
`````
