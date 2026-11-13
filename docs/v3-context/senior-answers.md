# Senior answers to the fifteen questions

Written 2026-11-12. Nothing was run or looked up.

| Tag | Meaning |
|---|---|
| [FACT] | printed in the brief or its appendices |
| [SURE] | stable, standard knowledge |
| [MEMORY] | recalled; check the docs or measure before relying on it |
| [GUESS] | my estimate |

Corrections to `plan.md` that came out of these answers are listed at the end and were applied to the file.

## 1. Burst and SIMD without FMA, one source

**The seam is a small set of lane primitives, not the loops.**

- Write about 20 functions over a 64-row lane: compare to mask, select, add, saturating add, clamp, `BoundOf`, int to float, multiply, pack mask. Generated kernels call only these and never write a row loop themselves.
- Each primitive has one scalar body. That body is the law, and it is what Burst compiles. Under `#if NET` the same function has a `Vector256`/`Vector128` front door. So `#if` appears about 20 times in one file, not in 10,000 rules.
- RyuJIT does not auto-vectorise [SURE]. On .NET the front doors are the only vector code. Burst vectorises the scalar bodies.

**Source shape Burst vectorises reliably** [MEMORY for the details]:
- constant trip count 64, no early exit, no call that is not inlined;
- inputs and outputs provably distinct (lanes are distinct pool blocks, so `[NoAlias]` on the pointers is true);
- body = load, arithmetic, `condition ? a : b`, store to `output[row]`.

**Masks in two steps.** An OR-reduction of shifted bits (`mask |= bit << row`) is the pattern I trust least to vectorise. Instead:
1. loop A writes one byte per row, 0 or 1 (a compare and a store);
2. pack eight bytes into eight bits with one multiply: `(word * 0x0102040810204080ul) >> 56`, where `word` is a `ulong` holding eight 0/1 bytes. Every product term lands on a distinct bit, so there are no carries and the result is exact. Eight multiplies per chunk, no intrinsics, identical on WASM.

