# sv v4 design: how fast can sv go, and how

Status: decided (section 8) and built as `src/Sv.Columns` (section 9, spec in `docs/v4-spec.md`). The measured prototype stays in `proto/Sv.V4`. Nothing in `contract/`, v3 tests or pinned digests changes.

## 1. The answer first

| Load (same shape as `v3-scale`, 4 cores) | sv v3 today | v4 prototype | Speed-up |
|---|---|---|---|
| 20,000 entities x 500 column ops | 236 to 240 ms (as records) | 0.41 ms | about 580x |
| 1,000,000 entities x 500 column ops | about 15 s (as records, at v3's measured 30 ns per record) | 22 ms | about 680x |
| 20,000 entities x 500 records, canonical order | 236 to 240 ms | 5.2 to 8.7 ms (median of runs 5.7) | about 41x (27x to 46x) |
| 20,000 entities x 500 records, shuffled order | (v3 needs its sort) | 20.7 ms | about 11x |
| 1,000,000 entities x 10 records | 295 to 302 ms | 19.8 to 20.7 ms | about 15x |
| 1,000,000 entities x (500 column ops + 10 records) | about 15 s | 38 ms | about 400x |

Every v4 number is bit-exact against a slow per-record reference law (a world digest per frame, see section 4), allocates 0 B warm, and was measured interleaved with v3 on the same VM (4-core Xeon 2.1 GHz with AVX-512, .NET 10; v4 with `DOTNET_TieredCompilation=0`, v3 with its default JIT settings).

**100x is reached when ops are columns**: 580x at 20k x 500 and 680x at 1M x 500. Ops that live with a template, card or timeline run as whole-chunk SIMD kernels, folded at compile time. That is the same reason tl handles millions of timelines: it skips work, it does not brute-force it.

**100x is not reachable while every op is a 16-byte record that sv must read.** Reading the 160 MB of records in a 20k x 500 frame once, doing nothing with them, costs 5.3 to 7.7 ms on 4 cores on this VM (24 to 30 GB/s; 2.8 ms was seen once on a quieter host). 100x of 236 ms is 2.4 ms, below that floor. v4's record path now runs at that floor: its 5.2 to 8.7 ms is the cost of reading the input once.

## 2. Where v3's time goes

`v3-scale`, 20k x 500, 4 cores (`benchmarks/Sv.Bench/ScaleBench.cs` on the 3.0.0 branch):

| Phase | ms | Why |
|---|---|---|
| send | 18.8 | 10M records written to per-writer arenas |
| seal | 98.7 | sort of every record by key (target, source, kind, channel, ...) |
| log | 23.5 | 128.5 MB input log per frame |
| decide | 101.2 | generated per-target kernels walk sorted runs; absorb uses an insertion sort per target |
| end | 10.7 | commit, events, journal |
| total | 255.4 | 25.5 ns per record |

Seal and decide are 78% of the frame, and both exist because v3 treats every record as individually meaningful: it sorts them so a target's records are adjacent and ordered, then walks them one by one. For most rules that order and identity do not matter. Integer addition is commutative, so a sum of terms is the same in any order on any thread count.

## 3. The v4 idea: tiers by what a rule needs to see

A rule needs per-record identity only when its result depends on which record is which. Most do not. v4 sorts work into three tiers, chosen by the pack compiler per (template, channel):

| Tier | What flows | Cost here | Used when |
|---|---|---|---|
| S, record | v3 path unchanged | 24 to 30 ns per record | a rule reads one record's outcome: per-hit absorb BySource, rolls, thorns or lifesteal back to the source |
| R, reduced | per-row class sums | 0.5 to 2 ns per record | rules depend only on sums per class |
| C, column | compiled ops per template | 0.04 ns per entity-op | ops are known per template, card or timeline, not sent per frame |

Both R and C feed the same per-chunk class totals, and one fold applies the rules to them. A frame can mix all three.

### 3.1 Classes and the per-target context

**Classes.** A class is a set of record attributes that a rule can still tell apart after gating. In the prototype pack that is `channel x sign`: 5 channels times gain or loss, 10 classes, `SumClass.Of(channel, loss) = channel*2 + loss` (`BenchPack.cs`). Sign matters because shield absorb only eats positive damage, so gains and losses stay apart and absorb is exact on sums.

The first prototype carried source kind and "source executes" in the class (80 classes). They are not needed in the class, because both only decide whether a record counts and whether it doubles. v4 decides that at reduce time from a per-target context word, so a 1M x 10 frame clears and folds 5 KB per touched chunk instead of 40 KB.

**Per-target context.** `TargetContext` is one `uint` per slot (`SlotTables.Contexts`, refreshed by the fold when it commits a chunk):
- bits 0 to 19: admit bit for each `(channel << 2) | sourceKind`, built from `ImmuneMask` and `KindMask`. One multiply spreads the five channel bits and the four kind bits into the 20-bit grid.
- bit 20: health x 100 < max x 20, the execute line.

**Execute bitset.** "Source executes" is a commit-only intrinsic of the source, so it is fixed for the frame. It lives in one `ulong` per chunk (`SlotTables.ExecuteBits`, 512 KB for 4M slots), read only when a record could double.

### 3.2 Tier R: reduce at send, fold once per chunk

**Reduce** (`ClassSums.cs`). Each writer adds `Term(amount, hits)` into its own per-chunk block of int64 sums `[10 classes][64 rows]`. No sort, no shared writes, no atomics. For one record:

```
term     = clamp(amount * hits, +-2^32)
admitted = context[target] >> AdmitShift(channel, kind) & 1
doubles  = below(target) & executes(source) & (channel == Physical)
term     = (term << doubles) & -admitted
cell     = SumClass.Of(channel, term < 0) * 64 + row(target)
```

Invalid records (target past the slot limit, unknown channel or kind) are counted, not added. Two AVX-512 paths do eight records per step:
- **Target runs.** When the 1st and 8th records of a batch share a target (records grouped by target, as v3's send produces), the batch adds into eight channel accumulators held in registers ("all" and "loss", where loss is `min(term, 0)`), and flushes gain = all - loss once per run.
- **Mixed batches.** Otherwise the terms are computed as vectors, contexts are fetched with eight scalar loads, and each lane adds into its cell. If all eight targets are in the current chunk the adds go straight in; otherwise each lane is placed with a constant-index extract (see the pitfalls in section 4.3).

The order of records changes nothing: the shuffled stream gives the same digest as the canonical one.

**Fold** (`RecordFold.cs`). Per chunk, sum the writers' blocks, add the Tier C totals, then run the whole rule set once per row as branch-free masks over `Vector256<long>`, four rows at a time:
1. Gate and threshold already happened at reduce; emits from last frame are gated here by the kind-0 and channel admit bits.
2. Conversion: fire becomes healing while the Converter card is active.
3. Absorb: `taken = min(max(shield, 0), positive damage)`; shield and damage both drop by `taken`.
4. Flow: `health + healing - damage`, clamped once to `[0, max]` (D4: sum, then clamp once).
5. Events: Dead, LowHealth (below 25%).
6. Next-frame emits: a poison tick while `PoisonTicks > 0`, regen while `RegenCard > 0`.
7. Refresh: write each row's new context word and execute bit.

These are exactly the owner's examples: "if health is less than X do Y" (threshold, events, `AddIfHealthBelowPercent`), "fire damage converts into health when a card is active" (conversion), damage types (channels and kinds), ordering (absorb before flow, clamp once).

### 3.3 Tier C: compiled column ops

When an op is known per template, card or timeline rather than sent per frame, it does not need to be a record. The prototype authors 500 ops per template of four kinds (`ColumnPrograms.cs`):

| Kind | Meaning |
|---|---|
| `AddConstant` | add N to a channel |
| `AddLaneTimes` | add lane x N (poison ticks x 3, regen rate x 2, ...) |
| `AddIfLaneAbove` | add N if a lane is above a threshold |
| `AddIfHealthBelowPercent` | add N if health is below P% of max |

Each op also carries a channel, a source kind and an executes flag, so the same gates and threshold apply as for records. 1 in 8 amounts are negative.

**Compile.** Ops are grouped by their gate (channel, source kind, executes), at most 24 groups (physical splits by executes), and folded inside a group with exact integer algebra:
- constants sum into one gain and one loss constant;
- lane terms sum per (lane, sign), so `3x + 5x` becomes `8x` and a positive and a negative factor stay apart for absorb;
- lane tests sum per (lane, threshold), health tests per threshold.

500 authored ops become about 267 compiled entries per template. Amounts are limited to 16 bits so the folded sums stay exact in int64 at any lane value.

**Kernel** (`ColumnBench.cs`, `ColumnKernel.AddChunk`). Eight rows at a time with AVX-512: load the 11 lanes widened to int64, then for each group accumulate "all" and "loss" in registers over its constants, terms and tests, apply the group's admit bit and execute doubling, and add gain and loss into the chunk's class totals. The fold then runs exactly as for records. The scalar twin (`ColumnLaw.AddChunk`) applies the raw, uncompiled ops one at a time per row, so the check compares the compiler and the kernel together against the plain law.

This is how tl already works: lazy stepping, due-tick scans, generated per-kind kernels. tl's outbox is the natural producer of Tier C programs.

### 3.4 What is exact on sums and what is not

This is the core of correctness, so it is listed plainly.

Exact on class sums:
- Any sum, gate, channel conversion, threshold on frozen state, clamp once, events.
- Absorb by amount descending, when nothing downstream reads which record was absorbed. Absorbing the largest positive terms first until the shield is gone takes the same total as `min(shield, sum)`.
- Multipliers by a power of two or any integer factor applied to a class sum (`2 x sum = sum of 2 x term`), which is how execute doubling works.

Not exact on class sums (stays Tier S, or needs an owner decision):
- Truncating multipliers per record, such as minus 50% with integer truncation or additive rage scaling. `trunc(a/2) + trunc(b/2)` is not `trunc((a+b)/2)`.
- Absorb BySource or per hit, where the order of sources decides who gets blocked.
- Rolls, and emits back to a specific source (thorns, lifesteal).
- Anything at send that reads mutable state. Reduce may read only the record, the target's context (frozen at last commit) and commit-only intrinsics.

One law difference between tiers is by design: a record term is clamped to plus or minus 2^32 (v3's `EffectLaw.Term`), while a column op has no per-op clamp and relies on the 16-bit amount limit instead.

## 4. Prototype and how to check it

```
cd proto/Sv.V4
dotnet build -c Release
export DOTNET_TieredCompilation=0
dotnet bin/Release/net10.0/Sv.V4.dll records 20000 500 4 32 10
SV4_ORDER=shuffled dotnet bin/Release/net10.0/Sv.V4.dll records 20000 500 4 32 10
dotnet bin/Release/net10.0/Sv.V4.dll records 1000000 10 4 32 10
dotnet bin/Release/net10.0/Sv.V4.dll columns 20000 500 4 32 8 0
dotnet bin/Release/net10.0/Sv.V4.dll columns 1000000 500 4 16 4 10
dotnet bin/Release/net10.0/Sv.V4.dll stream 20000 500 4
```

Arguments: `mode entities perEntity threads frames checkFrames`, and for `columns` a sixth argument, records per entity mixed into the same frame. `SV4_SCALAR=1` times the record engine without the run path.

- `records` first runs an equivalence check: 4,000 entities x 500 signed records at a twentieth of normal health, so shields break and 1,967 entities die in 10 frames. The reference law, the scalar engine and the vector engine must give the same digest every frame, in canonical and in shuffled record order. Then it times the vector engine on the full stream.
- The stream replicates `ScaleBench.Fill`: same xorshift seeds, grouped targets, mixed sources, channel mix, amounts 1 to 8, hits 1 to 2. `SV4_ORDER=shuffled` shuffles each writer's list after the canonical sort.
- `columns` checks the scalar law against the compiled AVX-512 engine by digest every frame at a twentieth of health (deaths, shield breaks and low health all happen), with records mixed in when asked, then times both.
- `stream` measures the memory floor: one pass over the record lists, summing a field.
- Deliberate bugs planted in the first prototype (absorbing losses, a health floor of 1, a missing source gate) were all caught by the check, then reverted.

The prototype is a standalone project. It references `Sv.Core` and `Sv.Host` (for `Effect` and `Workers`) and is not in `sv.slnx`.

### 4.1 Measured numbers

Interleaved with v3 on the same VM, 4 cores, medians over 32 frames after warm-up.

| Run | v4 total | Reduce | Columns and fold | Digest |
|---|---|---|---|---|
| 20k x 500 records, canonical, three runs | 8.7, 5.2, 5.7 ms | 8.6, 5.1, 5.6 ms | 0.12 to 0.14 ms | E90B8BC1FC98DCB6 |
| 20k x 500 records, shuffled | 20.7 ms | 20.5 ms | 0.16 ms | E90B8BC1FC98DCB6 |
| 1M x 10 records, two runs | 20.7, 19.8 ms | 15.6, 14.5 ms | 5.1, 5.3 ms | 0D689455AA1758E3 |
| 20k x 500 column ops, scalar law | 14.4 ms | | 14.4 ms | |
| 20k x 500 column ops, compiled | 0.41 ms | | 0.37 ms | |
| 1M x 500 column ops, compiled | 22.0 ms | | 21.6 ms | |
| 1M x (500 column ops + 10 records) | 38.0 ms | 14.5 ms | 22.5 ms | |
| Memory floor, 160 MB once | 5.3 to 6.5 ms | | | |

v3 in the same session: 236.3 to 239.6 ms at 20k x 500, 294.7 to 302.3 ms at 1M x 10. VM noise moves the v4 record runs between 5 and 9 ms; every run is at or near the floor measured next to it.

What is left in each case:
- **20k x 500 records**: input bandwidth. The reduce is within 1 ms of a plain read.
- **Shuffled records**: random adds into 10 classes x 64 rows per chunk for the whole world, which spills L2. Still 11x v3 with no sort.
- **1M x 10 records**: about 15 ms of reduce over 10M records spread across 15,625 chunks, plus 5 ms to gather and fold every chunk. Writer-owned chunk ranges would cut the gather; not yet built.
- **Column ops**: 0.04 ns per entity-op. Further gains need fewer compiled entries (coarser groups) or int32 lanes, which fit 16 rows per vector instead of 8.

### 4.2 Owner examples as column ops

The rule examples run in both tiers. In the column check, 136 of 4,000 entities died, 1,245 crossed the low-health line and 888 kept some shield after 4 frames, with records and columns in the same frame and digests equal every frame.

### 4.3 Pitfalls found on the way

- **.NET 10 JIT bug.** `Avx512F.Multiply(x.AsInt32(), Vector512.Create(longValue).AsInt32())` gives wrong products when `longValue` is not a constant. `Vector512.Create((int)value)` is correct. The digest check found it (op kinds 1 and 3 differed); the kernel uses the int form.
- **Variable-index `Vector512.GetElement(i)` in a loop** costs about 20 ns per record because the JIT spills the vector each time. Unrolling with constant indexes cut the shuffled case from 46 ms to 20.7 ms (208 to 86 ms on one core).
- **An int executes table of 16 MB** read for every record cost more than all other work in the sparse case. The 512 KB bitset fixed it and also took 20k x 500 from 9.8 to 7.5 ms at the time.
- **.NET has no 512-bit gather API.** The reduce uses AVX2 gathers or scalar loads; a Burst port has the same constraint on AVX2 machines.

## 5. What ess, bond, needle-cs and tl contribute

**ess is not a damage-rules library.** It is a command-line wrapper around StateSmith 0.22.2 that turns PlantUML state diagrams into unmanaged C# state-machine structs for Unity ECS. It has no health, damage type, conversion or card concepts; guards and actions are opaque C# text written in the diagram. "If health is less than X, do Y" is possible in ess only because you can type that C# into a guard. A scalar machine costs about 10 ns per entity-op; a SIMD rewrite we tried runs at 0.73 to 1.1 ns and is bit-equal, but the hot logic would still be hand-written guards. Best use: a host-side reactor for status lifecycles (stunned, burning, card phases), or a source format for those. The conditional and conversion logic belongs in sv packs, which already express it (`below`, `gt`, channels, priority, absorb order, the Converter card rules 2012 and 3012 in `packs/lego`), and in Tier C ops (`AddIfHealthBelowPercent`, `AddIfLaneAbove`).

**bond** is the command admission, journal, rewind and rule authoring layer between untrusted input and sv, tl, gi and sa. It runs around `sv.Step`, not inside it. v4 must keep what bond depends on: `PackEmitter`/`PackPlan`, lane, channel and kind id spaces, `FrameLoop.Send`/`Modify`/`StatOp` with non-negative sv sources (bond uses bit 31), 64-row chunks, one-frame delay, depth 3, event hysteresis, and the pinned Unity and bond hashes. bond carries its own copy of the generated lego kernels, so a v4 kernel change means regenerating bond's copy. Opportunities: bond rules compiled straight into Tier C groups (bond issue 13), bulk chunk-mask ops, per-chunk event bitmasks.

**needle-cs** is a float SIMD inference engine (Needle text, Whistle voice) on its own threads, 46 to 84 ms per call. It is an input device that feeds bond. It never touches the sv hot loop.

**tl** is the performance bar and the model for Tier C. It is fast because per-tick cost scales with events, not playbacks: 8-byte playbacks, due-tick SIMD scans, generated per-kind kernels, precomputed feeder tables. Measured here: 4.7 ns per active playback at 1M x 4 on one core. v4 should keep tl's outbox shape and let it emit Tier C programs. One problem found: sv's vendored tl Unity generator is pinned 136 commits behind tl main (`a15ea149`), and `NorthStar3Timelines.cs:46` calls `TlSystems.TlModule.Install()`, which current tl no longer generates. Fixed by `issues/0512`: both legs now pin tl `d87d1998` and the call is gone.

## 6. What v4 keeps

- **Determinism.** Tiers R and C use int64 addition, which is order- and thread-count-independent; the fold is pure integer masks with no FMA. Results are bit-identical on any core count and in any record order.
- **Digests.** World digests stay the contract. Tier R can reproduce v3 digests exactly wherever the pack is reducible.
- **Replay.** Log what the frame needs to replay. For Tier R that is the reduced class sums per touched chunk (5 KB each) instead of 128 MB of records; for Tier C it is the program version and the inputs that changed. Record-level logging stays available as a debug mode.
- **Journal and bond's hashes.** Unchanged where tiers produce the same commits.
- **0 B warm, C# 9, netstandard2.1, `#if NET` intrinsics with a scalar law twin.** Every vector path in the prototype has its scalar twin, and the checks compare them.

## 7. Migration path

1. **Reducibility classifier in the pack compiler.** For each (template, channel), decide S, R or C from the rules that read it. Emit the class map, the context layout and the fold kernel next to today's per-target kernels. No runtime change yet.
2. **Tier R behind a flag.** Route records of reducible channels to class-sum blocks; keep everything else on the v3 path in the same frame. Equivalence test: v3 digest equals v4 digest on lego and every fuzz corpus.
3. **Tier C API.** Template and card programs and their compiler, the tl outbox as a producer, bond rules as a source.
4. **Writer-owned chunk ranges** for the sparse case (1M x 10), so the gather reads one block per chunk.
5. **Unity Burst port** of reduce, column kernel and fold, then the rule 16 gate: at least 2x at owner scale in both .NET and Unity Burst, hash-exact, 0 B, before any v3 code is replaced.
6. **Retire Tier S paths** only where the owner accepts a contract change (section 8).

## 8. Decisions

The owner delegated these with one principle: build a system that does one thing very well. Each decision below follows from it: the smallest public surface, one way to do each thing, and determinism and digests that are never traded for speed. The normative rules that follow from them are written in `docs/v4-spec.md` together with the library that implements them.

1. **Columns are the engine; records are an adapter.** v4 has one engine: per-template column programs feeding per-row channel sums, then one fold. Standing effects (damage over time, regen, counters, emits to self, card effects) are ops in the pack. Records exist only for one-off hits sent by the host, and they are reduced into the same channel sums before the same fold, so there is one law, one code path and one digest. There is no Tier S in v4: rules that need one record's identity (thorns and lifesteal back to a source, absorb BySource or per hit, rolls, per-hit emits) stay in v3 or become host logic that sends records. Reason: two engines means two laws to keep equal forever; the measured win (580x to 680x) is all in columns.
2. **Multipliers are integer, fractions round once per row.** A `scale` rule multiplies terms by a small integer (exact on sums, so it can run on terms or sums alike). A `convert` rule moves a fraction of a channel's sum with a power-of-two denominator and rounds once per row per frame, on the sum. Per-record truncation is gone. Reason: one rounding point that is the same in every tier, cheap in SIMD, and explainable to a designer.
3. **One term law for both sources, no per-term clamp.** A term is `amount x multiplier x factor` in int64. Op amounts are 16-bit and the compiler bounds the sum of a template's amounts; a record carries a 28-bit amount with hits folded in at send and saturated there. Scale factors per channel multiply to at most 16. Under those limits no int64 sum can overflow, so the only clamp is the one at commit (D4: sum, then clamp once). Reason: v3 clamped records and the prototype did not clamp columns; v4 removes the difference instead of documenting it.
4. **Vector256 and a scalar law, nothing wider.** The runtime has exactly two paths: `Vector256` (AVX2, what Burst gets on almost every player CPU) under `#if NET`, and the scalar law. The AVX-512 prototype numbers stay in section 4 as the upper bound measured on this VM. Reason: one vector width keeps one kernel to prove equal to the law and matches what ships in Unity.
5. **Records are 8 bytes.** A record is one `ulong`: target slot (22 bits), channel (4), source kind (2), the source's frozen rule flags (8), amount (28). The source's kind and flags are read at send, so reduce never touches the source. Reason: the record path is bound by memory bandwidth (section 1), so halving the record halves its floor.
6. **The v4 contract starts as `docs/v4-spec.md`.** It becomes normative when the owner merges it, and moves to `contract/` as a new file then; no existing contract file changes. Replay in v4 is the initial world plus each frame's inputs (records, lane sets, spawns, despawns), re-executed; the 32-epoch journal is not part of v4.0.
7. **tl generator pin.** Tracked as an sv issue; unrelated to v4 and not blocking it.
8. **Rollback is a ring of whole frames plus an editable input log.** Asked for rollback netcode (go back, restore everything, replay with corrected or late inputs), v4 keeps for each of the last H frames the state the frame started from and the inputs it consumed, the shape GGPO-style libraries use (a state ring of max prediction + 1 frames: load, then save and advance per frame). `Rewind(frame)` is the load for hosts that replay their own game logic; `Inputs(frame)` plus `Resimulate()` replays sv's own log after an edit. Per-frame copies beat a lane journal here: a column frame rewrites every alive row, so a journal would hold the whole state anyway, and the copy rides on the resolve while the chunk is in cache (+0.07 ms at 20k, +6.1 ms at 1M). Gates are kept with the lanes so a rewind is a copy (0.15 ms at 20k, against 1.7 ms when they were recomputed). Normative text: `docs/v4-spec.md` section 9. A resimulate replays only the chunks an edit can reach, the way Unity Netcode rolls back only the ghosts a snapshot updated rather than the whole simulation: an entity folds only its own records and logged records are frozen, so a chunk that no edited input and no logged action touches cannot come out different and keeps its present state. One late input over 32 frames at 1M entities went from 2.1 s to 0.24 s. Keyframes and per-frame deltas were weighed for memory and left out: every live row is rewritten each tick, so deltas save nothing, and keyframes would turn a 12 ms rewind into several replayed whole frames to save at most the checkpoint half.

## 9. Built: Sv.Columns

`src/Sv.Columns` is the product of section 8: one pack model, one compiler with every limit checked, 8-byte records, one frame law with a scalar definition (`Law`), Vector256 kernels (`Kernels`), and a slow interpreter of the pack itself (`Reference.ReferenceWorld`). `Sv.Host.ColumnRunner` steps a world on `Workers`. `PackJson` reads packs, with conditions as text (`"Health < 30% of MaxHealth and not event Dead"`). The normative law is `docs/v4-spec.md`.

Checks (`tests/Sv.Columns.Tests`, 43 tests at first, 62 after the hardening pass below, green under default, `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0`):
- ten owner examples with hand-computed numbers (fire to healing with a card, execute doubling, shield absorb, poison ticks, regen, immunity, low-health hysteresis, phoenix revive, burning, hits and saturation);
- scalar, Vector, Vector256 and the reference in lockstep on the arena pack under random traffic, and on 200 random packs; Vector also on lanes too wide for int32 sums;
- record order, writer count, part count and kernel choice give equal digests; grouped target runs with dead targets and undeclared channels match the scalar law, drop counts included;
- 0 B per warm frame; every compiler limit and name error is reported; the JSON arena pack equals the C# one.

`benchmarks/Sv.Columns.Bench` (same VM, 4 cores, Vector256 only, medians after a 2 s warm-up, every run checked against the scalar law first, 0 B warm):

| Load | v3 (section 4.1) | Prototype, AVX-512 | Sv.Columns, Vector256 | vs v3 |
|---|---|---|---|---|
| 20k x 500 column ops | about 240 ms as records | 0.41 ms | 0.58 ms | about 400x |
| 1M x 500 column ops | about 15 s as records | 22.0 ms | 32.3 ms | about 460x |
| 20k x 500 records | 236 to 240 ms | 5.2 to 8.7 ms | 8.1 ms | about 29x |
| 1M x 10 records | 295 to 302 ms | 19.8 to 20.7 ms | 22.8 ms | about 13x |
| 1M x (500 ops + 10 records) | about 15 s | 38.0 ms | 49.8 ms | about 300x |

The product is a little slower than the prototype because it uses 256-bit vectors (decision 4) and runs a general pack (any conditions, scale, convert, absorb, events, gates) where the prototype hard-coded one. The records rows sit near the memory floor measured in section 4.1. Ops reach 100x and more because they are compiled per template and never travel as records; that is the path to use for millions of entities with hundreds of ops each.

What the work found:
- **Tiered JIT.** A method called once per frame stays in tier 0 or on-stack-replacement code for the first frames. The benchmark warms up for 2 s; the reduce loop is a small static method marked `AggressiveOptimization` so it is fully optimized from the first frame.
- **Register pressure.** One big reduce loop spilled every local to the stack (about 50 instructions per record). Splitting the chunk change out and running four same-target records per Vector256 step took 20k x 500 records from 24 ms to 8 ms.
- **Loop order.** Rules outer and rows inner (64 rows per rule) cut the fold and gate refresh from 2.5 to 1.3 microseconds per chunk. Condition masks computed 8 rows at a time as int32, and op terms summed in 16-row register accumulators, took 1M x 500 ops from 89 ms to 32 ms.
- **Self-referencing bounds.** `bounds L [M, L]` must read both bounds before applying either. The random-pack fuzz caught a kernel that applied the floor first.

Hardening pass (2026-11-26, issues 0513 to 0521): a lockstep harness (`Lockstep`), 16 edge tests (`EdgeTests`) and a deep fuzz at the pack limits (`DeepFuzzTests`, `eng/columns-fuzz.sh`) found eight engine faults and one reference fault, all fixed with the canon digest unchanged:
- the sourced send is `SendFrom(source, target, ...)`; with one name, a four-`int` call silently took the other overload, and six existing call sites did;
- out-of-range targets and channels are refused at send and counted in `Dropped` instead of wrapping onto another slot or channel;
- `amount * hits` and convert's `gain * numerator` are exact at every size (convert also made the scalar and Vector256 paths disagree past 2^63);
- calls after `Dispose` throw instead of reading freed memory; spawn, despawn and set throw during a frame;
- a chunk is listed at most once as having room, which fixed spawns of the wrong template after reclaim and a leak under churn.

Unity: `packages/com.sv.columns` mirrors `src/Sv.Columns` verbatim and adds `ColumnStepper` (a frame on the job system) and `ColumnHost` (a MonoBehaviour that opens a pack, steps it and disposes it). Unity 7000.0.0a21 runs CoreCLR (.NET 10) but compiles packages against netstandard2.1 without the `NET` define, so the Vector256 kernels are compiled out there even though the CPU has AVX2. `Simd` closes that gap: ops and condition masks in `System.Numerics.Vector<int>`, summed in int32 only when a per-chunk bound proves it exact, with the scalar law per group otherwise (spec section 6). It also serves any other netstandard2.1 host and ARM through NEON.

The `sv Unity columns` job (self-hosted runner, AMD Ryzen 5 8500G, 12 threads, Unity jobs, every run checked against the scalar law first, 0 B warm) runs 14 EditMode tests, all green, including the canon digest equal to .NET on every kernel and on jobs, and a play-mode host test. Medians:

| Load | Unity, scalar | Unity, Vector | Speed-up |
|---|---|---|---|
| 20k x 500 column ops | 2.59 ms | 0.48 ms | 5.4x |
| 1M x 500 column ops | 125.0 ms | 20.3 ms | 6.2x |
| 20k x 500 records | 6.9 ms | 6.8 ms | records stay scalar |
| 1M x 10 records | 18.5 ms | 18.2 ms | records stay scalar |
| 1M x (500 ops + 10 records) | 133.8 ms | 28.1 ms | 4.8x |

On .NET, one thread, 20k x 500 ops: scalar 18.6 ms, `Vector` 3.3 ms, Vector256 2.2 ms. Vector256 stays the .NET choice because it also vectorizes the fold and the reduce. Burst was not needed: the managed job path with `Vector<int>` already runs 500 ops for a million entities in about 20 ms.
