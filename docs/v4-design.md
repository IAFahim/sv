# sv v4 design: how fast can sv go, and how

Status: proposal with a measured prototype (`proto/Sv.V4`). Nothing in `src/`, `contract/`, tests or pinned digests changes in this PR.

## 1. The answer first

| Load (same shape as `v3-scale`) | sv v3 today | v4 prototype | Speed-up |
|---|---|---|---|
| 20,000 entities x 500 records, 4 cores | 246 to 254 ms | 10.4 to 14.0 ms | about 20x |
| 1,000,000 entities x 10 records, 4 cores | 322 to 333 ms | 74 to 77 ms | about 4.4x |
| 20,000 entities x 500 column ops, 4 cores | 255 ms (as records) | 1.25 ms, 0.65 ms folded | about 200x, 390x folded |
| 1,000,000 entities x 500 column ops, 4 cores | about 12.7 s (as records, extrapolated) | 63 ms, 35 ms folded | about 200x, 360x folded |

Every v4 number is bit-exact against a slow per-record reference law (digest per frame), allocates 0 B warm, and was measured interleaved with v3 on the same VM (4-core Xeon 2.1 GHz with AVX-512, .NET 10, `DOTNET_TieredCompilation=0`).

**100x is not reachable while every op is a 16-byte record that sv must read.** Reading the 160 MB of records in a 20k x 500 frame once, with no work at all, costs 2.8 ms on 4 cores (from L3) and 24 to 26 ms on one core. 100x of 255 ms is 2.55 ms, which is under that floor. The record path tops out around 20x to 50x.

**100x and beyond comes from not sending records at all**: ops that live as columns per chunk (from templates, cards, timelines) and run as whole-chunk SIMD kernels, folded at compile time. That is the same reason tl handles millions of timelines: it skips work, it does not brute-force it.

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

Two phases are 78% of the frame, and both exist because v3 treats every record as individually meaningful: it sorts them so a target's records are adjacent and ordered, then walks them one by one. For most rules that order and identity do not matter. Integer addition is commutative, so a sum of terms is the same in any order on any thread count.

## 3. The v4 idea: tiers by what a rule needs to see

A rule only needs per-record identity when its result depends on which record is which. Most do not. v4 sorts work into three tiers, chosen by the pack compiler per (template, channel):

| Tier | What flows | Cost here | Used when |
|---|---|---|---|
| S, record | v3 path unchanged | 25 ns/record | a rule reads a single record's outcome: per-hit absorb BySource, roll, thorns or lifesteal back to the source |
| R, reduced | class sums per row | 1 to 1.4 ns/record dense | rules depend only on sums per class |
| C, column | ops as columns per chunk | 0.07 to 0.13 ns/op | ops are known per template, card or timeline, not sent per frame |

### 3.1 Tier R: reduce at send, fold once per chunk

**Class.** A class is the set of record attributes that any rule in the pack can tell apart. In the prototype pack it is `channel x source kind x source-executes x sign`, 80 classes, packed as `(channel<<4)|(kind<<2)|(executes<<1)|loss`.
- Channel and source kind come from the key.
- "Source executes" is a source flag read from a commit-only intrinsic lane (`Execute`). It is fixed for the frame, so it can be read at send.
- Sign matters because shield absorb only eats positive damage. Keeping gains and losses in separate classes makes absorb exact on sums.

**Reduce.** Each writer adds `Term(amount, hits)` into its own per-chunk block of int64 class sums `[class][64 rows]`. No sort, no shared writes, no atomics. The AVX2 loop handles four records per step: unpack keys, multiply, clamp to plus or minus 2^32, gather the source flag, compute the cell, add (`proto/Sv.V4/ClassSums.cs`).

**Fold.** Per chunk, sum the writers' blocks, then run the whole rule set once per row as branch-free masks over `Vector256<long>`, four rows at a time (`ChunkFold.cs`):
1. Gate: zero the classes the target is immune to (`ImmuneMask` by channel, `KindMask` by source kind).
2. Threshold: physical from executing sources counts double when health is below 20% of max.
3. Conversion: fire becomes healing while the Converter card is active.
4. Absorb: `taken = min(max(shield, 0), positive damage)`; shield and damage both drop by `taken`.
5. Flow: `health + healing - damage`, clamped once to `[0, max]` (D4: sum, then clamp once).
6. Events: Dead, LowHealth (below 25%).
7. Next-frame emits: a poison tick while `PoisonTicks > 0`, regen while `RegenCard > 0`, into next frame's emit blocks.

