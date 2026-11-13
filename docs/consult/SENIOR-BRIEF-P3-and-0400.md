# Senior consult brief: sv v3 random-record frame time, and tl running in Unity

You are being asked as a senior engineer who can **not run anything**. Please answer with reasoning and **code snippets** I can paste in. Everything you need is in this file. If something you need is missing, say exactly what, and answer the rest anyway with stated assumptions. Ask yourself the hard questions: tell me what you know that I do not know I should ask (hardware behaviour, JIT/Burst behaviour, known algorithms, Unity internals, prior art).

Answer format I want back:
1. For each question, a numbered answer: the claim, why, and the snippet.
2. Mark every snippet as `tested-by-reasoning` (you could not run it) and list what could break it.
3. Constraints for all C#: C# 9, netstandard2.1 compatible (the same source also compiles in Unity 7000.0.0a16 under Burst 2.0.0 in the kernels), `unsafe` pointers fine, **no FMA, no float non-determinism, no `Vector<T>` size dependence**, bit-exact results, no allocations on the hot path, no comments in code (repo style).
4. Finish with a ranked list: "if you only have two days, do these in this order, expected gain in ms".

---

## 0. What sv is (30 seconds)

sv v3 is a deterministic, rewindable state engine for games, 1,000,000 entities. Entities are rows in 64-row **chunks** (15,625 chunks). State is **lanes**: one value for 64 rows = 256 bytes (`int[64]`). Lanes holding defaults are **shared** (copy-on-write from a per-template lane), so a chunk's lane table `LaneOf[chunk * LanesPerChunk + lane]` points either to a private lane or to a shared template lane. Lego pack: 44 value lanes + 2 meta lanes = 46 lanes per chunk, so `LaneOf` is 15,625 × 46 × 4 B = 2.9 MB.

All change is expressed as 16-byte **Effect records** (`ulong Key; int Amount; int Hits`). A frame:

```
Send   writers append records into arena blocks (11 parallel writers)
Seal   route (drop dead targets), count per (part, chunk), plan offsets, scatter to chunk order,
       order inside each chunk by Key then Amount, merge exact duplicates (sum Hits)
Log    compare the sealed records with the previous frame's input log (rewind/replay support)
Decide per chunk, per target slot: run all rules (gates, shields, thorns, lifesteal ...) over that target's records, write deltas
Commit apply deltas to lanes (copy-on-write), saturating
Events edge-detect conditions over lanes, may emit more records (depth <= 3)
Digest per-chunk hash of changed lanes, combine into a 64-bit frame digest (determinism proof)
```

**Hard rule: results are bit-exact deterministic.** A frame's digest must not change for any optimization. Records are processed per target in **canonical order = ascending (Key, Amount)**, and Key is:

```
bits 63..48 chunk (16 bits), 47..42 row (6 bits)   => target slot = Key >> 42 (22 bits)
bits 41..20 source slot (22 bits)
bits 19..12 kind (8), 11..4 channel (8), 3..2 origin (2), 1..0 depth (2)
```
So inside one target the records are ordered by **source slot** first. That ordering is part of the definition (it decides fold order for non-commutative saturating arithmetic).

## 1.0 UPDATE 2026-11-14 (read this first, it supersedes the numbers below where they differ)

A third pass (branch merged into v3/m8-unity) took `r` from **20.88 to 17.64 ms**; the gate (8.3 ms) is still NOT met. Same-session interleaved, median of 5 x 50 frames, ms:

| | send | seal | log | decide | commit | events | digest | end | total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| r before | 0.76 | 4.64 | 1.19 | 10.18 | 1.23 | 0.15 | 2.32 | 0.26 | 20.88 |
| r after | 0.77 | 4.28 | 1.22 | 8.42 | 1.29 | 0.15 | 1.13 | 0.27 | 17.64 |

A 2.78 -> 2.36, B 7.66 -> 7.76 (unchanged, noise), C 28.45 -> 24.91, D 40.32 -> 35.45. All digests unchanged.

