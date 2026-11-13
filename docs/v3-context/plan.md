# plan.md - sv v3: a compiled, layered, rewindable game-state engine

Written 2026-11-12 from the one-file brief only. Nothing was opened, built or run.

## 0. How to read

| Tag | Meaning |
|---|---|
| [FACT] | stated in the brief or its appendices |
| [DECISION] | my design choice, with the reason next to it |
| [ASSUMED] | the brief does not say; I state what I assumed |
| [DERIVED] | arithmetic on stated layouts |
| [PROJECTED] | my estimate, about +/-50 % until measured |
| [TARGET] | a gate an agent must meet |

Rn = requirement n of the brief. Qn = unresolved question n of brief section 6.
Section 17 lists every gap, contradiction, owner decision and unverified claim in one place.

## 1. Verdict

1. **It can be built, and it is one function.** A frame is `Step(State, Inputs) -> (State', Inputs', Events)`. Nothing else exists.
2. **Stored inputs are a multiset** (R5, R6). Its canonical form is a sorted, run-length list of 16-byte records. 1,000 identical hits are one record with `Hits = 1000`. Nothing is lost and the size is bounded by distinct (target, source, kind, type, amount).
3. **Every entity has every value at a generated offset** (R1). The storage unit is the *lane*: 64 rows of one value, 256 bytes. A chunk is a table of lanes. Lanes that still hold defaults are shared, so 1,000,000 entities cost about 0.1-0.2 KB each instead of 813 B dense [DERIVED, section 15].
4. **Layers run in a fixed order** (R7, R15): Seal -> Stats -> Decide (gate, emit, flow) -> Commit (fold, events). Anything meant for another entity is a record for the next frame. That one rule makes every target independent, so the frame is parallel and bit-identical with no locks and no atomics.
5. **Reflection terminates by construction** (R9): a record carries a 2-bit depth, a rule never emits past the depth limit, and the remove pass is a compaction, not a loop.
6. **Rules compile per content pack** (R11) into straight C# 9 that Burst, .NET and WASM all compile. A card is a possession count on the entity; its rules are always compiled in and guarded by that count.
7. **Rewind is a lane journal** (R14): the first write to a lane in an epoch keeps the old lane. Going back to one of 32 worlds costs O(lanes changed since then).
8. **tl stays outside** (R12, R17): generated glue declares outbox columns; sv core references nothing.
9. **Biggest risks:** the rule compiler's size (section 6), tl has no notion of "who started this playback" (section 8), and the 60 Hz budget at 3,000,000 records per frame (section 15).
10. **What dies:** the delta/inbox fold path as the main road, the 4-intrinsic/8-stat adapter limits, hand-written `[TlState]` per value, host-owned intrinsic columns.

## 2. Axioms and theorems

The whole system is these seven axioms. Every later section is a consequence.

| # | Axiom |
|---|---|
| A1 Cell | A value is one 4-byte cell at (slot, value id). Value ids are generated enums. `slot = chunk * 64 + row`. |
| A2 Lane | Storage is in lanes: the 64 cells of one value in one chunk. |
| A3 Record | An input is a record `(key, amount, hits)`. A frame's inputs are a multiset of records. |
| A4 Frozen | A layer reads only what an earlier layer finished. Persistent cells change only in Commit. |
| A5 Own row | Work for target t writes only t's cells. Anything for another entity is a record for the next frame. |
| A6 Fold | A cell changes at most once per frame: `after = clamp(before + saturate32(sum))`. |
| A7 Static rules | The rule set is fixed per content pack. What varies at run time is data. |

| # | Theorem | Why it holds |
|---|---|---|
| T1 Canonical | `Seal(M)` depends only on the multiset M | sort by a total order, then merge equal neighbours |
| T2 Locality | the result for chunk c depends only on frozen state and the records whose target is in c | A4 + A5 |
| T3 Order-free | every sum over records is exact | each term is saturated to +/-2^32, sums are 64-bit, fewer than 2^31 terms: integer addition commutes and associates |
| T4 Termination | a frame is a finite straight-line program; one root record causes at most `F + F^2 + F^3` records | strata form a DAG (checked at compile time); depth <= 3; F = emit rules per record, a compile-time constant |
| T5 Fusion | two layers may run fused per chunk iff the later one reads no cell of another chunk that the earlier one writes | then both orders read identical values |
| T6 Rewind | `Rewind(e)` restores every lane reference of epoch e; replaying the input log reproduces every digest | the journal holds each replaced reference once; Step is a function |
| T7 Width and host | any SIMD width, worker count or host gives the same bits | vertical operations only; each is an integer op, one IEEE binary32 op plus select, or the pinned `BoundOf` |

T2 is the reason no pass needs a lock. T3 is the reason a hot target can be split across workers. T5 is the reason the compiled code may be faster than the layer-by-layer picture while meaning the same thing.

## 3. Data model (R1, R2, R3, R16)

### 3.1 Value classes

| Class | Cell | Kept between frames | Written by | Default |
|---|---|---|---|---|
| Stat | `float` | yes | the stat machine (modifiers), frame step 2 | designer base |
| Intrinsic | `int` | yes | Commit (the fold) | designer default |
| Channel (the record "type") | `int` | no, frame scratch | Decide | 0 |
| Event | 1 level bit; the edge is derived | yes | Commit | designer default level |

- A **channel** is an intrinsic that only holds this frame's accepted total of one type (physical, healing, poison, ...). This is my reading of R2 "a damage type is itself a stat or intrinsic" [ASSUMED]. It has a fixed offset like every value, so rules and events read it by enum.
- `Kind`, `Owner`, link references, card and item counts, immunity masks are ordinary intrinsics. There is no second mechanism for them.
- A stat can bound an intrinsic (max health bounds health). The bound converts through `Clamp.BoundOf`, never `(int)` [FACT: clause 16].

### 3.2 Lane, chunk, world

```
world  = lane table: uint LaneOf[chunk * LanesPerChunk + laneIndex]
chunk  = 64 slots = LanesPerChunk lane references + 1 live word
lane   = 256 bytes in a pool: 64 float | 64 int | 32 words of event bits
cell   = lane base + row * 4            (events: word = event & 31, bit = row)
```