These are exactly the owner's examples: "if health is less than X do Y" (threshold, events), "20 fire damage converts into health when a card is active" (conversion), damage types (channels and kinds), ordering (absorb before flow, clamp once).

### 3.2 What is exact on sums and what is not

This is the core of correctness, so it is listed plainly.

Exact on class sums:
- Any sum, gate, channel conversion, threshold on frozen state, clamp once, events.
- Absorb by amount descending, when nothing downstream reads which record was absorbed. Sorting positive terms largest first and absorbing until the shield is gone gives the same total taken as `min(shield, sum)`.
- Multipliers by a power of two or any integer factor applied to a class sum (`2 x sum = sum of 2 x term`).

Not exact on class sums (stays Tier S, or needs an owner decision):
- Truncating multipliers per record, such as minus 50% with integer truncation or additive rage scaling. `trunc(a/2) + trunc(b/2)` is not `trunc((a+b)/2)`.
- Absorb BySource or per hit, where order of sources decides who gets blocked.
- Rolls, and emits back to a specific source (thorns, lifesteal).
- Anything at send time that reads mutable self state or stats. Send may read only the record and commit-only intrinsics; self state is read at fold time.

### 3.3 Tier C: column ops

When an op is known per template, card or timeline rather than sent per frame, it does not need to be a record. The prototype stores ops as small programs per template (`AddConstant`, `AddLaneTimes`, `AddIfLaneAbove`, `AddIfHealthBelowPercent`) and runs each op across a whole 64-row chunk with `Vector256` (`ColumnBench.cs`). Ops with the same shape fold at compile time (500 authored ops became 255), which is exact integer algebra.

This is where 100x lives, and it is how tl already works: lazy stepping, due-tick scans, generated per-kind kernels. tl's outbox is the natural producer of Tier C programs.

## 4. Prototype and how to check it

```
cd proto/Sv.V4
dotnet build -c Release
DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/Sv.V4.dll records 20000 500 4 32 10
DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/Sv.V4.dll columns 20000 500 4 32
DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/Sv.V4.dll stream 20000 500 4
```

Arguments: `mode entities perEntity threads frames checkFrames`.

- `records` first runs an equivalence check: 4,000 entities x 500 signed records at a twentieth of normal health, so shields break and 1,967 entities die in 10 frames. Reference law, scalar fold and vector fold must give the same digest every frame. Then it times the vector engine on the full stream.
- The stream replicates `ScaleBench.Fill`: same xorshift seeds, grouped targets, mixed sources, channel mix, amounts 1 to 8, hits 1 to 2, canonical order per writer.
- Three deliberate bugs were planted to prove the check bites: absorbing losses (caught on both paths, frame 0), a health floor of 1 on the scalar path (caught at frame 8), and a missing source flag gate. All were caught and reverted.
- `columns` checks scalar, vector and folded programs against each other by digest, then times each.
- `stream` measures the memory floor.

The prototype is a standalone project. It references `Sv.Core` and `Sv.Host` (for `Effect` and `Workers`) and is not in `sv.slnx`.

### 4.1 Measured numbers

All on 4 cores unless noted, medians.

- Tier R, 20k x 500: 10.4 to 14.0 ms over three interleaved runs (v3: 246 to 254 ms). Reduce is 10 to 13 ms of that, fold 0.4 to 0.6 ms. One core: 43 ms.
- Tier R, 4k x 500: 2.1 ms, about 1 ns per record.
- Tier R, 1M x 10: 74 to 77 ms (v3: 322 to 333 ms), reduce 42 ms, fold 31 ms. At 10 records per entity the dense 80-class blocks are mostly zeros: each touched chunk costs 40 KB per writer to clear and fold. A pack-sized class set (the lego pack needs about 10 live classes, not 80) and writer-owned chunk ranges should cut this by several times; not yet built.
- Tier C, 20k x 500: one entity at a time 12.05 ms, whole chunk per op 1.25 ms, folded 0.65 ms.
- Tier C, 1M x 500 (500M entity-ops): 566 ms scalar, 63 ms vector, 35 ms folded.
- Memory floor, 20k x 500 records: 2.8 ms on 4 cores, 24 to 26 ms on one.

## 5. What ess, bond, needle-cs and tl contribute