What worked: (1) digest: event cells read only non-zero event words of a lane (digest 2.34 -> 1.16); (2) a per-frame **source-chunk table** (24 B per chunk, 375 KB, one parallel `Sources` phase of 0.17 ms: presence, mask of lanes still on the template, and the template's value row), so a cross-entity read of a lane still on the template is one load instead of three dependent loads (Decide 10.13 -> 8.96, then 8.42 with the value-row pointer); (3) Seal Order returns at the first descent and merges each row as it is sorted (Order -16%, total -1.7%). A real bug found on the way: the `Workers` pool read its generation counter on the worker thread, so a late-starting worker skipped the first Run and the pool hung (fixed, issue 0372).

What did NOT work (so my earlier idea (6) is dead): software prefetch of source rows from Decide (r decide 10.36 -> 10.66), prefetch of the next chunk's table entries (9.76 -> 10.05), prefetch two chunks ahead in Seal Order (18.26 -> 18.35), Seal Count/Scatter with 22 parts instead of 64 (A -9.5%, B -2.7% but r +1.2%, C +1.3%), checking Shape every 8 records. Vector256 table build: bandwidth-bound at 0.17 ms.

My current floor analysis (estimates, not counter-measured): about 120 MB of record traffic per frame over Send, Seal (3 passes), the input log and Decide, roughly 4 ms of bandwidth alone at ~30 GB/s; Decide about **40 ns of core time per record** (8.42 ms x ~5.5 effective cores / 1.17M records), compute and branch bound (per-record channel switch in mixed targets, 10-argument rule method calls, per-target settle and 18-lane delta fold) with source misses gone; Commit and Digest touch all 15,625 chunks every frame. Ideas NOT tried: carry source values in the record (contract change), run splitting (r has ~1.2 records per target so M12b does not apply), writer-side bucketing, fusing Seal and the input-log examine into one pass per chunk, Decide per channel in column-wise batches over a chunk instead of per target, per-chunk M12a (the lego pack cannot use it pack-wide because `immune_by_kind_mask` tells every channel apart, so it needs a per-chunk proof that no KindMask is set), an incremental digest. Rough estimate for more trims of the current shape: 15-16 ms, so a design change is needed.

Because of this, the questions that matter most now are **A1 (what is Decide's 40 ns/record made of), A3 (Seal order and fusing passes), A5/A7 (batched, column-wise Decide), A8 (incremental digest) and A9 (floor and the smallest contract change)**. A2 is now answered by experiment (prefetch loops cost more than they save at this scale); you may still explain why.

## 1. The problem (Part A)

The performance gate is: **frame total <= 8.3 ms at 1,000,000 entities with up to 1,000,000 records per frame, 11 workers (main thread + 10), digest every frame, 0 B managed allocation warm.**

Machine: AMD Ryzen 5 8500G (Zen 4 + Zen 4c mixed cores, SMT, 11 workers), Linux, .NET 10, Release. Please apply your own knowledge of its cache sizes and latencies (I do not trust my numbers: assume L1D 32 KB, L2 1 MB per core, L3 16 MB shared, DRAM ~80-100 ns).

Scenarios (median of 5 processes × 50 frames):

| Shape | Records / frame | Total ms now | Gate |
|---|---|---|---|
| A: 100k random records into every 10th chunk | 100k | 2.82 | pass |
| B: one `Tick` record per entity, already bucketed by chunk | 1M | 7.69 | pass (just) |
| **r: 1,000,000 random records (random target, random source, Physical 50% / Healing 25% / Fire / Poison)** | **1M (1,165,758 after derived records)** | **22.4** | **FAIL, this is the problem** |
| C: B plus r | 2M | 28.5 | beyond envelope |
| D: 3M records | 3M | 41.2 | beyond envelope |

Phase split of `r` (ms): send 0.79, **seal 4.78**, log 1.26, **decide 10.45**, commit 2.08, events 0.16, digest 2.36, end 0.27, total 22.4. 1,165,758 canonical records; about 630k distinct sources; targets are random over 1M entities so each active chunk gets about 75 records, scattered over all 15,625 chunks.

What I measured about Decide (details in issue 0370): with sources in cache a Physical record costs 23 ns; with random sources 46 ns (one worker). With 11 workers the scaling is only about 4.6× (6 physical cores). A synthetic "all source reads return the fallback" run (semantics broken, measurement only; measured on scenario C, not r) gives Decide 11.0 ms vs 13 ms real, so the **source reads cost at most ~2 ms there**. I have not repeated this on r. So the rest of Decide is not source misses; it is the target loop itself (see §1.3). I may be wrong about this; please challenge it.

### 1.1 Seal code (the 4.78 ms), exactly as it is

```csharp
// SealView: Raw arena, Records (canonical out), Scratch, Start/Base/Merged/Active per chunk, Counts[part*Chunks+chunk], Route
public static void Count<TForward>(in SealView view, ref TForward forward, int part) where TForward : struct, IForward
{
    var counts = view.Counts + (long)part * view.Chunks;
    Memory.Clear(counts, (nuint)view.Chunks * sizeof(int));             // 64 parts x 15,625 chunks x 4 B = 4 MB cleared every frame
    Range(view.Blocks, part, view.Parts, out var from, out var to);
    for (var block = from; block < to; block++)
    {
        var records = ArenaCore.Block(view.Raw, block);
        var fill = view.Raw->Fill[block];
        var kept = 0; var moved = 0;
        for (var at = 0; at < fill; at++)
        {
            var record = records[at];
            var key = record.Key;
            var verdict = Sv.Route.Deliver(view.Route, ref forward, ref key);   // live-mask lookup of the target chunk: random access into Live[chunk]
            var keep = verdict >= 0;
            if ((verdict | (kept ^ at)) != 0) records[kept] = new Effect(key, record.Amount, record.Hits);
            counts[keep ? EffectKey.Chunk(key) : 0] += keep ? 1 : 0;           // random increment into a 15,625-int row (62 KB, fits L2)
            kept += keep ? 1 : 0;
        }
        view.Raw->Fill[block] = kept;
    }
}

public static int Plan(in SealView view)      // serial in places: prefix sums over parts x chunks (column-major walk)
{
    var running = 0;
    for (var chunk = 0; chunk < view.Chunks; chunk++)
    {
        view.Base[chunk] = running;
        var cell = view.Counts + chunk;
        for (var part = 0; part < view.Parts; part++) { var c = *cell; *cell = running; running += c; cell += view.Chunks; }
    }
    return running;
}

public static void Scatter(in SealView view, int part)
{
    var cursor = view.Counts + (long)part * view.Chunks;
    // per block: if Raw->Ordered[block] != 0 copy contiguous same-chunk runs with Memory.Copy
    // else the random case:
    for (var at = 0; at < fill; at++)
    {
        var chunk = EffectKey.Chunk(records[at].Key);
        view.Records[cursor[chunk]] = records[at];       // 16 B scattered write; destination = random line of a 16-19 MB array
        cursor[chunk]++;
    }
}

public static void Order(in SealView view, int chunkFrom, int chunkTo)    // per chunk, ~75 records in random order
{
    // Shape(): one pass, descents/repeats flags. If unsorted: Unordered() = ByRow counting sort on the 6-bit row
    // into Scratch, then insertion sort of runs of 16 + bottom-up merge sort by (Key, Amount); then Merge() collapses
    // equal (Key, Amount) pairs summing Hits (saturating). Result written back to Records.
}
```
Seal split (ms, mean, measured on scenario C with temporary probes, not on r; r total Seal is 4.78): count 1.5, plan 0.3, scatter 2.3, order 4.7. r probably has the same shape at about half the volume; I have not split r itself. The order step is the biggest: ~75 random 16-byte records per chunk sorted by a 64-bit key with a comparison merge sort.

### 1.2 What the blocks look like when writers send random records

11 writer threads each append 16-byte records to their own arena blocks in the order the game produced them (random targets). `Ordered[block]` is a flag that says "all records in this block are non-descending by Key" (true for B, false for r).

### 1.3 Decide for one chunk (generated code, the part that costs 10.45 ms)

```csharp
public static ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next)
{
    var v = View(world, chunk);          // resolves ~35 lane pointers for this chunk: LaneOf lookups + Segments[] (one line per lane)
    Masks(world, ref v);                 // computes per-chunk row masks for conditions
    ...
    while (at < count)                   // effects[] are this chunk's canonical records, sorted by (target, source, ...)
    {
        var first = at;
        var target = EffectKey.Target(effects[at].Key);
        var present = ChannelBit(EffectKey.Channel(effects[at].Key));
        at++;
        while (at < count && EffectKey.Target(effects[at].Key) == target) { present |= ChannelBit(...); at++; }
        var row = target & 63;
        var self = 1ul << row;
        switch ((present & (present - 1ul)) == 0ul ? Bits.Tzcnt(present) : -1)      // lone channel: specialised copies
        {
            case 0: totalled |= Target<Lone0>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
            ... case 27 ...
            default: totalled |= Target<Mixed>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;   // targets with more than one channel present (C, D); in r most targets get 1 record (1.17 records per target on average), so they take a Lone path
        }
    }
}
```
`Target<Mixed>` per target: set up `untaken`, `used13/16/21` counters, `reshaped` from card masks, `a.Assign[0..3]` from lanes, then loop over that target's records running **55 rules** (gate layer: priority-ordered, grouped exclusive groups, additive/multiplicative terms into a `ShapeTerm[10]` array, then a canonical fold, clamps, absorb, then emit layer: thorns/lifesteal write new records to `next` writers), then settles the outcome into `scratch` delta and `records`. A rule that reads the source looks like:

```csharp
if (((long)RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 41, 0)) > 0L) { terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, StatLaw.Rescale(RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 43, 0), 100, 10000)); ... }

public static bool Present(in WorldView world, int capacity, long slot)
{
    if ((ulong)slot >= (ulong)capacity) return false;
    var at = (int)slot;
    var meta = Lanes.WordsOf(world, at >> 6, world.Layout.MetaLane);          // LaneOf[chunk*46+meta] -> Segments[lane>>13] + (lane&8191)<<8
    return (((meta[ChunkMeta.Live] | meta[ChunkMeta.Dying]) >> (at & 63)) & 1ul) != 0ul;
}
public static int Read(in WorldView world, int capacity, long slot, int lane, int fallback) => Present(world, capacity, slot) ? Lanes.Read(world, (int)slot, lane) : fallback;
// Lanes.Read = Ints(world, slot >> 6, lane)[slot & 63]  => LaneOf lookup (dependent load) -> Segments[] (dependent) -> the lane (dependent) -> the int
```
Each rule that reads the source repeats `Present` + `Read` (the JIT may or may not CSE them; many are reads of different lanes of the same source). Dependent-load chain per source read: `LaneOf[...]` → `Segments[x>>13]` → lane bytes → value. For a *shared* lane the last address is the same for all sources (cached); for a private lane it is a miss.

Measured shape cost, L1-hot, 1 chunk, one thread (`v3-lego-decide-shape`): Tick records 64 targets × 1 record = 822 ns (12.8 ns per target); Physical 64×1 = 2,360 ns (37 ns per record), 1×64 (64 records on one target) 1,310 ns (20 ns per record).

### 1.4 What has already been tried (do not just suggest these again unless you see why it failed)

- Skipping Seal's order check with a per-chunk "single ordered run" flag: Seal −0.12 ms, Decide +0.11 ms: net 0. (Misses move to the next phase: Seal/Log/Send are bandwidth-bound; moving a pass does not remove the miss.)
- Per-frame **source table** (one row per distinct source holding the 6 lanes the gates read, 17 MB, built per frame): exact, but Decide 13.0 → 14.8 ms. Build cost 2 ms from contended bitmap marking, plus its own random read is an L3 miss.
- A warm pass per chunk that touches each record's source meta and lanes before the gates: Decide 15.9 ms (worse than 13.0!). I do not understand why a prefetch-like pass made it worse. Please explain.
- Software prefetch of the next chunks' log segment; Vector256 log compare: <3% gains.
- Lazy `a.Assign` initialization, gating the chunk row masks by channels present: inside noise.
- `Below` with operands < 2^31 as plain 64-bit products; whole-chunk row masks for events; `Target<LoneN>` specialisation for single-channel targets; per-chunk liveness-sum cache in the digest: these gave B 9.68 → 7.69 ms but do nothing for r.

### 1.5 Reserves the design document allows (use if you judge them right)

- **M12a**: a channel that no rule distinguishes by source, kind, hits or amount may skip records and add straight into a per-target delta scratch (compiler proves the property; result defined identical). For Healing/Fire/Poison in the lego pack I think the rules do distinguish some by source (`owner_only_heal` reads `record.source`), so it may only apply to some channels. Is there a cheap way to prove and exploit this for the rest, so Seal does not need to sort them?
- **M12b**: run splitting for hot targets (split a target with thousands of records over workers).
- Contract changes are acceptable if the digest of the pinned scenarios stays identical: e.g. carrying the source values the gates read inside the record at send time (the Effect is 16 B: Key 8, Amount 4, Hits 4. Could we have a 32 B record with 4 source attributes, if it removes the dependent loads? What is the cost in Seal bandwidth?).

### 1.6 Questions for you, Part A (please answer each)

A1. **Where is the 10.45 ms of Decide on r really going?** Given the numbers in §1.3 (37 ns per Physical record even L1-hot, 46 ns in-frame) and the structure (target loop, `switch` into `Target<Mixed>`, 55-rule chain with guards, saturating `long` arithmetic via `RuleMath.Add/Multiply` with overflow clamps), what are the likely top 3 costs? Suggest the exact `perf stat`/`perf annotate` questions I should ask of the code (branch-misses, bad speculation, dependent-load stalls, JIT inlining failures of `RuleMath.*`, `ref EffectWriter`, struct copies of `DecideArgs`). Which of those can I verify by reading the disassembly (`DOTNET_JitDisasm`) and what would I look for?

A2. **Why would a pre-touch pass make Decide slower** (13.0 → 15.9 ms)? Candidates: touching evicts what the next chunk needs, the pass doubles the dependent-load chain work, TLB misses (the lane segments are 2 MB each: `SegmentBytes = 8192 lanes × 256 B`; 1M entities × 44 lanes shared mostly), 4 KB vs 2 MB pages, false sharing of `Touched[]`? Is transparent huge page worth it here (`madvise(MADV_HUGEPAGE)`, `DOTNET_GCLargePages`, or allocate arena/lane segments with `mmap` + `MAP_HUGETLB`)? Give the allocation snippet and what it changes for the LaneOf table, the Segments, the arena, the ring.

A3. **Seal order step (4.7 ms for ~75 random records per chunk).** Propose the fastest exact algorithm for "sort 16-byte records by (Key, Amount) as an unsigned 64-bit key plus signed 32-bit tie-break, merge exact duplicates, per chunk of ~75 records, 15,625 chunks, 11 threads". Options I see: (a) radix sort on the 6-bit row then insertion on small groups (Key within a target is source,kind,... so ~1 record per target mostly: i.e. after bucketing by row, groups are tiny), (b) a sorting network + AVX2 for 8/16-record blocks, (c) sort **once at send time** on the writer side (each writer bucketizes by chunk so Seal needs only a k-way merge of 11 sorted lists), (d) a single 2-pass LSD radix over a packed 64-bit key with the (target, source) fields. Give code for your best choice, with the expected ns per record, and what it does to Scatter (can Scatter and Order become one pass?). Remember the sort must give the **same total order** as `(Key asc, Amount asc)` and the same merge result.

A4. **Parallelism and the memory system.** Seal's count matrix is 64 parts × 15,625 chunks × 4 B = 4 MB, cleared, filled and prefix-walked every frame regardless of record count; 11 workers on 6 physical cores (hybrid Zen 4/4c). Would fewer parts with larger blocks, a per-writer pre-bucket by chunk range (so a part only touches a contiguous chunk range), or a two-level (chunk >> 7) histogram be better? What is the right number of parts for r, and how should work be assigned to avoid false sharing in `Counts`?

A5. **Source locality in Decide.** Records inside a target are sorted by source (an artefact of the Key layout), and targets in a chunk are 64 rows. Is there an **exact** way to reorder *reads* (not the fold order) so the dependent-load chains of different records overlap (memory-level parallelism)? For instance: a first pass over the chunk's records that only computes `(LaneOf-index, meta-address)` into a dense array using independent loads and `Sse.Prefetch0`, then the rule pass. Why would that have failed in my "warm pass" experiment (A2), and how would you write it so it does not? Give a snippet for an MLP-friendly gather of N sources' `Present` bits and 6 lanes using `Vector256` gathers or software pipelining with depth ~8-16.

A6. **Fixed per-target and per-chunk costs.** `View(world, chunk)` resolves ~35 lane pointers per chunk even when only 1-2 lanes are needed by the records present; `Masks()` computes row masks per chunk. At r every chunk is active (75 records), so what matters is per-record cost, but for A/B it is per-chunk. Show how to make `View` lazy per lane or per channel group without changing semantics and without increasing code size explosively (the 10k-rule pack already emits 14.4 MB of C#, and the lego `Target<T>` is 5.7 KB of machine code per `Lone` instantiation × 28).

A7. **Branchy rule chain.** 55 rules evaluated per record with data-dependent branches (`channel == Physical`, `source.Execute > 0`, `roll(2500)`, ...). Beyond specialising per lone channel, could the emitter specialise per (channel, kind-class) or compile each channel's rules to a branch-free predicated form (compute all conditions as masks over the record batch of one target, SIMD over records instead of rows)? What structure would you generate (struct-of-arrays records for a target group)? Is it worth it at ~75 records per chunk in ~40 targets?

A8. **Commit + Digest on r (2.08 + 2.36 ms).** Commit applies deltas to lanes copy-on-write; digest hashes changed lanes. With 1M random targets nearly every chunk changes several lanes. Suggestions for cheaper digest of changed lanes (the digest must be identical to the current definition: say how to maintain the identical 64-bit result), e.g. vectorized mix with a proven-equal scalar fallback.

A9. **The honest question:** given everything above, what is the realistic floor for frame time on r on this machine (a roofline: bytes moved per record per phase, bandwidth, latency chain counts)? Is 8.3 ms reachable *without* a record-format change, and if not, what is the smallest contract change that makes it reachable? Which ideas above would you actually bet on, in order?

A10. **Secrets of the world.** What techniques do high-performance deterministic simulation engines (e.g. ECS with command buffers, GPU-style sort-based aggregation, "sorted bulk-synchronous" game servers, columnar database vectorized execution like DuckDB/Velox, HFT order books) use for "scatter 1M small updates over 1M entities, group by target, apply rules reading a few attributes of a random second entity" that I have not listed? References welcome (papers, source files).

## 2. Part B: running tl inside Unity (issue 0400)

**STATUS 2026-11-14: SOLVED and verified, questions B1-B5 can be skipped.** A real tl timeline runs inside Unity 7000.0.0a16 on tl's plain host, feeds the outbox, Collect and the Burst jobs; per-frame digests equal the .NET NorthStar3 journals at 1 and 11 workers (pins A566FAEC8837BA57, 2D6A06776AE25DCB). Unity gate: 101/101 with com.tl.unity, 91/91 without, 0 error CS, 0 warning CS, 0 error BC. Merged in v3/m8-unity. How (docs/UNITY.md "tl in Unity"): com.tl.unity as a plain manifest dependency (not under testables), `Tl.Gen.Plain.dll` with label RoslynAnalyzer in its own asmdef folder, one empty `[TimelineSystem]` struct in the glue (the generator emits nothing without one), `game.bytes` baked with `tlb library` against an assembly with the same name as the host's (the library records the assembly name). Open: ECS/SubScene path, PlayMode, IL2CPP.

(Original question text kept below for reference.)

Another agent is working this in parallel; I want your independent view so we do not both get stuck.

Context. `tl` is the owner's timeline library (repo `/home/os/GitHub/tl`, with `com.tl.unity` the Unity package). sv's `sv pack --glue tl` generates `Glue.g.cs` (`tests/Sv.NorthStar3/Glue.g.cs`): `[TlState]` outbox columns (e.g. `PoisonOut` int cells), `[TlKind]` kind feeds, `[TlClip]` clips, an `ITlTrack` named `PoisonTrack` with `[Feeds]`, and a `TimelineRequests` class that Starts a tl timeline when an sv event enters and Stops it when it leaves. The .NET host calls `Sv.Host.Outbox.Send(host, cells, channel, kindLane, targetLane)` which turns each non-zero outbox cell into an `Origin.Timeline` effect record and zeroes the cell (code below). The test must run, **inside Unity 7000.0.0a16 (an alpha, Linux, batch mode, Burst 2.0.0, Entities 7.0.0)**, a real tl timeline on a status entity that writes the outbox column, run Collect (Outbox.Send), step sv's generated Burst jobs, and match the per-frame digests the .NET host produced (committed twin journals) at 1 and 11 job workers. The sv half is already proven (journals replayed through the Burst jobs). Missing: tl itself running in Unity.

```csharp
public static int Send<TKernels>(FrameLoop<TKernels> host, Span<int> cells, int channel, int kindLane, int targetLane) where TKernels : struct, IPackKernels
{
    var sent = 0;
    for (var slot = 0; slot < cells.Length; slot++)
    {
        var amount = cells[slot];
        if (amount == 0) continue;
        var kind = kindLane < 0 ? 0 : host.Read(slot, kindLane);
        var aimed = targetLane < 0 ? 0 : host.Read(slot, targetLane);
        var target = aimed <= 0 ? slot : aimed - 1;
        kind = kind < 0 ? 0 : kind > 255 ? 255 : kind;
        sent += host.Send(target, slot, kind, channel, amount, 1, Origin.Timeline) ? 1 : 0;
        cells[slot] = 0;
    }
    return sent;
}
```

What I know (from reading `com.tl.unity/Tests/Plain`):
- The "plain" path needs `Tl.Gen.Plain.dll` imported as a **Roslyn analyzer** (plugin label `RoslynAnalyzer`, set through Unity, not by hand-editing .meta), placed in an asmdef folder that the game assembly references; a baked `game.bytes` library produced by `tlb library` over the fixture against the game assembly; and a host class like `PlainHost`.
- Those asmdefs are gated by `UNITY_INCLUDE_TESTS`, so the package must be under `testables`, which also compiles tl.unity's own test assemblies, where a kind is registered twice (a known duplicate-registration error).
- The ECS path needs authored Timeline assets and a SubScene bake with a Playable Director and Tl tracks for the glue clips: heavier; chosen follow-up.
- Rule M9: **no change to tl** beyond generated glue.

Questions, Part B:
B1. In Unity 6/7 batch mode, how do I reliably make a prebuilt DLL a Roslyn analyzer **without the editor UI**: exact `.meta` fields (`labels: - RoslynAnalyzer`, `isPreloaded`, `platformData: Any: enabled 0`, `validateReferences: 0`), whether `-batchmode` import honours it on first import, whether the analyzer DLL must be in `Assets/` or can be in a `file:` UPM package, and the known pitfalls (domain reload, analyzer cache, `csc.rsp` alternatives like `-a:path/to/Tl.Gen.Plain.dll`, `Directory.Build.props`, `.globalconfig`)? Give the exact meta file text and an alternative that works from a UPM package referenced by `file:`.
B2. How do I get **only** the tl.unity assemblies I need into the test run without compiling or running tl.unity's own Tests (duplicate kind)? Options: asmdef `defineConstraints`, `testables` limited to one package, a second tiny package with the `Plain` fixture copied (is copying a tl test fixture into sv acceptable?), `[assembly: InternalsVisibleTo]`, or excluding by `optionalUnityReferences`. Show the manifest and asmdef JSON.
B3. How would you build the `game.bytes` library in a Unity batch step (an editor `[InitializeOnLoadMethod]` or a build step that shells out to `dotnet tlb library`) and keep it deterministic and committed? Where should it live so EditMode tests load it with `File.ReadAllBytes` from the package path vs `Resources`/`TextAsset`?
B4. A skeleton of the EditMode test: create the sv world from the generated `PackData`, build the `FrameLoop`, create the tl host (PlainHost-style) with the `PoisonTrack`, advance N frames calling the timeline update, `Outbox.Send`, `Step`, compare the digest with the journal text. Where does the tl clock come from (deterministic frame counter, not `Time.deltaTime`)? Show how to keep Unity out of the loop so it is repeatable at 1 and 11 job workers.
B5. Gotchas of Unity 7 alpha + Burst 2.0 + managed tl code in EditMode: static constructors on background threads, domain reload in batch mode between compile and test, `[BurstDiscard]` probe semantics, function pointer delegates public/internal only, managed exceptions across native frames. What would you check first if the test hangs or the digest differs only in Unity?

## 3. Anything else I did not ask

Tell me the three biggest mistakes or blind spots you see in this design (determinism, canonical order, copy-on-write lanes, log phase, memory layout) given that the gate is 8.3 ms at 1M entities and 1M records, and what you would do differently if you were starting the hot path today. Be blunt.