- The generated enum is the lane index. A read is two loads (lane reference, cell); in a kernel the first is hoisted out of the 64-row loop. No dictionary, no string, O(1) (R1).
- Adding a value appends one lane index. Ids are stored in the pack and never reused, so old saves stay readable (R1 "adding stays O(1)").
- **Sharing.** Each template (entity archetype in the designer's sense) owns one read-only default lane per value. A chunk's table points at it until some row differs. The first write copies 256 bytes and retargets the table entry.
- **The dense block is the special case** where the table is the identity: lane `c * LanesPerChunk + i` belongs to chunk c, value i, and the chunk is one contiguous blob. Same kernels, same addresses function. The owner picks the policy (decision D1); I recommend sharing.
- Chunks are filled per template (lowest free slot of that template's chunks, main thread, journal order), so default lanes stay shared.
- Pool: 256-byte blocks, 64-byte aligned, in 2 MB segments that never move. A lane reference is a `uint`. One `uint Epoch` per lane says who may write it (section 9).

### 3.3 Sizes for 100 stats + 100 intrinsics + 100 events [DERIVED]

| Layout | Per chunk | Per entity | 1,000,000 entities |
|---|---:|---:|---:|
| Dense (identity table) | 200 x 256 + 100 x 8 = 52,000 B | 812.5 B | 812.5 MB |
| Shared: table only (204 references x 4 B) | 816 B | 12.75 B | 12.75 MB |
| Shared: table + 16 touched lanes per chunk [ASSUMED 16] | 4,912 B | 76.75 B | 76.75 MB |
| Shared: table + 40 touched lanes per chunk | 11,056 B | 172.75 B | 172.75 MB |

Capacity: 22-bit slots = 4,194,304 entities, 65,536 chunks (limit set by the record key, section 4).

### 3.4 SIMD and copying

- A lane is 4 cache lines: one AVX-512 register covers 16 rows, AVX2 8, NEON/SSE 4. State-only conditions ("is cursed", "has card X") are computed per chunk as one 64-bit mask by a vertical compare. This answers Q4: **the chunked array-of-lanes gives both**; branchy logic becomes mask algebra plus `select`, and only the record-dependent part runs per record.
- Save, load, unload of a chunk: `ChunkImage = live word + 204-bit materialized map + the materialized lanes in id order`. One sequential write of at most 52 KB (R3). The image is canonical: it does not depend on pool addresses.
- Digest: sum of `CellDigest.Key(laneIndex, slot, bits)` over live cells that differ from default [FACT: slot-keyed sums commute]. Lane references and pool order are never hashed.

### 3.5 Slots and death (R14)

`Free -> Live -> Dying -> Free`. A dead entity becomes `Dying`: no layer visits it, its lanes stay, and its slot is not reused until the snapshot ring has moved past its death. So a rewind never needs data that was thrown away, and a record that names it as source stays meaningful.

### 3.6 Where stored inputs live

| Buffer | Shape | Size at capacity C records |
|---|---|---|
| Raw arena (frame n+1 being written) | 4 KB blocks (256 records) from a pool sized at warm-up; one list per chunk for records aimed at the writer's own chunk, one list per worker for the rest | 16 B x C |
| Canonical arena (frame n being read) | one contiguous array + `int Start[chunk + 1]` | 16 B x C + 262 KB |
| Frame scratch | channel totals and deltas, 512 B per (active chunk, written value) | reset every frame |

C is a host setting. C = 1,000,000 costs 32 MB for both arenas.

## 4. The stored-input record (R5, R6, R16)

### 4.1 Fields

```
Effect (16 bytes) = ulong Key | int Amount | int Hits

Key bits  63..42 Target slot   (22)
          41..20 Source slot   (22)   all ones = no source
          19..12 Source kind    (8)   designer enum: sniper, rifle, ...
          11..4  Channel        (8)   the type
           3..2  Origin         (2)   Direct, Timeline, Reflected, Triggered
           1..0  Depth          (2)   causal distance from a root record
```

- Sorting by `Key` is sorting by (target, source, kind, channel, origin, depth). `Key >> 48` is the chunk. One `ulong` compare orders two records.
- `Amount` is one hit's amount, an integer (R18: only integers cross entities). `Hits` is how many identical hits this record stands for.
- Source kind is stored, not looked up: the source may be dead next frame, and a lookup would be a cache miss per record.
- "How much it consumed" (R6) is not a stored field. It is computed: `Taken` = what passed the gates, `NotTaken = Amount - Taken`. Both are available to every later rule [ASSUMED: this is what "consumed" means; see gap G2].
- Limits: 256 kinds, 256 channels, 4,194,304 slots. Past any of them the record grows to 24 bytes (decision D2).

### 4.2 Sending

`Send(target, source, kind, channel, amount, hits = 1)` is the only way to affect an entity. It appends to the raw arena and returns. Zero amounts are not stored. Any system, any thread, any moment of the frame (R5).

### 4.3 Seal: raw -> canonical (start of frame n+1)

| Step | Work | Law |
|---|---|---|
| 1 Route | retarget records whose target forwards that channel to another entity (section 7) | frozen reads |
| 2 Partition | counting sort by `Key >> 48` into the canonical array; fills `Start[]` | stable, parallel by worker histograms |
| 3 Sort | each chunk's segment by (Key, Amount); a segment already in order is only verified | total order |
| 4 Merge | equal (Key, Amount) neighbours become one record; `Hits` add in 64 bits and saturate once | T1 |
| 5 Check | SV_CHECKED: strictly ascending, no zero amount, depth <= limit | - |

The result is the same for any worker count and any arrival order (T1). Records produced by a timeline on its own row arrive already bucketed and in row order, so steps 2 and 3 are a linear check for them.

### 4.4 Bounds and overflow

- **Raw arena full.** Layers are pure, so the cure is safe: finish counting what was needed, grow the pool once (a cold event, logged), rerun the layer. A host with a hard memory cap takes the next rule instead.
- **Canonical arena over C.** Lossy merge in canonical order, in three levels until it fits: (1) merge records with equal Key (amounts add, hits add, flagged `Merged`); (2) merge across sources per (target, kind, channel) with source = none; (3) merge across kinds. Totals per (target, channel) are conserved at every level. The world raises the `Overflow` event with the level. The decision depends only on the multiset, so every host degrades identically.
- This is the answer to the R16 tension: everything is stored, identical things are stored once, and the worst case is bounded and declared.

## 5. The passes (R7, R8, R9, R10, R15)

### 5.1 Frame order

```
frame n+1
 1 Seal      raw arena n -> canonical E                         (section 4.3)
 2 Stats     expire -> StatOps in canonical order -> Recalculate -> changed stats into lanes
 3 Decide    per active chunk, read-only on persistent lanes:
             3a Gate     each record -> Taken per hit
             3b Remove   drop records nothing later needs
             3c Emit     reflection and triggers aimed at others -> raw arena n+1
             3d Flow     channel totals, triggers aimed at self -> delta per intrinsic
 4 Commit    per active chunk, own row only:
             4a Fold     intrinsic = clamp(before + saturate32(delta))
             4b Events   level bits by hysteresis; edges -> messages and on-edge emits
 5 Publish   outward events sorted by slot, digest, epoch mark
 --- state is frozen from here to the next step 2 ---
 6 The rest of the frame: tl, kernels, game systems read sv and Send into raw arena n+1
```

- The order of R7 was a reading. Corrections: grouping "by type" and "by source" is not a separate pass, it is the sort order of the canonical records (source runs are contiguous inside a target) plus the channel totals of 3d. Gates run before totals, because a gate decides what counts. Reflection reads both the offered and the taken amount, so it sits after Gate.
- An **active chunk** has at least one record, a changed stat, or a pending event re-check. Everything else is not visited. An idle world costs the Seal check and nothing more.
- 3a-3d write only frame scratch and the next arena. So they are fused per chunk (T5). 4a-4b read only their own row. So they are fused per chunk. Between 3 and 4 there is one barrier: a rule in Decide may read another entity's cell (`source.X`), and it must see the value from before Commit (A4). If a pack has no such read, the compiler fuses 3 and 4 as well.
- `unfused` mode runs every sub-layer over the whole world before the next, exactly as R15 words it. It gives the same bits (T5) and is the mode the reference interpreter and the web simulator show.

### 5.2 Gate (R8)

A gate rule is `priority, when, then`. `when` is a total boolean expression over the record's fields, the target's cells, the source's cells and named chains. `then` is a list of effects on two decision cells:

| Cell | Effects | How several rules combine |
|---|---|---|
| Admit | `Deny`, `Allow` | the applicable rule with the highest priority decides; none -> Allow |
| Shape | `Added v`, `Additive v`, `Multi v`, `Cap v` | the stat modifier algebra sv already has: `min(Cap, (amount + sum Added) * (1 + sum Additive) * product Multi)`, then `BoundOf` |

- "Physical damage is not allowed": `when channel = Physical then Deny`.
- "Immune to snipers": `when kind = Sniper then Deny`. It drops the poison the bullet carried too, because the test is on the kind, not the channel.
- "Cursed: healing is blocked": `when channel = Healing and self.Cursed > 0 then Deny`.
- "Cannot heal while double jumping" (priority 10, Deny) against "heals twice while jumping" (priority 20, `Allow` + `Multi 2`): the higher priority owns Admit, so healing passes and doubles. Swap the numbers and it is denied. A `Shape` effect of a rule only applies if the record is admitted.
- Equal priority with different Admit verdicts on overlapping conditions is a **compile error**, not a silent tie-break. The designer must choose.
- Shape effects stack by default. A rule may join an exclusive group; then only the highest priority member of the group applies.
- Immunity as a bit mask (5 = bits 101): `when (self.Immunity >> channel) & 1 then Deny`. One generated flags enum.
- Immunity as a count ("5 times immune"): `Absorb(budget = self.ImmuneCharges, per = Hit)`. It takes `min(budget, hits)` hits from each record in canonical order and lowers the budget through the fold. `Absorb(per = Amount)` is a shield. Absorb is the only effect that depends on record order, and the order is the canonical one (by source slot, kind, channel, amount), never arrival time, because a frame has no arrival time.
- **Chains.** A chain is a named boolean expression. Chains may use chains; the graph must be acyclic (compile error otherwise). "Is any blocking chain active for health" is `any` over the chains tagged (blocks, Health). A chain that reads only the target's cells becomes one 64-bit mask per chunk.

Float use is row-local and deterministic: `Amount` converts to float, the four kinds apply in a fixed code order, `BoundOf` converts back. Each step is one IEEE operation. No FMA.

### 5.3 Remove (R9)

One compaction over the chunk's records: a record stays only if it was taken, or an emit rule still wants it (a denied record can still be reflected when the rule says `of = Offered` or `NotTaken`). Later sub-layers see survivors only. This is the owner's remove pass: records leave the list; no pass ever adds to the list it is reading.

### 5.4 Emit: reflection and triggers (R9, R10)

One primitive: `on <condition> emit(to, channel, amount, per)`.

| Part | Choices |
|---|---|
| `to` | `Self`, `Source`, `Owner`, a reference-valued intrinsic |
| `amount` | constant, `Offered`, `Taken`, `NotTaken`, each optionally scaled by a stat of the emitting row |
| `per` | `Record`, `Hit`, `Source`, `Frame` |

- **Reflection** is `emit(to = Source, channel = same, amount = Taken, per = Record)`. "What I did not take is added to them" is `amount = NotTaken`. "Immune and still reflect" is `amount = Offered`.
- **To another entity** always means a record in the next arena with `Origin = Reflected | Triggered`, `Depth + 1`, source = the emitting entity, kind = its `Kind`. The receiver gates it like any record: it may be immune.
- **To self** in the same frame is a delta added in Flow. Self rules are ordered into strata by what they read and write. A cycle is a compile error unless one edge is marked `nextFrame`, which turns it into a record. This answers Q1: **triggers may cascade, to a depth fixed at compile time.**
- A same-frame self emit into a channel is still subject to that channel's state-only gates (cursed blocks a triggered heal). Record-dependent gates cannot apply; there is no record.

**Termination and bounds (Q5).**
- A rule does not fire on a record whose depth is at its limit. Default limit for reflection: 0, so a reflected record is never reflected. World maximum: 3.
- Mutual reflection: A hits B in frame n. Frame n+1: B takes it and reflects (depth 1). Frame n+2: A gates and takes the reflected record; A's reflection does not fire on depth 1. Done in two frames, for any immunity on either side.
- Mutual immunity: both records are denied in Gate; `amount = Taken` is 0 and zero records are not stored. Done in one frame.
- Work per frame is `O(records + active chunks)`. Records caused by one root: at most `F + F^2 + F^3` (T4).

### 5.5 Flow and Fold (clause 19 stays)

- Channel total: `Total[c] = sum(Term(Taken, Hits))` with `Term = clamp(Taken * Hits, -2^32, 2^32)` in 64 bits (T3).
- A flow is a designer row: `Health += Healing - Physical - Poison - Bleeding`. A record may also name a persistent intrinsic directly.
- Fold, once per changed cell: `after = clamp(before + saturate32(delta), min(floor, ceiling), ceiling)` [FACT: `IntrinsicLaw.Step`, unchanged].
- **Sum, then clamp once.** Heal and damage of one frame are added first. Reason: the result is a function of the multiset, so no order exists to argue about, and no entity dies at 0 in the middle of a frame that also healed it. The cost: "heal first, then hit" within one frame cannot be expressed except through `Absorb`. Decision D4 if the owner wants otherwise.

### 5.6 Events (Q2)

**An event is a per-entity value: one level bit, kept.** `next = (~state & enter) | (state & stay)` [FACT: `Condition.Next`]; the edge is `state ^ next`. An edge does two things: it becomes an outward message (ascending slot, capacity per channel, one `Dense` record above the capacity [FACT: clause 17]), and it fires on-edge emit rules, at most one per (entity, event) per frame. Counters are intrinsics. Event conditions read only the entity's own row.

### 5.7 Checks between layers

Two things carry this name, both kept:
- **Designer checks** are chains: conditions evaluated once and reused by the layers after them.
- **Engine checks** run between layers in `SV_CHECKED` builds: Seal output strictly ascending; `0 <= Taken` for unsigned shapes; no record above the depth limit; per-target conservation `sum(Offered) = sum(Taken) + sum(NotTaken)`; no persistent lane written during Decide; delta scratch all zero after Commit.

### 5.8 The worked example, traced

| Layer | Result |
|---|---|
| Seal | P's records in order S1(physical 40, poison 10), S2(physical 20, bleeding 5), S3(healing 15) |
| Gate | card A (`kind = Sniper -> Deny`): S1's two records Taken 0; others Taken = Amount |
| Remove | S1's records leave (card B uses `amount = Taken`) |
| Emit | card B: next arena gets (target S2, source P, physical, 20, depth 1) and (S2, P, bleeding, 5, depth 1) |
| Flow | Physical 20, Bleeding 5, Healing 15; card C (`per = Source`, one damaging source): +5; delta(Health) = -20 - 5 + 15 + 5 = -5 |
| Fold | `clamp(100 - 5, 0, 100) = 95` |
| Events | LowHealth: 95 is not below 30 %; no edge |

Card C's "every damage you take" could mean per frame, per source, per record or per hit. The example gives 5 under the first two. It is a field of the rule, so the designer says which [gap G3].

## 6. Rules (R4, R8, R11)

### 6.1 What a designer authors

A **content pack** is a folder of canonical JSON (`schema: 1`, unknown keys rejected, the style tl already uses [FACT: tl json-format]). Tables, not graphs: 10,000 rows diff, merge and bulk-edit; a graph is a view the tools draw over the rows.

| File | Rows |
|---|---|
| `values.sv.json` | stats, intrinsics, channels, events, kinds: stored id, name, default, bounds, flags |
| `rules.sv.json` | gate, emit, flow and event rules |
| `cards.sv.json` | a card or item = a name + the rules it owns + the modifiers it grants |
| `templates.sv.json` | entity templates: non-default starting values, starting cards |
| `*.scenario.json` | entities, sends per frame, expected values after N frames (the simulator's and the tests' input) |

```json
{ "id": 4101, "name": "immune_to_snipers", "card": "sniper_ward", "layer": "gate", "priority": 100,
  "when": { "eq": ["record.kind", "Sniper"] },
  "then": [ { "deny": {} } ] }
```

- No code per value (R4): adding a stat, a channel or a card is a row. The pack compiler regenerates the enums and the kernels.
- Every row has a stored numeric id that is never reused. Enum values come from ids, not from sort order, so adding a row never renumbers another (the StateSmith notes flag name-sorted ids as a thing not to copy).

### 6.2 Cards add and remove checks without recompiling (Q7)

- A card is an intrinsic: its count on the entity. Giving a card is `Send(entity, Card.SniperWard, +1)`. Taking it is `-1`. It goes through the same fold.
- Every rule a card owns is compiled into the pack with `self.<card> > 0` and-ed into its `when`.
- Per chunk, the kernel first builds the card masks. A rule group whose card mask is zero for all 64 rows is skipped with one test. Entities without cards pay for no card rules.
- **Rules compile per content pack.** A game build links one or more compiled packs. `PackHash` is part of state identity (it replaces `Links.Hash`). A pack that is not compiled in still runs through the reference interpreter (mods, the web tool, hot reload).

### 6.3 Intermediate representation

Three levels, each a plain data structure that can be dumped as JSON and diffed in a snapshot test.

| Level | Content |
|---|---|
| Pack IR | values with ids and types; rules as typed expression trees over `StatId`, `IntrinsicId`, `ChannelId`, `EventId`, `KindId`; hash-consed so equal sub-expressions are one node |
| Layer IR | per layer: decision cells with their rules in final order; chains inlined; strata numbered; every expression split into its *row part* (reads only the target's cells) and its *record part* |
| Kernel IR | per layer, per chunk: a straight vector program (`load lane`, `compare -> mask`, `and/or/andnot`, `select`, `add`, `mul`, `BoundOf`) and a per-record scalar block entered through `switch (channel)` |

Expressions are total: division is not an operator; a comparison with NaN is false; every reference resolves at compile time or the pack does not compile.

### 6.4 Compiler passes

A named, ordered list of `(id, Pack -> Pack)` steps (the one StateSmith idea worth copying whole). A project may insert its own step by id.

| # | Pass | Does |
|---|---|---|
| 1 | Bind | names -> ids, types checked |
| 2 | Validate-1 | designer-facing errors: unknown name, cyclic chain, Admit tie, self-rule cycle, emit depth above the world limit, over 256 channels or kinds |
| 3 | Lower | sugar -> core: cards -> possession guards; "immune to X" -> Deny; reflection -> emit; events -> enter/stay pairs; stat bounds -> fold bounds |
| 4 | Stratify | order self rules by read/write dependency |
| 5 | Order | per decision cell: priority descending, then id; mark rules shadowed by a higher unconditional rule (warning with both ids) |
| 6 | Split | row part vs record part of every condition |
| 7 | Optimise | constant folding; shared sub-expressions by hash; dispatch on channel first, then kind; group rules by card mask |
| 8 | Partition | cap each emitted method (target <= 400 lines) so the JIT and Burst keep optimising; groups chain by direct calls |
| 9 | Emit | `Values.g.cs`, `Kernels.g.cs`, `TlGlue.g.cs`, the Layer IR as data for the interpreter, and a side file `rule id -> authored location` (no comments in generated code) |
| 10 | Validate-2 | compiler self-check: every rule emitted or reported, ids stable against the previous pack, `PackHash` |

### 6.5 Scaling to 10,000 rules

- Cost per record follows the rules for *its* channel and kind whose card mask is set in *its* chunk, not the total. With 10,000 rules over 40 channels and most rules card-guarded, a record meets tens of tests [PROJECTED].
- The `switch (channel)` is compiled by the C# compiler into a jump. That is a branch table made by the compiler, not a data table interpreted by us (R11 "no lookup tables").
- Row parts are evaluated once per chunk as masks, only for rule groups whose card mask is non-zero.
- Compile time and source size: target under 60 s and under 20 MB of generated C# for 10,000 rules [TARGET]; kill rule in section 14.

### 6.6 What the compiler refuses

Cyclic chains; cyclic same-frame self rules; an Admit tie; an emit that could exceed the depth limit; an event condition that reads another row; a float sent across rows; a flow that reads a channel written in a later stratum. Each error names both rules by authored name and location.

## 7. Entity to entity (R13)

- **Any entity can send to any entity**: `Send` takes a slot. No targeting system exists to build.
- **Q3: the hand and the shield are separate entities** with their own values, as R13 words it ("a hand with its own values"). `Owner` is an intrinsic holding a slot. With shared lanes a small entity costs its table share plus the lanes it actually changes, so many small entities are cheap.
- **Forwarding** is a per-template row: `forward Healing -> Owner`. The Route step of Seal rewrites the target before sorting, so the hand's healing reaches the player's health in the same frame. A forward may chain (shield -> hand -> player), at most 4 hops; a longer chain or a cycle drops the record and raises `RouteFault` in checked builds. Routing reads a 1-bit-per-slot "has a forward" set first (125 KB at 1M, cache resident), so unrouted records pay one bit test.
- "A shield's curse goes to the player" is the same row on another channel.
- **Reads.** A rule may read `source.X`, `owner.X` or the cell behind any reference intrinsic. These are frozen reads (A4): every worker sees the value from before Commit. A system outside sv reads with `World.Read(slot, id)`.
- **Deterministic order.** There is none to choose: a target's records are in canonical order, sums are exact, and no entity writes another's cells (A5).
- **Hot targets.** All records of one target are one contiguous run. The merge step already shrinks it. If a run is longer than 4,096 records and the pack has no `Absorb` on that channel, the run is cut into pieces for several workers and their 64-bit sums are added (T3). A million sends to one boss never serialize on a lock; there is no shared cell to contend on.
- **Cost of latency.** A to B takes one frame. A to B to A takes two. Forwarding takes zero. This is the price of T2, and it is the same one-frame rule sv has today [FACT: clause 7].

## 8. Timelines without depending on tl (R12, R17)

### 8.1 The boundary

sv core knows one thing about the outside: something calls `Send`. tl core knows nothing about sv. The pack compiler emits a third thing, the **glue**, into the game's assembly:

| Glue piece (generated, one per channel marked `timeline`) | Purpose |
|---|---|
| `[TlState] struct PoisonOut { int Value; }` | a row-local outbox column [FACT: the form tl binds directly] |
| `[TlKind] struct PoisonFeed` with `Combine: total += next` and `Apply: outbox.Value += total` | tl's own way to write a column [FACT: sv docs/UNITY.md] |
| a clip and track pair `PoisonClip { int Amount; }` | what the designer drops on a timeline; no hand-written code per value (R4) |
| `SvRead` context struct | a tl system context that reads sv cells by slot, for feeders that need a stat (the `AnchorLookup` pattern [FACT]) |

**Collect** runs after the timeline step. For each outbox column it visits the chunks that changed, and for every non-zero cell: `Send(target, source = this row, kind = this row's Kind, channel, amount, Origin = Timeline)`, then writes zero back (consumer clears [FACT: clause 13b]). `target` is the row itself, or the row's target column when the template has one.

Timeline effects are therefore stored inputs like any other: gated, reflected, logged, one frame later (R5).

### 8.2 Who applied the poison

[FACT] tl has no source per playback, and a kind's `Combine` adds all playbacks of a row into one number. So a poison timeline playing on the victim cannot say which sniper started it. Two ways out, no tl change needed for either:

- **Status as an entity (recommended).** The bullet's poison is a small entity: `Owner` = the sniper, `Kind` = Sniper, target column = the victim. The timeline plays on it. Collect sends from it to the victim, so every tick carries the right source and kind, and "immune to snipers" drops the ticks too. When the timeline ends, the entity dies. This is R13 used as intended, and shared lanes make it cheap.
- **Sourceless self effects.** Regeneration and similar play on the entity itself; source = self.

A real per-playback tag in tl would remove the extra entity. It is a tl change and outside this plan (gap G5).

### 8.3 The other direction

- sv -> tl: a rule may `request(timeline, on)`. Requests leave sv as an outward stream in ascending slot order; the glue turns them into `PlaybackTable.Start`. sv never calls tl.
- Stat modifiers at clip edges stay as they are: a start pulse becomes `StatOp.Add`, an end pulse `StatOp.RemoveSource` [FACT: USING-WITH-TL section 2].
- On the .NET host the glue is the same generated source; row = slot.

### 8.4 Cost at the boundary

A record per tick per entity is 16 B against 4 B for a delta cell. At 1,000,000 ticking entities that is 16 MB written per frame and a linear Seal check. If measurement says this is the bottleneck, reserve M12a applies: a channel that no rule distinguishes by source, kind, hits or amount may skip records and add straight into the delta scratch. The compiler can prove that property; the result is defined to be the same.

## 9. Rewind (R14, Q6)

### 9.1 What a snapshot is

Nothing is copied at snapshot time. A snapshot is an **epoch mark**.

- The world has an epoch number. Every lane carries the epoch that may write it.
- First write to a lane in the current epoch: take a new lane, copy 256 bytes, journal `(table index, old reference)`, retarget. Later writes in the same epoch are plain writes.
- The ring keeps 32 epochs (decision D3: frames per epoch; default 2 at 30 Hz = 2.1 s of history).
- When an epoch leaves the ring, the old lanes its journal holds return to the pool.

`Rewind(e)`: walk the journals from the newest epoch back to e+1, restore each table entry, free the newer lanes. Cost: 8 bytes read and one store per changed lane. The live words, the event bits and the `Dying` set are lanes or journaled words, so they rewind the same way.

The stat machine (modifiers, sets, wheel) rewinds with the undo log it already has, one undo frame per epoch [FACT: `BeginUndoFrame`, `Rollback`]. Stat lanes come back through the lane journal; the two must agree bit for bit (test in section 12).

### 9.2 Cost at 1,000,000 entities [DERIVED from the layout; activity levels ASSUMED]

| Activity per epoch | Journal per epoch | 32 epochs |
|---|---:|---:|
| 10 % of chunks, 3 intrinsics each | 4,688 lanes x 264 B = 1.24 MB | 40 MB |
| every chunk, 3 intrinsics each | 46,875 lanes x 264 B = 12.4 MB | 396 MB |
| idle | 0 | 0 |

This is "only a few stored values": only lanes that changed, once per epoch, however many times they changed inside it.

### 9.3 The input log

Per frame the log keeps everything that entered from outside sv: the canonical records with `Origin = Direct | Timeline`, the structural commands (spawn with its template and slot, kill) in main-thread order, and the StatOps in canonical order. Reflected and triggered records are not logged; replay recreates them. (Corrected 2026-11-12: the first version logged records only; a replay without the spawns, kills and StatOps would not reproduce the frame.)

Steady timeline traffic repeats: the same chunk gets the same records every frame. The log stores each chunk segment by content hash; an unchanged segment is a reference to the previous frame's. A long poison costs its first frame.

### 9.4 Lag compensation

1. An input arrives stamped frame j, older than now. If j is older than the ring: clamp to the oldest epoch or reject (host policy).
2. Collect all late inputs of this server tick. Let e = the newest epoch at or before the smallest j.
3. `Rewind(e)`. Insert the late records into the log at their frames.
4. Step forward to now from the log. The result is the corrected present.
5. Outward events of the replayed frames are compared with what was already published; the host receives the difference as `Retracted` and `Added` (what a game does with a retracted death is decision D5).

Cost: `(now - e) x frame cost`, once per server tick, not per packet. With 2-frame epochs and 200 ms of ping at 30 Hz that is 6-8 frames [DERIVED].

Two modes, one mechanism:
- **sv-only replay** (default): other systems' sends come from the log unchanged. Exact for "who hit whom first"; it does not re-run AI or tl.
- **host replay**: the host rewinds its own systems and tl, then sends again frame by frame. sv only needs to be told to drop the log after e. tl can step backward exactly for clip effects [FACT: tl README], but its start/stop requests need the host's own snapshot (gap G6).

### 9.5 What a snapshot does not contain

Frame scratch, raw arenas older than the log window, pool addresses, worker state, compiled code. `PackHash` and the epoch number identify it.

## 10. Hosts (R4, R17, R18)

| Assembly | Language and target | Holds | May reference |
|---|---|---|---|
| `Sv.Core` | C# 9, `netstandard2.1` + `net10.0`, unsafe, no allocation after warm-up | world, lanes, pool, records, Seal, fold law, digest, journal, log, codec | nothing |
| `Sv.Rules` | C# 9, `netstandard2.1` | Pack IR, compiler passes, emitter, reference interpreter | `Sv.Core` |
| `Sv.Pack` (CLI and MSBuild task) | net10 | `sv pack <folder>` -> generated sources + `pack.svb` | `Sv.Rules` |
| `Sv.Host` | net10 | frame loop, worker pool, .NET front doors (`static abstract`, `Vector256`) over the same core | `Sv.Core` |
| `Sv.Wasm` | net10 `browser-wasm`, AOT | `[JSExport]` surface for the web tool | `Sv.Rules`, `Sv.Host` |
| `com.sv.unity` | C# 9, Burst | jobs, Collect, `SvSlot`, authoring windows | `Sv.Core`, `Sv.Rules` (editor only) |

- **Core API** (complete): `World.Create(layout, capacity)`, `Acquire(template)`, `Kill(slot)`, `Send(...)`, `Step()`, `Read(slot, id)`, `Events()`, `Requests()`, `Digest`, `Rewind(epoch)`, `Replay(to)`, `SaveChunk / LoadChunk`, `Dispose`. Eleven verbs.
- **Unity 7.** sv owns its memory; it is not ECS chunk data, so no Unity table is on any path (R17). An entity carries `SvSlot`. `SvBeginSystem` schedules Seal, Stats, Decide and Commit as `IJobParallelFor` over the active chunk list; the job structs are concrete and generated with the pack (no generic jobs [FACT]); every job is `[BurstCompile(FloatMode = FloatMode.Strict)]` and carries a `[BurstDiscard]` probe [FACT]. `SvEndSystem` runs Collect and Publish. Designers add values and rules in an editor window over the same JSON; saving runs the pack compiler and the capacity-style regeneration tl.unity already uses for kinds.
- **Web tool.** The compiler and the interpreter are plain C#, so they run in the browser as WASM. A designer edits rows, presses Simulate, and sees every layer of every frame for the chosen entity: records in, verdict per rule with the rule's authored name, totals, fold, events. The same pack file goes to Unity unchanged (R4).
- **Portable surface** stays as contract v2.1.0 words it: static functions over pointers and plain structs, struct-generic strategies; .NET-only features are front doors over the same core [FACT].

What the tooling buys, concretely:

| Technique | Where it is used |
|---|---|
| Source generation at pack-compile time | enums as offsets, kernels, glue, job structs: no reflection at run time, AOT-safe |
| `Span`, pointers, `stackalloc`, struct strategies | 0 B per warm frame in every kernel |
| Laws as property tests (CsCheck) | T1-T7 are executable, with shrinking to the smallest failing world |
| `DOTNET_JitDisasm` and Burst assembly grep | no FMA, bounds checks gone, vector width as expected |
| NativeAOT / WASM | one core for server, browser and tools |
| `DynamicMethod` IL emission (reserve M12d) | hot-reload a pack on CoreCLR hosts without a rebuild |

## 11. From today's sv

### 11.1 Stays, replaced, deleted

| Today [FACT] | Fate | Reason |
|---|---|---|
| `IntrinsicLaw.Saturate / Step / Applied` | **stays, unchanged** | it is axiom A6 |
| `Clamp.BoundOf`, `Simd.BoundOf128/256` | stays | the only float -> int conversion |
| `Condition.Below / Next / Edges` | stays | the event law |
| `CellDigest.Key` and slot-keyed sums | stays; the column id becomes the lane index | pool order must stay invisible |
| `StatMath`, `Board` stats, `SetTable`, wheel, `StatOp`, `StatOps.Canonicalize`, Board undo | stays as the stat machine; its sink writes lanes | stats are sparse and edge-driven |
| `ModifierKind` (Added, Additive, Multi, Cap) | stays; also becomes the Shape algebra of gates | one algebra, two uses |
| `EventDerive` capacity and `DenseEvent` | stays for outward messages | clause 17 |
| `Memory`, `NativeArena`, `IStateCodec`, `SvState` | stays | allocation and codec seams |
| `Kernel.Fold`, `Fold.Word`, `FoldSpan`, the bound strategies | kept as the scalar reference and the body of Commit | the proven widths are reused |
| `Inbox`, `InboxBuffer`, `InboxLanes` | **replaced** by records | a sum cannot carry a source (brief section 5) |
| delta columns and `LinkMode.Delta` | replaced by records; return only as reserve M12a | same |
| `Link`, `ColumnDef`, `Links.Validate`, `Links.Hash` | replaced by the pack schema and `PackHash` | connections become generated |
| host-owned intrinsic columns, `Board.RunIntrinsicsChunk`, `RunEnabledChunk` | replaced by lanes and Commit | R1 |
| owned stat columns, mirrors, `BindOutput`, `BindSink` | replaced by stat lanes | the lane is the column |
| `Board.Keyframe` | replaced by the lane journal for dense state; Board undo keeps the sparse state | R14 |
| `SvHost`, `SvLinkRegistry`, `SvJobs`, `SvSendJob` (adapter v3) | **deleted** after M8 | limits of 4 intrinsics and 8 stats; hand-written state structs |
| `Sv.Compose`, the old north star | frozen legacy, untouched | their pins stay |

### 11.2 Pinned digests

| Pin [FACT] | Moves? |
|---|---|
| cross-host `A2ACAA69E55F79D8` | No. Its kernels keep their semantics and stay in the tree |
| Burst kernel `2B43BC1549AAE751` | No. Same |
| soak `6D6C602BF07E9366` | No. The stat machine is unchanged |
| new: worked example, NorthStar3, law scenarios, `PackHash` of the reference pack | baked once on .NET, asserted on every leg |

No bump is requested. If M11's deletions reach a pinned scenario, the agent stops and reports instead of moving the pin.

### 11.3 Contract v3.0.0 (owner approves; new file, nothing edited)

- **Clause 7, replaced:** the frame order of section 5.1.
- **Clause 13, narrowed:** accumulate columns exist only as outboxes; their single consumer is Collect.
- **Clause 19, kept** as written.
- **Clause 20, replaced:** connections are the content pack; `PackHash` is part of state identity.
- **Clause 21, new. Records.** The 16-byte layout and key order of section 4.1.
- **Clause 22, new. Canonical inputs.** A frame's inputs are a multiset; Seal's output is its only representation; overflow is the three-level merge.
- **Clause 23, new. Layers.** Axioms A4 and A5; the fusion condition T5.
- **Clause 24, new. Gates.** Admit by highest priority, ties rejected at compile time; Shape by the modifier algebra; Absorb in canonical order.
- **Clause 25, new. Emission.** To others: next frame, depth + 1, never past the limit. To self: stratified.
- **Clause 26, new. Lanes and epochs.** Section 3.2 and 9.1; digests never see lane references.
- **Clause 27, new. Replay.** Section 9.3 and 9.4.

### 11.4 Migration order for agents

One step is one PR with receipts. Old and new live side by side until M11.

1. M0 contract text and owner decisions. 2. M1 layout, lanes, pool, codec, digest. 3. M2 records and Seal. 4. M3 reference interpreter + the worked example. 5. M4 compiler and emitter, differential tests. 6. M5 .NET frame loop and the 1M bench. 7. M6 epochs, journal, log, replay. 8. M7 stat machine onto lanes. 9. M8 Unity adapter v4. 10. M9 tl glue and NorthStar3. 11. M10 web tool. 12. M11 deletions. 13. M12 reserves, only if a gate failed. Gates and kill rules: section 14.

## 12. Verification

### 12.1 The reference interpreter

`Sv.Rules.Reference` executes the Layer IR directly: one layer over the whole world, then the next; scalar; no fusion, no masks, no SIMD, no card-mask skipping. It is slow and obviously right. It is the meaning of a pack.

**The main test of the project:** for random packs, random worlds and random sends, `compiled == interpreted` on every cell, every next-frame record and every event, frame after frame. A failure shrinks to the smallest pack and world that still disagree.

### 12.2 Laws as tests (each is a property over generated inputs)

| Law | Property |
|---|---|
| T1 | `Seal(shuffle(M)) == Seal(M)`; `Seal(Seal(M)) == Seal(M)`; `Seal(A + B) == Seal(Seal(A) + Seal(B))` |
| T2 | any split of chunks over 1, 2, 4, 7, 11 workers gives one digest and one event stream |
| T3 | a target's run cut at any points and summed in any order gives the same delta |
| T4 | no record deeper than the limit ever exists; records caused by one root never exceed the bound |
| T5 | `fused == unfused` on every cell |
| T6 | `Rewind(e)` then `Replay(now)` equals the straight run, digest by digest; `Rewind` then `SaveChunk` equals the bytes saved at e |
| T7 | scalar, 128-bit, 256-bit and Burst give one digest; the ISA legs of the existing gate script [FACT: `eng/isa-legs.sh`] |
| A6 | Commit equals `IntrinsicLaw.Step` on the lattice {MIN, -1, 0, 1, MAX} x crossed bounds x NaN and infinite stat bounds |
| Gate | Admit equals "highest priority applicable rule" computed naively; Shape equals `StatMath.Fold` on the same entries |
| Overflow | totals per (target, channel) are equal before and after every merge level |
| Codec | `LoadChunk(SaveChunk(c)) == c`; a chunk image is equal for two worlds with different pool histories |

### 12.3 Rule-set simulation

Scenario files run on the interpreter, on compiled .NET, in the browser and in Unity, and must print one digest. The worked example of the brief is scenario 1 (health 95; S2 holds two depth-1 records next frame). Every example sentence of R8-R10 is a scenario with its expected numbers.

### 12.4 Zero allocation and host identity

- .NET: 0 managed bytes over 2,000 warm frames, workers included; 0 native allocation calls.
- Unity: main thread 0 B; every Burst probe 1; delta computed into a local before asserting [FACT: the CoreCLR editor boxes the assert arguments].
- FMA: grep on source and on Burst assembly, with a `FloatMode.Fast` positive control [FACT].
- Cross-host: one pinned digest per scenario on .NET, the Unity editor and a Burst player.

### 12.5 How numbers are taken

Owner's machine; 5 fresh processes per arm, ABAB; 32 warm-up frames; median of process medians with range, plus the max and p99 frame (the first frame of every epoch copies every hot lane, and a median hides it); Burst on, safety checks, job debugger and leak detection off; each line carries entities, records per frame, rules, workers, ISA leg and the stage split [FACT: the method of the previous plan]. A second agent reruns every gate from a fresh clone.

## 13. Source examples

Rules for all code: no comments; names carry the meaning; data and behaviour are separate; static functions over plain structs; every function total. Core and generated code are C# 9. None of this was compiled; M1-M4 are where it must compile (section 14). The pack used throughout is the worked example.

### 13.1 Generated: `Values.g.cs`

```csharp
namespace Game.Sv
{
    public enum Stat : ushort { MaxHealth = 0, MoveSpeed = 1 }

    public enum Intrinsic : ushort { Health = 2, Kind = 3, Owner = 4, SniperWard = 5, Thorns = 6, Leech = 7 }

    public enum Channel : byte { Physical = 0, Poison = 1, Bleeding = 2, Healing = 3 }

    public enum Event : ushort { LowHealth = (8 << 5) | 0 }

    public enum Kind : byte { None = 0, Sniper = 1, Rifle = 2 }

    public static class Layout
    {
        public static readonly int LanesPerChunk = 9;

        public static int LaneOf(Stat stat) => (int)stat;

        public static int LaneOf(Intrinsic intrinsic) => (int)intrinsic;

        public static int LaneOf(Event raised) => (int)raised >> 5;

        public static int WordOf(Event raised) => (int)raised & 31;
    }
}
```

- **An enum number is the lane index, and it never changes.** Stats, intrinsics and event bit-lanes draw from one counter stored in the pack; a number is never reused. Adding a stat therefore moves nothing. An event's number is `lane * 32 + word`. (Corrected 2026-11-12: the first version added a `Stats` count to every intrinsic id, so adding a stat would have moved every intrinsic lane.)
- Enum members are constants, and C# copies constants into every calling assembly. That is harmless only because of the rule above. Anything that does change between pack builds (`LanesPerChunk`, counts, `PackHash`) is `static readonly` or travels in `WorldView`.
- The file also emits `const ulong CompiledAgainst` next to `static readonly ulong PackHash`. A stale caller holds an old copy of the first; the world compares the two at creation.

### 13.2 Hand-written core: the record and its laws

```csharp
using System.Runtime.InteropServices;

namespace Sv
{
    public enum Origin : byte { Direct = 0, Timeline = 1, Reflected = 2, Triggered = 3 }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public readonly struct Effect
    {
        public readonly ulong Key;
        public readonly int Amount;
        public readonly int Hits;

        public Effect(ulong key, int amount, int hits)
        {
            Key = key;
            Amount = amount;
            Hits = hits;
        }
    }

    public static class EffectKey
    {
        public const int SlotBits = 22;
        public const int SlotMask = (1 << SlotBits) - 1;
        public const int NoSource = SlotMask;
        public const int DepthLimit = 3;

        public static ulong Pack(int target, int source, byte kind, byte channel, Origin origin, int depth) =>
            ((ulong)(uint)(target & SlotMask) << 42)
            | ((ulong)(uint)(source & SlotMask) << 20)
            | ((ulong)kind << 12)
            | ((ulong)channel << 4)
            | ((ulong)((byte)origin & 3) << 2)
            | (ulong)(uint)(depth & 3);

        public static int Target(ulong key) => (int)(key >> 42);

        public static int Source(ulong key) => (int)(key >> 20) & SlotMask;

        public static byte Kind(ulong key) => (byte)(key >> 12);

        public static byte Channel(ulong key) => (byte)(key >> 4);

        public static Origin OriginOf(ulong key) => (Origin)((key >> 2) & 3);

        public static int Depth(ulong key) => (int)(key & 3);

        public static int Chunk(ulong key) => (int)(key >> 48);

        public static int Row(ulong key) => (int)(key >> 42) & 63;
    }

    public static class EffectLaw
    {
        public const long TermLimit = 1L << 32;

        public static long Term(int taken, int hits)
        {
            var product = (long)taken * hits;
            return product > TermLimit ? TermLimit : product < -TermLimit ? -TermLimit : product;
        }

        public static bool Precedes(in Effect first, in Effect second) =>
            first.Key < second.Key || (first.Key == second.Key && first.Amount < second.Amount);
    }

    public static unsafe class Seal
    {
        public static int Merge(Effect* sorted, int count, Effect* canonical)
        {
            var written = 0;
            var at = 0;
            while (at < count)
            {
                var key = sorted[at].Key;
                var amount = sorted[at].Amount;
                var hits = 0L;
                while (at < count && sorted[at].Key == key && sorted[at].Amount == amount)
                {
                    hits += sorted[at].Hits;
                    at++;
                }

                canonical[written] = new Effect(key, amount, IntrinsicLaw.Saturate(hits));
                written++;
            }

            return written;
        }
    }
}
```

`Merge` may write over its own input: `written` never passes `at`.

### 13.3 Hand-written core: lanes

```csharp
namespace Sv
{
    public unsafe struct WorldView
    {
        public byte** Segments;
        public uint* LaneOf;
        public uint* EpochOf;
        public int LanesPerChunk;
        public uint Epoch;
    }

    public readonly struct JournalEntry
    {
        public readonly long TableIndex;
        public readonly uint Previous;

        public JournalEntry(long tableIndex, uint previous)
        {
            TableIndex = tableIndex;
            Previous = previous;
        }
    }

    public unsafe struct LaneWriter
    {
        public uint* Free;
        public int FreeCount;
        public JournalEntry* Journal;
        public int JournalCount;
    }

    public static unsafe class Lanes
    {
        public const int Rows = 64;
        public const int Bytes = 256;
        public const int SegmentShift = 13;
        public const uint SegmentMask = (1u << SegmentShift) - 1u;

        public static byte* At(in WorldView world, uint lane) =>
            world.Segments[lane >> SegmentShift] + (long)(lane & SegmentMask) * Bytes;

        public static long IndexOf(in WorldView world, int chunk, int lane) => (long)chunk * world.LanesPerChunk + lane;

        public static int* Ints(in WorldView world, int chunk, int lane) => (int*)At(world, world.LaneOf[IndexOf(world, chunk, lane)]);

        public static float* Floats(in WorldView world, int chunk, int lane) => (float*)At(world, world.LaneOf[IndexOf(world, chunk, lane)]);

        public static int Read(in WorldView world, int slot, int lane) => Ints(world, slot >> 6, lane)[slot & 63];

        public static byte* Writable(in WorldView world, ref LaneWriter writer, int chunk, int lane)
        {
            var index = IndexOf(world, chunk, lane);
            var current = world.LaneOf[index];
            if (world.EpochOf[current] == world.Epoch)
            {
                return At(world, current);
            }

            writer.FreeCount--;
            var fresh = writer.Free[writer.FreeCount];
            Memory.Copy(At(world, current), At(world, fresh), (nuint)Bytes);
            world.EpochOf[fresh] = world.Epoch;
            writer.Journal[writer.JournalCount] = new JournalEntry(index, current);
            writer.JournalCount++;
            world.LaneOf[index] = fresh;
            return At(world, fresh);
        }
    }

    public static unsafe class Mask
    {
        public static ulong Positive(int* lane)
        {
            var mask = 0ul;
            for (var row = 0; row < Lanes.Rows; row++)
            {
                mask |= (lane[row] > 0 ? 1ul : 0ul) << row;
            }

            return mask;
        }
    }
}
```

- `Writable` is the whole of copy-on-first-write and the whole of snapshotting. Rewind is the inverse walk over `JournalEntry`.
- A writer belongs to one worker. Before each chunk the job tops it up to the number of lanes the kernel declares it may write, so the free list cannot run dry inside a kernel. Template lanes carry epoch 0 and are never the current epoch, so they are never written.
- `Mask.Positive` is the scalar reference. Burst vectorises it; the .NET front door is a `Vector256` compare and move-mask with the same result (T7).

### 13.4 Authored: the three cards of the worked example (`rules.sv.json`, rows only)

```json
[
  { "id": 4101, "name": "sniper_ward", "card": "SniperWard", "layer": "gate", "priority": 100,
    "when": { "eq": ["record.kind", "Sniper"] }, "then": [ { "deny": {} } ] },

  { "id": 4102, "name": "thorns", "card": "Thorns", "layer": "emit", "depthLimit": 0,
    "when": { "in": ["record.channel", ["Physical", "Poison", "Bleeding"]] },
    "then": [ { "emit": { "to": "Source", "channel": "same", "amount": "Taken", "per": "Record" } } ] },

  { "id": 4103, "name": "leech", "card": "Leech", "layer": "emit",
    "when": { "gt": [ { "taken": ["Physical", "Poison", "Bleeding"] }, 0 ] },
    "then": [ { "emit": { "to": "Self", "channel": "Healing", "amount": 5, "per": "Source" } } ] },

  { "id": 4001, "name": "health_flow", "layer": "flow",
    "then": [ { "add": ["Health", "Healing"] }, { "subtract": ["Health", "Physical", "Poison", "Bleeding"] } ] },

  { "id": 4002, "name": "health_bounds", "layer": "fold", "value": "Health", "floor": 0, "ceiling": { "stat": "MaxHealth" } },

  { "id": 4201, "name": "low_health", "layer": "event", "event": "LowHealth",
    "enter": { "below": ["Health", "MaxHealth", 30, 100] }, "stay": { "below": ["Health", "MaxHealth", 40, 100] } }
]
```

### 13.5 Generated: `Kernels.g.cs` for that pack

```csharp
using Sv;

namespace Game.Sv
{
    public static unsafe class Kernels
    {
        const ulong Damage = (1ul << (int)Channel.Physical) | (1ul << (int)Channel.Poison) | (1ul << (int)Channel.Bleeding);

        public const int LanesWrittenPerChunk = 2;

        public static ulong Decide(in WorldView world, int chunk, Effect* effects, int count, long* healthDelta, ref EffectWriter next)
        {
            var sniperWard = Mask.Positive(Lanes.Ints(world, chunk, Layout.LaneOf(Intrinsic.SniperWard)));
            var thorns = Mask.Positive(Lanes.Ints(world, chunk, Layout.LaneOf(Intrinsic.Thorns)));
            var leech = Mask.Positive(Lanes.Ints(world, chunk, Layout.LaneOf(Intrinsic.Leech)));
            var kinds = Lanes.Ints(world, chunk, Layout.LaneOf(Intrinsic.Kind));
            var touched = 0ul;
            var at = 0;
            while (at < count)
            {
                var target = EffectKey.Target(effects[at].Key);
                var row = target & 63;
                var self = 1ul << row;
                var delta = 0L;
                while (at < count && EffectKey.Target(effects[at].Key) == target)
                {
                    var source = EffectKey.Source(effects[at].Key);
                    var damageFromSource = 0L;
                    while (at < count && EffectKey.Target(effects[at].Key) == target && EffectKey.Source(effects[at].Key) == source)
                    {
                        var effect = effects[at];
                        at++;
                        var channel = EffectKey.Channel(effect.Key);
                        var denied = (sniperWard & self) != 0 & EffectKey.Kind(effect.Key) == (byte)Kind.Sniper;
                        var taken = denied ? 0 : effect.Amount;
                        var term = EffectLaw.Term(taken, effect.Hits);
                        var isDamage = ((Damage >> channel) & 1ul) != 0;
                        delta += isDamage ? -term : channel == (byte)Channel.Healing ? term : 0L;
                        damageFromSource += isDamage ? term : 0L;
                        var reflects = isDamage & (thorns & self) != 0 & taken != 0 & source != EffectKey.NoSource & EffectKey.Depth(effect.Key) == 0;
                        if (reflects)
                        {
                            next.Send(EffectKey.Pack(source, target, (byte)kinds[row], channel, Origin.Reflected, 1), taken, effect.Hits);
                        }
                    }

                    delta += (leech & self) != 0 & damageFromSource > 0 ? 5L : 0L;
                }

                healthDelta[row] = delta;
                touched |= self;
            }

            return touched;
        }

        public static ulong Commit(in WorldView world, ref LaneWriter writer, int chunk, long* healthDelta, ulong touched)
        {
            var health = (int*)Lanes.Writable(world, ref writer, chunk, Layout.LaneOf(Intrinsic.Health));
            var maxHealth = Lanes.Floats(world, chunk, Layout.LaneOf(Stat.MaxHealth));
            var changed = 0ul;
            var rows = touched;
            while (rows != 0)
            {
                var row = Bits.Tzcnt(rows);
                rows &= rows - 1;
                var before = health[row];
                var after = IntrinsicLaw.Step(before, IntrinsicLaw.Saturate(healthDelta[row]), 0, Clamp.BoundOf(maxHealth[row]));
                health[row] = after;
                healthDelta[row] = 0L;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

        public static ulong LowHealthEdges(in WorldView world, ref LaneWriter writer, int chunk, ulong changed)
        {
            var health = Lanes.Ints(world, chunk, Layout.LaneOf(Intrinsic.Health));
            var maxHealth = Lanes.Floats(world, chunk, Layout.LaneOf(Stat.MaxHealth));
            var enter = 0ul;
            var stay = 0ul;
            var rows = changed;
            while (rows != 0)
            {
                var row = Bits.Tzcnt(rows);
                rows &= rows - 1;
                var whole = Clamp.BoundOf(maxHealth[row]);
                enter |= (Condition.Below(health[row], whole, 30, 100) ? 1ul : 0ul) << row;
                stay |= (Condition.Below(health[row], whole, 40, 100) ? 1ul : 0ul) << row;
            }

            var lane = Layout.LaneOf(Event.LowHealth);
            var word = Layout.WordOf(Event.LowHealth);
            var state = ((ulong*)Lanes.Ints(world, chunk, lane))[word];
            var next = (state & ~changed) | (Condition.Next(state, enter, stay) & changed);
            var edges = Condition.Edges(state, next);
            if (edges != 0)
            {
                ((ulong*)Lanes.Writable(world, ref writer, chunk, lane))[word] = next;
            }

            return edges;
        }
    }
}
```

- Read it top to bottom: masks for the chunk, then target, then source, then record. That is the canonical order, visible as three nested loops.
- Every decision is a `select`; the only branch with a side effect is the send.
- The trace of section 5.8 is this code run by hand: delta = -20 - 5 + 5 + 15 = -5, health 95.
- A real pack's `Decide` starts with `switch (channel)` into per-channel methods, and its delta is one lane per written intrinsic; this pack is small enough to show whole.
- `Damage >> channel` is correct only while the pack has at most 64 channels: C# masks a shift count to 6 bits, so channel 64 would read bit 0. Above 64 channels the emitter uses `switch`. The emitter never shifts by a value it cannot bound.

### 13.6 Generated: the Unity job that calls it

```csharp
using Sv;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Game.Sv
{
    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct DecideJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public WorldView World;
        [NativeDisableUnsafePtrRestriction] public Effect* Effects;
        [NativeDisableUnsafePtrRestriction] public int* Start;
        [NativeDisableUnsafePtrRestriction] public int* ActiveChunks;
        [NativeDisableUnsafePtrRestriction] public long* Delta;
        [NativeDisableUnsafePtrRestriction] public ulong* Touched;
        [NativeDisableUnsafePtrRestriction] public EffectWriter* Writers;

        public void Execute(int index)
        {
            SvProbe.Mark();
            var chunk = ActiveChunks[index];
            var first = Start[chunk];
            Touched[index] = Kernels.Decide(World, chunk, Effects + first, Start[chunk + 1] - first, Delta + (long)index * Lanes.Rows, ref Writers[index]);
        }
    }
}
```

One writer per active chunk: the output of a chunk is owned by that chunk, never by "whichever worker ran it". That is what makes the next arena independent of scheduling.

Schedule it in contiguous ranges of at least 16 indices. Neighbouring `Touched[index]` slots are 8 bytes each; written by different workers they would share a cache line.

### 13.7 Generated: tl glue for one channel

```csharp
using Tl;

namespace Game.Sv.Glue
{
    [TlState] public partial struct PoisonOut { public int Value; }

    [TlKind]
    public struct PoisonFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref PoisonOut outbox) => outbox.Value += total;
    }

    public struct PoisonClip { public int Amount; }

    public struct PoisonTrack : IBlend<PoisonClip>
    {
        public void Blend(in PoisonClip first, in PoisonClip second, float factor, out PoisonClip result) => result = first;
    }

    public struct PoisonTick : ITrack<PoisonTrack, PoisonClip>
    {
        [Feeds(typeof(PoisonFeed))]
        public static int Evaluate(in Frame<PoisonTrack, PoisonClip> frame) => frame.Clip.Amount;
    }
}
```

The shapes follow the tl sources in appendix E; the agent checks them against tl main before M9.

### 13.8 Hand-written: one law as a test (net10 test project)

```csharp
public sealed class SealLaws
{
    static readonly Gen<Effect> AnyEffect =
        from target in Gen.Int[0, 255]
        from source in Gen.Int[0, 255]
        from kind in Gen.Byte[0, 3]
        from channel in Gen.Byte[0, 3]
        from amount in Gen.Int[-50, 50].Where(value => value != 0)
        from hits in Gen.Int[1, 4]
        select new Effect(EffectKey.Pack(target, source, kind, channel, Origin.Direct, 0), amount, hits);

    [Fact]
    public void Seal_depends_only_on_the_multiset() =>
        (from effects in AnyEffect.Array[0, 2048]
         from shuffled in Gen.Shuffle(effects)
         select (effects, shuffled))
        .Sample(pair => Canonical.Bytes(pair.effects).SequenceEqual(Canonical.Bytes(pair.shuffled)));
}
```

Small ranges on purpose: collisions are where the merge can be wrong. CsCheck member names are from memory and unverified.

### 13.9 File layout

```
sv/
  contract/CONTRACT-v3.0.0.md
  src/Sv.Core/        World.cs  Lanes.cs  LanePool.cs  Journal.cs  Effect.cs  Arena.cs  Seal.cs  Route.cs
                      Fold.cs (IntrinsicLaw, kept)  Clamp.cs (kept)  Condition.cs (kept)  CellDigest.cs (kept)
                      ChunkImage.cs  InputLog.cs  Replay.cs  Stats/ (today's Board stat machine, moved)
  src/Sv.Rules/       Pack/ (Schema, Rule, Expression)  Passes/ (01_Bind .. 10_Validate2)
                      Emit/ (Values, Kernels, Glue, Jobs)  Reference/ (Interpreter, Trace)
  src/Sv.Pack/        Program.cs (sv pack, sv run, sv trace)  build/Sv.Pack.targets
  src/Sv.Host/        Frame.cs  Workers.cs  Net/ (Vector256 front doors)
  src/Sv.Wasm/        Exports.cs
  packages/com.sv.core/   synced copy of Sv.Core and Sv.Rules (sha-checked, as today)
  packages/com.sv.unity/  SvWorld.cs  SvSlot.cs  SvBeginSystem.cs  SvEndSystem.cs  SvCollectJob.cs  Editor/
  tools/sv-web/       the designer and simulator (static site + Sv.Wasm)
  packs/reference/    values, rules, cards, templates, scenarios (the worked example lives here)
  tests/Sv.Laws.Tests  Sv.Diff.Tests  Sv.Scenario.Tests  Sv.NorthStar3  (old suites untouched)
  benchmarks/Sv.Bench/ Seal1m  Decide1m  Commit1m  Frame1m  Rewind1m  PackCompile10k
```

## 14. Milestones, gates, kill rules

One milestone = a few PRs, each with receipts (commands, counts, digests, numbers with settings). A step that trips its kill rule is reverted or downgraded as written, not tuned for more than one day.

| M | Deliverable | Gate [TARGET] | Kill rule |
|---|---|---|---|
| M0 | `CONTRACT-v3.0.0.md`; owner decisions D1-D10 answered | owner approval | - |
| M1 | layout, lanes, pool, table, `ChunkImage`, digest | codec laws green; reference pack at 1M entities <= 200 B per entity at rest; a 64-row loop through the table within 10 % of a flat array | indirection costs more than 10 % in `Commit1m` -> identity table (dense), report the memory |
| M2 | `Effect`, arenas, Seal, overflow merge | T1 laws green; Seal <= 0.4 ms per 1M pre-bucketed records and <= 3 ms per 1M random records, 11 workers | above 2x the target -> counting sort by row inside the chunk; still above -> stop and report |
| M3 | reference interpreter, trace, scenarios | scenario 1 prints health 95 and two depth-1 records; every sentence of R8-R10 is a green scenario | a sentence cannot be expressed -> stop, list it, ask the owner |
| M4 | compiler passes, emitter | 10,000 random (pack, world, sends) cases: 0 differences from the interpreter; a 10,000-rule pack compiles in under 60 s to under 20 MB of C# | a generated method stops being optimised (throughput halves between two partition sizes) -> halve the partition cap; C# compile over 5 min -> cold card groups run on the interpreter, report |
| M5 | .NET frame loop, workers, `Frame1m` | section 15 budgets; 0 B over 2,000 warm frames; digests equal at 1, 2, 4, 7, 11 workers | a digest differs by worker count -> stop and report the first differing lane and slot |
| M6 | epochs, journal, input log, `Rewind`, `Replay`, `Dying` | T6 green; rewind <= 1 ms per 10,000 changed lanes; ring memory within 20 % of section 9.2 | replay digest differs from the straight run -> stop and report |
| M7 | stat machine writes lanes; StatOps keep their path | soak pin `6D6C602BF07E9366` unchanged; stat lane bits equal the old sink bits on twin worlds | Board undo and lane journal disagree after rewind -> lanes win, Board state is rebuilt from a keyframe, report |
| M8 | Unity adapter v4, generated jobs, authoring window | fresh clone compiles in the newest Unity 7 with 0 `error CS` and 0 `error BC`; every probe 1; scenario digests equal .NET at 1 and 11 workers; main thread 0 B | a generated kernel does not compile under Burst -> fix the emitter, never hand-edit output |
| M9 | tl glue, Collect, requests, NorthStar3 | live = replay; .NET = Unity; tl's pinned hashes untouched; status-as-entity scenario green ("immune to snipers" drops a sniper's poison ticks) | needs a tl change beyond generated glue -> stop and report |
| M10 | web designer and simulator | scenario 1 traced in the browser with the pinned digest; a pack edited in the browser loads in Unity byte-identical | WASM build over 30 MB or first simulate over 5 s -> interpreter-only build |
| M11 | deletions of section 11.1 | all old pins green; test count only grows; source size of `packages/com.sv.unity` shrinks | a pinned scenario needs deleted code -> keep that code, report |
| M12 | reserves, only for a failed gate: (a) delta scratch for channels no rule tells apart; (b) run splitting for hot targets; (c) replay limited to chunks reachable from the changed records; (d) IL-emitted kernels for hot reload | A/B, 5 processes per arm | under 15 % gain -> revert |

**Done means, all at once:** every law of section 12.2 green on .NET (default, no-AVX2, scalar, `netstandard2.1`), the Unity editor and a Burst player; compiled equals interpreted; the worked example equals the brief; 0 B warm; section 15 gates measured, not projected; a fresh clone builds everywhere; no old test loosened; the three old pins unmoved; contract v3.0.0 vendored; a second agent reran the numbers.

**Rules for agents** (unchanged from the previous plan, they worked): one step per PR; never edit a pinned digest or an existing test body; production code has no comments; a number without its settings is rejected; take `bench.lock` then `unity.lock`; stop and report instead of deciding when a kill rule says so.

**Learning order for the owner** (R17), about one sitting each:
1. Section 2: seven axioms.
2. Section 5.8, then run scenario 1 in the web tool and click through the layers.
3. Section 13.5: the same thing as generated code, three nested loops.
4. Section 4: why a record looks the way it does.
5. Section 9.1 and `Lanes.Writable`: all of rewind is one function.

## 15. Budgets

Reference machine [FACT]: Ryzen 5 8500G, 6 cores / 12 threads, 11 workers acting as about 7 cores, about 30 GB/s streaming.

### 15.1 Cost model [PROJECTED]

| Stage | Unit cost | Basis |
|---|---|---|
| Collect | 4 B read per outbox cell of a changed chunk + 16 B written per record | streaming |
| Seal, pre-bucketed records | 1-2 ns per record (verify order, merge) | linear |
| Seal, random records | 10-20 ns per record (partition 32 B moved, counting sort by row, small sorts) | CPU bound |
| Decide, masks | 256 B read per (active chunk, lane a rule reads) | streaming |
| Decide, records | 4-8 ns per record | key decode, selects, one add |
| Commit | about 1 KB touched per (active chunk, written intrinsic); +256 B on the first write of an epoch | streaming |
| Events | 2 compares per (changed row, event that reads a changed value) | - |
| Digest | 2 mixes per changed cell, or every K frames [FACT: cadence knob exists] | - |
| Stats | O(modifier changes), main thread | [FACT: measured small today] |

### 15.2 Frame time for sv at 1,000,000 entities, 11 workers [PROJECTED]

| Scenario | Collect | Seal | Decide | Commit + Events | Digest + Stats | Total |
|---|---:|---:|---:|---:|---:|---:|
| A: 100,000 records, 10 % of chunks active | 0.1 | 0.2 | 0.2 | 0.2 | 0.3 | **1.0 ms** |
| B: 1,000,000 timeline records, every entity ticking | 0.7 | 0.3 | 1.7 | 0.9 | 0.6 | **4.2 ms** |
| C: B + 1,000,000 random cross-entity records | 0.7 | 2.3 | 2.6 | 1.1 | 0.8 | **7.5 ms** |
| D: 3,000,000 records, half random | 0.7 | 3.5 | 5.0 | 1.5 | 1.0 | **11.7 ms** |

- Gate [TARGET]: **sv <= 8.3 ms per frame at 1,000,000 entities with up to 1,000,000 records per frame** (25 % of a 30 Hz frame, the gate sv already uses). A, B pass; C passes narrowly; D does not and is what reserve M12a and M12b exist for.
- Honest comparison with today [FACT]: the fold path costs 2.35 ms in scenario B's shape. v3 projects 4.2 ms. The difference is the price of keeping source, kind and hits on every effect; the fold cannot do R6-R9 at any price.
- 60 Hz (4.2 ms): A passes, B is on the line, C and D do not. Reported, not gated (decision D6).
- [FACT] tl's own kind job is 8.45 ms at 1M rows. The whole frame does not fit 25 % of 30 Hz at 1M whatever sv does (gap G11).

### 15.3 Memory at 1,000,000 entities, shared lanes [DERIVED from layouts; activity ASSUMED]

| Item | Bytes | Note |
|---|---:|---|
| Lane table | 12.75 MB | 204 references per chunk |
| Materialized lanes, 16 per chunk | 64 MB | grows 4 MB per extra lane touched in every chunk |
| Lane epochs | 1-2 MB | 4 B per lane |
| Stat machine | 18.6 MB + 58.8 B per live modifier | [FACT: measured board bytes] |
| Arenas, C = 1,000,000 | 32 MB | raw + canonical |
| Frame scratch | 8 MB | 512 B per (active chunk, written value) |
| Journal ring, 32 epochs, 10 % activity | 40 MB | 396 MB if every chunk changes 3 lanes every epoch |
| Input log, 64 frames | 10-100 MB | unchanged segments are shared; capped by the host |
| **Typical total** | **about 210 MB** | **0.21 KB per entity, history included** |
| Dense policy, same content | 812.5 MB + ring + arenas | the literal reading of R1 + R3 |

Gate [TARGET]: at rest (no history, empty arenas) <= 200 B per entity on the reference pack at 1M; ring within 20 % of section 9.2 for the measured activity.

## 16. The seven open questions, answered

| Q | Answer | Where |
|---|---|---|
| Q1 triggers cascade? | Yes, to a depth fixed at compile time. Same-frame self triggers are ordered into strata and a cycle is a compile error. Triggers aimed at others arrive next frame with depth + 1 and stop at the limit (default 0 for reflection, world maximum 3). | 5.4 |
| Q2 events: values or messages? | Values. One kept level bit per entity per event, updated by hysteresis. The edge is derived and becomes a message and a trigger. Counters are intrinsics. | 5.6 |
| Q3 hand and shield | Separate entities with their own values and an `Owner` reference. Forwarding rows move their channels to the owner in the same frame. | 7 |
| Q4 SIMD across entities or contiguous blocks | Both: a chunk of 64 rows whose values are lanes. Conditions on state are 64-bit masks from vertical compares; only the record-dependent part is per record. | 3.4 |
| Q5 mutual reflection and immunity | A reflected record carries depth 1 and reflection does not fire on it. Mutual reflection ends in two frames, mutual immunity in one, for any combination. The remove pass is a compaction. | 5.3, 5.4 |
| Q6 input lifetime, snapshot content | A snapshot is an epoch mark; the ring holds the old lanes of 32 epochs. Inputs from outside sv are logged for the ring's span, shared when unchanged. Derived records are never stored. | 9 |
| Q7 per build or per pack; how a card adds a check | Per content pack. A card is a count on the entity; its rules are always compiled and guarded by that count; whole rule groups are skipped per chunk when nobody holds the card. Packs not compiled in run on the interpreter. | 6.2 |

## 17. Gaps, contradictions, decisions, unverified

### 17.1 Gaps and contradictions in the brief

| # | Gap | What I did |
|---|---|---|
| G1 | R1 + R3 (all 300 values, contiguous, per entity) against R16 (very low memory): the literal layout is 812.5 MB at 1M before any history | shared lanes keep R1 exactly and R3's purpose (bulk copy, SIMD), not its letter; dense remains one policy (D1) |
| G2 | "how much it consumed" (R6) is not defined | computed `Taken` and `NotTaken`; no stored field |
| G3 | "every damage you take, you heal 5": per frame, source, record or hit is not said | a field of the rule; the example holds for frame and source |
| G4 | R9 says an immune entity can still reflect; the worked example reflects only what was taken | `amount = Taken / Offered / NotTaken` on the rule |
| G5 | tl has no source per playback, and a kind sums playbacks | status as an entity; a tl tag would be cleaner but is a tl change |
| G6 | lag compensation that re-runs AI and tl needs their own rewind; sv cannot provide it | two replay modes; sv-only is the default |
| G7 | R2 says a damage type may be a *stat*. A record aimed at a stat is a modifier, which is today's `StatOp` (24 B, sparse). Gating StatOps by source ("immune to slows") is not designed here | StatOps keep their path in M7; an Admit gate on them is a small follow-up, not in the gates above |
| G8 | amounts are integers; nothing says how fine | the designer picks the unit per intrinsic (health in 1/100 if needed) |
| G9 | event messages carry (slot, event, entered) only | richer payloads are not designed |
| G10 | a record aimed at a `Dying` or free slot | dropped in Seal, counted in `DroppedDead` |
| G11 | the 8.3 ms gate covers sv; tl's kind job alone is 8.45 ms at 1M | reported; not sv's to fix |
| G12 | "what that source is" | one `Kind` intrinsic per entity, copied into the record at send time |
| G13 | Unity side "C# 9 / Burst" while tl.unity raises the language version per asmdef | I hold sv to C# 9 everywhere Unity compiles |

### 17.2 Assumptions that carry weight

| # | Assumption | If false |
|---|---|---|
| S1 | a record's type names a channel, and channels are per-frame totals with fixed offsets | channels become persistent intrinsics; Commit writes them; memory and journal grow by the active channel lanes |
| S2 | one frame of delay between entities is acceptable everywhere except forwarding | same-frame cross-entity resolution needs a fixpoint and breaks T2; the design would change at the root |
| S3 | about 16 of 204 lanes are non-default in a typical chunk | section 15.3 scales linearly; at 100+ the dense policy is simpler and equal |
| S4 | at most 256 kinds, 256 channels, 4,194,304 slots | 24-byte record (D2) |
| S5 | designers accept tables with a graph view | a graph front end writes the same rows |
| S6 | Burst vectorises the scalar mask and select loops | hand-written `Unity.Burst.Intrinsics` front doors, same bits |

### 17.3 Owner decisions

| # | Decision | My recommendation |
|---|---|---|
| D1 | storage policy: shared lanes or dense identity table | shared |
| D2 | record width: 16 B with the limits of S4, or 24 B | 16 B |
| D3 | ring: 32 epochs of how many frames | 2 frames at 30 Hz |
| D4 | sum, then clamp once, as the meaning of a frame | keep (clause 19) |
| D5 | replay mode, and what the game does with a retracted event | sv-only; game decides per event |
| D6 | the gate: 8.3 ms at 1M entities and 1M records; is 60 Hz required | gate 30 Hz, report 60 Hz |
| D7 | approve contract v3.0.0 | - |
| D8 | sourced timeline effects: status entity, or ask tl for a playback tag | status entity now |
| D9 | default capacity of an outward event channel | 16,384 [FACT: today's default] |
| D10 | arena full: grow once and rerun, or lossy merge at a hard cap | grow on servers and editor; hard cap in the browser |

### 17.4 Not verified

- Every [PROJECTED] number. Nothing here was measured.
- That any code in section 13 compiles. It follows the appendices' APIs as printed; the tl attribute and interface shapes and the CsCheck member names in particular.
- That Burst 2.0 turns the mask loops into compares and move-masks, and keeps `FloatMode.Strict` free of FMA in the generated kernels (the grep of section 12.4 proves or refutes it).
- That the C# compiler and Burst stay fast on 20 MB of generated source (M4's kill rule).
- That 2 MB pool segments and the table indirection cost under 10 % (M1's kill rule).
- That the Board's undo log and the lane journal agree after a rewind (M7).
- That tl's generated job leaves a change version the Collect filter can trust for outbox columns [FACT says false positives only, never misses; I rely on it].
- The share of lanes that stay at default in a real game (S3). It decides D1 more than anything I can argue.
- Where I guessed: the meaning of "consumed" (G2), the unit of card C (G3), channels as transient totals (S1), and that "checks between layers" means chains plus engine invariants (5.7).


## Manager addendum, 2026-11-12 (appended by the manager; NOT the senior's text)

- D11 (owner): stats are integers with a designer-chosen decimal scale per value, percentages in basis points (10,000 = 100 %), int64
  intermediates, truncating division, saturation to int32; no float state anywhere. Replaces the float `BoundOf`/`StatMath` use in Shape and the
  stat machine (sections 5.2, 11.1, 13.5). The legacy float machine in `src/Sv` and its pins stay untouched until M11. Contract v3.0.1.
- Agents wrote wave 1 and C3 from this plan; their readings of its gaps are in issues 0160, 0170, 0180, 0210. Where this plan and those readings
  differ, the repo code follows the issue, and the owner confirms or changes each.
- Lessons from the For Honor modifiers talk to fold into the design, pending owner OK: origin layers for stacking (core before external),
  attacker/victim modifier snapshots for damage-over-time, minimal trace viewer early, bulk export/import, status effects with power levels,
  keep the rule language simple. Full list: `D:\GitHub\_work\HANDOFF-sv-v3.md` section 8.
- Status of code and the resume plan: `D:\GitHub\_work\HANDOFF-sv-v3.md`.