**ess is not a damage-rules library.** It is a command-line wrapper around StateSmith 0.22.2 that turns PlantUML state diagrams into unmanaged C# state-machine structs for Unity ECS. It has no health, damage type, conversion or card concepts; guards and actions are opaque C# text written in the diagram. "If health is less than X, do Y" is possible in ess only because you can type that C# into a guard. A scalar machine costs about 10 ns per entity-op; a SIMD rewrite we tried runs at 0.73 to 1.1 ns and is bit-equal, but the hot logic would still be hand-written guards. Best use: a host-side reactor for status lifecycles (stunned, burning, card phases), or a source format for those. The conditional and conversion logic belongs in sv packs, which already express it (`below`, `gt`, channels, priority, absorb order, the Converter card rules 2012 and 3012 in `packs/lego`).

**bond** is the command admission, journal, rewind and rule authoring layer between untrusted input and sv, tl, gi and sa. It runs around `sv.Step`, not inside it. v4 must keep what bond depends on: `PackEmitter`/`PackPlan`, lane, channel and kind id spaces, `FrameLoop.Send`/`Modify`/`StatOp` with non-negative sv sources (bond uses bit 31), 64-row chunks, one-frame delay, depth 3, event hysteresis, and the pinned Unity and bond hashes. bond carries its own copy of the generated lego kernels, so a v4 kernel change means regenerating bond's copy. Opportunities: bulk chunk-mask ops into Tier C, compiling bond rules straight into fold kernels (bond issue 13), per-chunk event bitmasks.

**needle-cs** is a float SIMD inference engine (Needle text, Whistle voice) on its own threads, 46 to 84 ms per call. It is an input device that feeds bond. It never touches the sv hot loop.

**tl** is the performance bar and the model for Tier C. It is fast because per-tick cost scales with events, not playbacks: 8-byte playbacks, due-tick SIMD scans, generated per-kind kernels, precomputed feeder tables. Measured here: 4.7 ns per active playback at 1M x 4 on one core. v4 should keep tl's outbox shape and let it emit Tier C programs. One problem found: sv's vendored tl Unity generator is pinned 136 commits behind tl main (`a15ea149`), and `NorthStar3Timelines.cs:46` calls `TlSystems.TlModule.Install()`, which current tl no longer generates.

## 6. What v4 keeps

- **Determinism.** Tier R uses int64 addition, which is order- and thread-count-independent; the fold is pure integer masks with no FMA. Results are bit-identical on any core count.
- **Digests.** World digests stay the contract. Tier R can reproduce v3 digests exactly wherever the pack is reducible.
- **Replay.** Log what the frame needs to replay. For Tier R that is the reduced class sums per chunk (kilobytes per touched chunk) instead of 128 MB of records; for Tier C it is the program version and the inputs that changed. Record-level logging stays available as a debug mode.
- **Journal and bond's hashes.** Unchanged where tiers produce the same commits.
- **0 B warm, C# 9, netstandard2.1, `#if NET` intrinsics with a scalar law twin.** The prototype already has the scalar twin for every vector path.

## 7. Migration path

1. **Reducibility classifier in the pack compiler.** For each (template, channel), decide S, R or C from the rules that read it. Emit the class map and the fold kernel next to today's per-target kernels. No runtime change yet.
2. **Tier R behind a flag.** Route records of reducible channels to class-sum blocks; keep everything else on the v3 path in the same frame. Equivalence test: v3 digest equals v4 digest on lego and every fuzz corpus.
3. **Shrink class sets per pack** and give writers chunk ranges, to fix the sparse case (1M x 10).
4. **Tier C API.** Template and card programs, the tl outbox as a producer, bulk chunk-mask ops from bond.
5. **Unity Burst port** of the reduce and fold, then the rule 16 gate: at least 2x at owner scale in both .NET and Unity Burst, hash-exact, 0 B, before any v3 code is replaced.
6. **Retire Tier S paths** only where the owner accepts a contract change (section 8).

## 8. Decisions for the owner

1. **Truncating multipliers.** Should "minus 50%" and similar rules apply to the sum per class (fast, Tier R) or per record as today (exact v3 behaviour, Tier S)? This is a contract change if sums are chosen.
2. **Where ops come from.** 100x needs ops authored per template, card or timeline (Tier C), not sent per frame as records. Is that how games should author sv?
3. **Burst target.** AVX2 as the baseline (what Burst on most players gets) or AVX-512 paths too?
4. **Contract amendment.** Tiers change what is logged and what replay means. That needs a v4 contract section before code lands in `src/`.
5. **tl generator pin.** Update sv's vendored tl Unity generator to current tl, and fix `NorthStar3Timelines.cs` accordingly.