**Strictness.**
- `[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]` on every job [FACT: tl's generated job].
- The float mode belongs to the entry point: a kernel reached from somebody else's `Fast` job is compiled fast [MEMORY]. So kernels are called only from sv's own jobs.
- No `Unity.Mathematics` in kernels (`math.mad`, float `math.min/max`).
- Emit `(float)` around every float operation; it is free on CoreCLR and Burst and makes Mono match [FACT: field note on Mono].
- `Loop.ExpectVectorized()` in `Unity.Burst.CompilerServices` turns a loop that failed to vectorise into a compile error [MEMORY; it sat behind an experimental define].

**Proof from the output.**
- FMA: `objdump -d` over the Burst cache, search `vf(n)?m(add|sub)`, with a `FloatMode.Fast` job as positive control [FACT].
- Vectorised: per primitive, the expected instructions are present (`vpcmpgtd` / `vcmpps`, `vblendvps` / `vpblendvb`).
- .NET: `DOTNET_JitDisasm=<method>`.
- Then bits: every primitive against its scalar body on the edge lattice and on random lanes.

## 2. Saturating 64-bit sums under AVX2

**Do not do 64-bit SIMD at all.**

- The 64-bit sum is per target and scalar (plan T3). Saturate it to 32 bits at the moment the delta is stored. The delta lane is then `int` (256 B, half the scratch).
- Commit becomes the 32-bit fold sv already has, with its pinned 128/256-bit paths [FACT: `Kernel.Fold`].
- If the fold is rewritten, the cheapest exact form is all 32-bit: `sum = a + b; overflow = ((a ^ sum) & (b ^ sum)) < 0; saturated = overflow ? (a >> 31) ^ int.MaxValue : sum`, then clamp with integer min/max (allowed for ints [FACT: clause 2]).
- It equals `IntrinsicLaw.Step`: clamping to the `int` range and then to `[low, ceiling]` inside that range is the same as clamping the exact sum once.
- Cost: about 10 simple operations per 8 lanes, under 0.1 ns per row [GUESS].

If someone insists on 64-bit lanes: AVX2 has signed 64-bit compare (`vpcmpgtq`) and byte blend (`vpblendvb`), so `saturate32` is two compares and two blends, narrowed with `vpermd`. Exact, about 4 operations per lane [GUESS]. AVX-512 has `vpmovsqd`, a saturating narrow in one instruction.

## 3. Compaction

**For 16-byte records, use the scalar branch-free store.** `output[count] = record; count += keep ? 1 : 0;` It is stable by construction and identical on every host. A 256-bit register holds only two records, so SIMD has almost nothing to gain. About 1.5-3 cycles per record [MEMORY].

**In the fused kernel there is no compaction at all.** Remove is a `keep` flag that later steps of the same loop test. A physical remove pass exists only in the interpreter and in `unfused` mode.

For 32-bit row indices, where SIMD does pay:

| Method | Notes |
|---|---|
| bit mask + `tzcnt` loop | best when few bits are set; what the plan's code does |
| AVX-512 `vpcompressd` | compress into a register, then store. The store-to-memory form is very slow on Zen 4 [MEMORY], and the reference machine is Zen 4 |
| AVX2 `vpermd` with a 256-entry shuffle table | 2 KB kernel constant; about 1-1.5 cycles per element [MEMORY] |
| BMI2 `pext` to build the shuffle | fast on Intel and Zen 3+, microcoded and slow on Zen 1/2 [MEMORY] |

All of them keep ascending order, so determinism is not at risk. Only speed differs.

## 4. Generated kernels at 10,000 rules

**Shape.** `switch (channel)` -> one method per channel -> one method per card group, each taking a small decision struct by `ref` (admit priority and verdict, added, additive, multi, cap). Straight-line selects, flat expressions, temporaries reused.

**RyuJIT cliffs** [MEMORY: the JIT's MinOpts thresholds; verify in `jitconfigvalues.h`]:
- a method over about 60,000 bytes of IL, 20,000 IL instructions, 2,000 basic blocks or 2,000 locals is compiled with no optimisation at all;
- only about 1,024 locals are tracked for registers; past that everything lives on the stack.

Target each emitted method an order of magnitude under all of these: under 8 KB of IL, 500 blocks, 200 locals. That is roughly 150 rules per method [GUESS]. Find the real cliff with a scaling run (50, 100, 200, 400, 800 rules per method) and watch for the step in throughput.

**Roslyn.** One deeply nested expression can fail with "expression too long or complex". Flat temporaries avoid it.

**Tiering.**
- Tiers, OSR and dynamic PGO change speed and code layout, never results, for our operation set: integer ops, single IEEE float ops, no automatic fusion [SURE], and `BoundOf` in place of raw casts.
- Put `[MethodImpl(MethodImplOptions.AggressiveOptimization)]` on generated kernels: full optimisation at first call, no Tier-0, no OSR. The price is no PGO, and `static readonly` fields do not fold if the class was not yet initialised [MEMORY]. Generated kernels use `const`, so that is fine.
- Run every kernel once on an empty world at creation. Benchmarks and the 0 B test then start warm.
- NativeAOT, Burst, IL2CPP and WASM AOT have no tiers.

**Burst.** I do not know its size cliffs. One job that reaches all 10,000 rules is one large LLVM unit, and compile time is the likely limit. Measure it at M8; I make no claim.

## 5. Constants

| Form | .NET | Burst | Stale-caller trap |
|---|---|---|---|
| `const` and enum members | immediate everywhere | immediate | **yes**: the value is copied into every calling assembly at C# compile time |
| `static readonly` | immediate at Tier-1 once the class is initialised; not in Tier-0 or ReadyToRun code [MEMORY] | constant if the initialiser can be evaluated at compile time [MEMORY] | no: read from the defining assembly when the caller is compiled to machine code |
| constant through a generic struct (`default(T).Value`) | immediate; struct instantiations are specialised [SURE] | immediate | no |

Rules that follow:
- Inside the pack assembly (kernels and enums generated together), use `const` freely.
- A value's enum number must never change once assigned. That is the plan's stored-id rule, and it makes the copy into callers harmless. My `Layout` example broke it (correction 1 below).
- Anything that changes between pack builds (`LanesPerChunk`, counts, `PackHash`) is `static readonly` or travels in `WorldView`. Never a public `const`.
- Use the trap as a check: emit `const ulong CompiledAgainst` beside `static readonly ulong PackHash`. A caller copies the first and reads the second at run time. They differ exactly when the caller is stale. Assert at world creation.

## 6. The lane table: layout, prefetch, pages, NUMA, false sharing

**Table.**
- One row of lane references per chunk, padded to a multiple of 64 bytes (208 entries = 832 B for the 204-lane example).
- A kernel reads the same few offsets in every row. That is a constant-stride load per call site, which hardware prefetchers follow [SURE]. No reordering by co-access is needed, so table position can equal the stable id.

**Lanes.**
- The lane address comes from the table, so hardware cannot predict it. While working on chunk i, software-prefetch the lanes of chunk i+2: `Sse.Prefetch0` on .NET; Burst has a prefetch intrinsic in `Unity.Burst.Intrinsics.Common` [MEMORY].
- Give each worker its own pool segments and allocate by bump within an epoch. Lanes copied in one pass over consecutive chunks then sit consecutively in memory, and Commit writes a sequential stream. This matters more than prefetch.

**Pages.**
- 2 MB segments, 2 MB aligned, `madvise(MADV_HUGEPAGE)` on Linux [FACT: `NativeArena` already does this].
- Windows large pages need the "lock pages in memory" privilege [SURE]. Use them on a server you control, never assume them on a client.
- Without huge pages the bump allocation above is what keeps TLB misses down: 16 lanes share a 4 KB page.

**NUMA.** One socket: nothing to do. Two sockets: give each worker a fixed chunk range, let it first-touch its own segments, steal work only at the end of a phase. Unity cannot pin threads; ignore NUMA there.

**False sharing.**
- Lanes are 256 B and 64-byte aligned: two lanes never share a line.
- Per-chunk outputs indexed by active-chunk number (`Touched[index]`, 8 B each) do share lines. Hand workers contiguous index ranges of at least 16, so only range edges are shared. My `DecideJob` example missed this (correction 4).
- Pad per-worker state (writer, counters) to 128 B; the adjacent-line prefetcher makes 64 B not enough [MEMORY].
- The lane epoch array is safe once segments are per worker.

**Is it under 10 %?** I expect yes for the reasons above [GUESS]. M1's kill rule decides. Measure after a long soak, not on a fresh world: fragmentation is the real risk.

## 7. Parallel Seal and allocation-free dispatch

**Sort.** The output is the canonical order, so any correct sort gives the same bytes. Choose for speed:
1. Local records (target in the emitting chunk) are already bucketed. Verify order, merge. Linear.
2. Remote records, pass 1: each worker splits its own list into R regions of consecutive chunks (R about 4 x workers). Small histograms, sequential writes.
3. Pass 2: one worker owns one region. Counting sort by (chunk, row), 14 bits, into the canonical arena; then insertion sort inside each target on the rest of the key; then merge equal neighbours.

No pass compares records from different regions, and no worker writes outside its region. Nothing is shared, so nothing is locked.

Something better than sorting: less to sort. Timeline traffic is local, and status entities send to one target. Measure the remote share before tuning pass 2.

**Dispatch.**
- Fixed threads created at warm-up; the main thread is worker 0.
- One shared work record: a function pointer, a state pointer, an item count, an atomic next index, a done count. `delegate*` to a static method, never a delegate or lambda. tl's `TlWorkers` is exactly this shape [FACT] and can be copied.
- Between phases of one frame workers spin; the next phase arrives within microseconds. At frame end they park on a kernel event. Spin about 50 µs before parking [GUESS; tune].
- Hand out contiguous ranges, not single chunks: 15,625 one-chunk items would be mostly overhead.
- First exception is captured and rethrown on the main thread [FACT: `TlWorkers`].
- No thread pool, so no starvation and no per-call garbage. In Unity the job system replaces all of this.

## 8. Cheap rewind and the digest

**Keep the explicit check.**

| Alternative | Why not |
|---|---|
| Windows write watch (`MEM_WRITE_WATCH`) | 4 KB granularity = 16 lanes; a system call per query; no equivalent on WASM or consoles |
| Linux soft-dirty bits | page granularity; `/proc` reads; needs privileges in some setups [MEMORY] |
| page protection and fault handler | a fault costs microseconds; hostile to Burst and Unity; not portable |
| `fork` snapshots | not on Windows, not in a browser |
| SIMD compare of epochs | a kernel writes 1-4 lanes per chunk; nothing to vectorise |

The explicit check is one predictable compare per written lane per chunk, hoisted out of the row loop. It is already close to free, and it is the only one that is portable and exact.

**One real cost to watch:** the first frame of every epoch copies every hot lane. The median frame hides it; the worst frame shows it. Report p99 and max (correction 5). If it hurts: copy the lanes that were hot last epoch at the epoch mark, in parallel, before the frame starts.

**Digest.**
- Per cell: `Key(lane, slot, bits)` [FACT: `CellDigest`]. Sums are wrapping adds, so order never matters.
- Tree: lane sum -> chunk sum (one `ulong` per chunk, 512 KB at full capacity) -> world sum. Two peers compare world sums; on mismatch they compare chunk sums, then lane sums of one chunk, then cells. The Board already does this per page [FACT: `FirstDivergentPage`].
- **Take the digest from the journal.** At an epoch mark every changed lane has its old and new copy side by side. Add `Key(new) - Key(old)` for the cells that differ. A cell that changed twenty times costs two mixes, once per epoch. Digest cadence = epoch length.
- An additive hash detects accidents. It does not resist an attacker who can choose values [SURE]. For anti-cheat, rely on server authority, or mix with a per-match secret.

## 9. Float and platform traps

**The whole float operation set is: `+`, `-`, `*`, `/`, compare, select, int to float, and `BoundOf`.** Everything else is forbidden until someone proves it on every host.

| Trap | What differs | Rule |
|---|---|---|
| fused multiply-add | one rounding instead of two | forbidden; grep proves it |
| float min / max | NaN and signed-zero results differ between x86, ARM and WASM [SURE] | compare and select only [FACT: clause 2] |
| NaN bits | which payload and sign propagates differs; WASM allows any [SURE] | canonicalise before storing or hashing [FACT: `CanonicalNaN`]; never branch on a payload |
| float to int | x64 gives `0x80000000`, ARM and WASM-sat saturate, NaN differs [FACT: clause 16] | `BoundOf` only |
| denormals | a host with flush-to-zero gives 0 where another gives a tiny number; WASM cannot flush at all | the law is IEEE gradual underflow; each worker runs a probe at warm-up (smallest normal x 0.5 must be non-zero, computed through a non-inlined call) and refuses to start otherwise |
| denormal speed | arithmetic on them is very slow on some CPUs | where a stat is stored, replace a subnormal result with +0 by select, next to the NaN rule. Same on every host |
| wider intermediates | Mono keeps them [FACT] | `(float)` on every operation |
| WASM SIMD | 128-bit only; the relaxed-SIMD operations may differ per engine [SURE] | 128-bit path only; make sure the build does not enable relaxed SIMD [MEMORY for how .NET exposes the switch] |
| estimates | reciprocal and rsqrt estimates differ per CPU | forbidden |
| transcendentals | platform libraries differ | forbidden; pre-folded tables [FACT: `Curve`] |
| shift counts | C# masks the count to the type width; a shift by 64 is a shift by 0 | never shift by an unbounded value (correction 2) |
| integer divide | traps on zero and on `MinValue / -1` | not an operator in the rule language |
| decimal text to float | parsers can differ | the pack compiler converts once; generated code carries the result as 9 significant digits, and a test compares the bits |

`sqrt` is correctly rounded everywhere [SURE] and could be allowed later. Nothing needs it today.

## 10. Embedded tables

**Tables live in native memory owned by the world, filled at warm-up from the pack file.** Kernels receive a pointer in the view struct.

- One mechanism for .NET, Burst and WASM. No managed array, no static constructor, nothing to pin.
- It is what the Board already does for curves [FACT: `curveData`].
- A pack that only changes numbers reloads without recompiling.
- The pack file carries a hash; `PackHash` covers it.

Small fixed sets become code: the emitter writes immediates and `switch` cases.

For .NET-only tools, `static ReadOnlySpan<byte> Name => new byte[] { ... }` compiles to data in the image with no allocation [SURE]; newer compilers extend that to other primitive types [MEMORY]. Burst accepts `static readonly` arrays of primitives as read-only data [MEMORY]. Neither is needed if the native table is the rule.

## 11. Emitter shape

**A separate pre-build tool (`sv pack`), with the compiler as a library. Not a Roslyn incremental generator.**

- A generator runs inside the IDE on every edit and must answer in milliseconds. A 10,000-rule pack takes seconds.
- Unity wants real `.cs` files on disk: Burst, asmdefs and other generators all see them, and Unity's generators never see another generator's output [FACT: field notes].
- The web tool needs the compiler as a library anyway.

**Caching and identical output.**
- Key = hash of (all input files, tool version). Same key: write nothing.
- Write a file only when its bytes change, so Unity does not recompile for nothing [FACT: `tlb kinds` does this].
- Output is a function of the input: rows in id order, `\n` line endings, invariant culture, no time stamps, no absolute paths, no machine names. A test compiles the reference pack twice in different folders and compares bytes.

**Diagnostics.**
- `path(line,column): error SV0123: text`, the form IDEs and the Unity console turn into links.
- `--diagnostics json` for tools [FACT: tl has the same switch].
- A message names both rules by authored name and says what to change. A row's line and column are kept from the JSON parse.

**Burst.**
- Static methods reached from a `[BurstCompile]` job are compiled with it. They need no attribute [SURE].
- No function pointers between kernels: they stop inlining. Direct calls and `switch`.
- `[NoAlias]` on lane pointers is true by construction. The attribute lives in `Unity.Burst`, so the emitter adds it only for the Unity target. Burst matches `[BurstDiscard]` by name [FACT]; whether it does the same for `[NoAlias]` I do not know. Test it.
- Job structs are concrete and generated per pack [FACT: no generic jobs].

## 12. The checks

**Cheapest independent oracle: WASM, run locally.** Build the interpreter and the compiled kernels for `browser-wasm` or WASI and run the scenario digests under node. It is a different code generator, 128-bit only, with strict IEEE arithmetic. If x64 and WASM agree on a digest, most host bugs are already excluded. No CI is needed, which fits the owner's rule to finish locally first [FACT].

**ARM64:** a real device or an ARM runner is the honest test. Running `dotnet test` under QEMU user emulation may work for small scenarios [GUESS]; I have not seen it done on this code.

**Property tests that catch lane-width bugs random data misses:**

| Test | Catches |
|---|---|
| **Row permutation:** for any permutation p of the 64 rows, `kernel(p(input)) == p(kernel(input))` | any leak between rows, any tail handled differently, any off-by-one at a vector edge. The single most valuable one |
| **One hot row:** all rows default except row r, for every r in 0..63 | mask bit order, wrong lane in a blend |
| **Edges at the seams:** edge values placed at rows 3/4, 7/8, 15/16, 31/32, 63 | boundary handling for each vector width |
| **Garbage in dead rows:** fill non-live rows with random bits, including NaN patterns | results or digest depending on rows that do not exist |
| **Live counts** 1, 63, 64 | tail and full-chunk paths |
| every width against scalar on all of the above | the law itself |

## 13. Pre-mortem for the .NET frame loop (M5)

Ordered by how likely I think each is.

| # | Cause | What exposes it early |
|---|---|---|
| 1 | Decide costs more per record than 4-8 ns: key decoding and unpredictable run boundaries | `Decide1m` at M3 with realistic run lengths (1, 2, 5, 50 records per target); count branch misses |
| 2 | Seal on random records is slower than 3 ms per million | `Seal1m` at M2 with uniform, clustered and single-target inputs |
| 3 | Epoch-start copy spikes: the median passes, the worst frame fails | record max and p99 from the first run of `Frame1m` |
| 4 | Memory bandwidth: masks read more lanes per chunk than assumed | a touch job that only reads the same bytes [FACT: the technique]; compare with the kernel |
| 5 | A generated method falls off a JIT cliff as rules grow | the rules-per-method scaling run of answer 4, at M4 |
| 6 | Dispatch overhead: items too small, workers parking between phases | time an empty frame; it should be tens of microseconds |
| 7 | Pool fragmentation after hours: lanes scattered, TLB misses | bench after a 50,000-frame soak, not only on a fresh world |
| 8 | A worker-dependent value leaks into state (a lane reference, a writer index, a block order) | worker sweep 1/2/4/7/11 on digests from M1 on, every PR |
| 9 | Hidden allocation or zeroing: boxing through an interface call, a closure in dispatch, `stackalloc` cleared inside a loop | the 0 B test from M2; `memset` in the profile |
| 10 | The grow-and-rerun and lossy-merge paths are never exercised and are wrong | run the whole suite once with arena capacity of one block |

## 14. My own doubts

| Claim I am least sure of | If wrong | Cheapest experiment |
|---|---|---|
| One frame of delay between entities is acceptable everywhere (S2) | the design changes at its root: same-frame resolution needs a fixpoint | no code: play five real cards through the interpreter with a designer and ask if the timing feels right |
| About 16 of 204 lanes are non-default per chunk (S3) | sharing saves little; the dense layout is simpler and equal | count non-default lanes per chunk in any existing game state, or a prototype world at M1 |
| 4-8 ns per record in Decide | scenario B and C miss the gate | `Decide1m` on the worked-example pack at M3 |
| Channels as per-frame totals (S1) is what the owner means by "a damage type is an intrinsic" | channel lanes become state, memory and journal grow | ask the owner one question with the two readings side by side |
| `Absorb` order by source slot is acceptable | which hits a shield eats depends on spawn order; designers may find it arbitrary | make the order a rule field (by amount descending, by source); decide with a designer |
| Board undo and the lane journal agree after rewind | stat lanes and the stat machine diverge silently | the twin-world test of M7, written first |
| Burst vectorises the scalar primitives and stays strict | the Unity side runs several times slower | compile the 20 primitives alone in Burst and read the assembly; one day, before M8 |
| Unchanged log segments can be shared by hash | a collision replays wrong inputs | compare bytes on a hash match; it costs nothing measurable |
| "State-only gates also apply to same-frame self emits" is unsurprising | designers get results they cannot explain | three scenarios in the web tool, shown to a designer |
| 256 kinds fit in the key | the record grows to 24 B | ask the owner for the expected number of kinds |

And the four mistakes below, which were not doubts but errors.

## 15. What I would change first

**Stats as fixed-point integers, not floats.**

Every float rule in the plan exists to make floats behave like integers: no FMA, one conversion function, canonical NaN, the denormal probe, no min/max, `(float)` casts for Mono, the WASM NaN caveat, a contract clause for each. A rules engine does not need what floats give. It needs multipliers and percentages. Q16.16 in an `int` does that with a 64-bit multiply and a shift, and is bit-identical on every host with no rules at all.

- Cost: range and precision become a designer-visible choice (Q16.16 is +/-32,768 in steps of 1/65,536). Vector multiply is about twice the work of a float multiply under AVX2 [GUESS].
- Gain: answer 9 shrinks to nothing, the cross-host leg becomes a formality, and the gate algebra and the stat algebra use the same integer type as the records.
- R1 says stats are float and the existing stat machine and its pinned soak digest are float. So this is an owner decision, not something to slip in. I would raise it before M7.

Second: **write the interpreter and the scenarios before any storage.** The meaning of a pack is the product. Lanes, sharing and the journal are optimisations of something that should already be running and agreed with a designer.

Third: **do not pack the record key so tightly on day one.** Start with a 24-byte record and plain fields, prove the laws, then pack to 16 bytes as a measured optimisation with the old form as the oracle.

## Corrections applied to `plan.md`

| # | Where | Was | Now |
|---|---|---|---|
| 1 | 13.1 `Layout` | intrinsic lane = `Stats` count + id, so adding a stat moved every intrinsic lane and broke saved data and compiled callers | every value's enum number is its lane index, from one counter that never reuses a number; counts are not public constants |
| 2 | 13.5 kernel notes | `Damage >> channel` shown without its limit | valid for at most 64 channels; above that the emitter uses `switch` and never shifts by an unbounded value |
| 3 | 9.3 input log | only records were logged | spawns, kills and StatOps are inputs from outside too, and are logged in canonical order |
| 4 | 13.6 job | neighbouring `Touched[index]` slots written by different workers | scheduled in contiguous ranges of at least 16 |
| 5 | 12.5 measurement | medians only | max and p99 as well, because of the epoch-start copy |

