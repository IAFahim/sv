# sv v4.0 columns: specification

Status: normative for `src/Sv.Columns` and `Sv.Host.ColumnRunner`. When it is accepted it moves to `contract/` as a new file; the v3 contracts stay as they are. The decisions behind it, with reasons, are in `docs/v4-design.md` section 8; a tutorial with a runnable program is `docs/v4-guide.md`.

v4 does one thing: it folds many integer contributions per entity into lanes, once per frame, in a fixed order, with the same result on every machine, thread count and instruction set. Contributions come from two places, and both meet in the same per-row class sums and the same fold:

- **ops**: known per template or card, compiled into column kernels;
- **records**: sent per frame by game code, 8 bytes each, reduced at send into the class sums.

There is no per-record rule (no thorns, lifesteal, per-hit absorb or rolls). Those stay in v3 or in host logic.

## 1. Pack

A pack is plain data: `ColumnPack` in C#, or JSON read by `PackJson.Read` (section 7). `ColumnPack.Compile()` checks every limit and name and returns a `CompiledPack` or throws `PackException` listing every problem.

| Part | Meaning |
|---|---|
| lanes | named int32 values per entity, each with a default |
| channels | named kinds of contribution (damage types, healing, timers) |
| kinds | source kinds; kind 0 is the default for records without a source and for ops |
| kindLane | optional lane holding an entity's kind as a source (out of range reads as 0) |
| cards | a name, a lane, and ops that apply while that lane is above 0 |
| templates | a name, lane values, card lane values, and ops |
| rules | deny, denyByMask, scale, convert, absorb, flow, bounds, event |

### 1.1 Limits

| Limit | Value |
|---|---|
| lanes | at most 62 declared, plus the internal `@events` and `@entered` |
| channels | 1 to 16 |
| kinds | 1 to 4 (one kind named `none` is added when none are declared) |
| events | at most 31 |
| scale rules | at most 8; the product of the times that can apply to one channel is at most 16 |
| convert denominator | a power of two up to 2^16; numerator 0 to the denominator |
| op amount | at most plus or minus 32767 |
| template budget | sum of absolute op amounts of a template plus all cards at most 2^20 |
| bit index | 0 to 31; below percent 0 to 100 |
| capacity | at most 2^22 slots, rounded up to a multiple of 64 |
| records | at most `int.MaxValue / 2` per writer and `int.MaxValue` per frame |

Names are identifiers (letter or underscore, then letters, digits or underscores) and unique within their kind. Names starting with `@` are reserved.

### 1.2 Conditions

A condition is a conjunction of atoms. Each atom may be negated.

| Atom | Holds when |
|---|---|
| `L op N`, `L op M` | lane L compared with a constant or another lane; op is `>`, `>=`, `<`, `<=`, `==`, `!=` |
| `L < P% of M` | `L * 100 < M * P` in int64 |
| `bit L B` | bit B of lane L is set |
| `event E` | event E is at level |
| `entered E` | event E entered this frame |

In JSON a condition is text, for example `"Health < 30% of MaxHealth and not event Dead"`. `of` is optional. Atoms are deduplicated and sorted at compile time, so equal conditions share work.

### 1.3 Ops

An op is `(channel, amount, times?, when?, kind?)`. Its term is `amount` or `amount * lane[times]`, counted when `when` holds. A card op also needs the card lane above 0. Ops are grouped at compile time by (channel, kind); constant terms with the same condition fold into one, lane terms with the same lane, condition and sign fold into one.

## 2. Records

A record is one `ulong`:

| Bits | Field |
|---|---|
| 42 to 63 | target slot (22 bits) |
| 38 to 41 | channel |
| 36 to 37 | source kind |
| 28 to 35 | source flags (the source's scale source bits, section 3 step 1) |
| 0 to 27 | amount, two's complement, saturated to plus or minus (2^27 - 1) |

`RecordWriter.Send(target, channel, amount, hits)` sends with kind 0 and the pack's no-source flags. `SendFrom(source, target, channel, amount, hits)` reads the source's kind and source flags from its gate at send time; a source that is not alive reads as kind 0 with no flags. The two calls have different names on purpose: with one name, `Send(target, source, channel, amount)` and `Send(target, channel, amount, hits)` both accept four `int`s and the compiler silently picks the second. The stored amount is the exact product `amount * hits` saturated once to plus or minus (2^27 - 1); no intermediate wraps (`RecordWriter.AmountTimesHits`).

A record is dropped and counted in `ColumnWorld.Dropped` when:
- its target is outside `0 .. Capacity - 1` or its channel is outside `0 .. 15` (refused at send, counted at the next step, so such a target can never alias another slot through the 22-bit field);
- its target is not alive at the step, or its (channel, kind) is not declared (counted at reduce).

## 3. Frame law

`ColumnWorld.Step()` runs `BeginFrame`, `Reduce(w)` for every writer, `Resolve(p)` for every part, then `EndFrame`. Records sent before `Step` are consumed by it. A host may run the reduces in parallel with each other and the resolves in parallel with each other (`ColumnRunner` does this on `Sv.Host.Workers`). Nothing may send between `BeginFrame` and `EndFrame`.

Per alive row, in this order. All arithmetic is int64 unless stated; there is no per-term clamp.

1. **Accumulate.** Every term is zeroed unless the row's gate admits its (channel, kind), then multiplied by `factor(channel, mask)`, the product of the times of the scale rules in `mask` that cover the channel (1 when none). For a record, `mask = gate.Flags & recordFlags`; for an op, `mask = gate.Flags & (gate.Flags >> 8)`. Positive terms add to the channel's gain, negative terms to its loss. Records and ops use the gate and lanes as they were at the start of the frame.
2. **Convert**, in pack order: when the condition holds on the frame-start lanes, `moved = floor(gain[from] * numerator / denominator)`, then `gain[from] -= moved`, `gain[to] += moved`. The product is exact: with `denominator = 2^s`, `gain = q * 2^s + r`, the law computes `q * numerator + ((r * numerator) >> s)`, which never leaves int64 because `numerator <= denominator` and gains are never negative.
3. **Absorb**, in pack order: `pool` = sum of gain over the listed channels; `taken = budget > 0 ? min(budget, pool) : 0`; budget lane `-= taken`; `taken` is drawn from the listed channels in the order listed.
4. **Flow**, in pack order: lane `+= gain + loss` for each added channel and `-= gain + loss` for each subtracted channel.
5. **Bounds**, in pack order: floor and ceiling are read before the rule (a lane bound reads the value as of this step); the floor is applied, then the ceiling.
6. **Store**: every written lane is saturated to int32 and stored.
7. **Events**, on the stored lanes with last frame's `@events`: an event at level holds while its stay condition holds (its enter condition when it has no stay); an event not at level enters when its enter condition holds. `@entered = new & ~old`.
8. **Gate refresh**, on the stored lanes:
   - admit starts as every declared (channel, kind); each deny rule whose condition holds removes its channels times kinds; each denyByMask rule removes, by kind, bits 0 to 3 of its lane for every channel, or, by channel, every kind of each channel whose bit is set in its lane;
   - flags: bit r is scale rule r's own condition, bit 8 + r its source condition;
   - kind: the kind lane's value when it is a declared kind, else 0.

Dead rows are not changed. A spawned row starts from its template's values and a fresh gate.

## 4. Determinism

The result of a frame depends only on the world before it and the multiset of records sent for it. These do not change it, and tests hold each of them:

- the order of records and how they are split across writers;
- the number of writers and resolve parts;
- the kernels: `KernelChoice.Scalar`, `Vector`, `Best` with Vector256, and each of them with `DOTNET_EnableAVX2=0` or `DOTNET_EnableHWIntrinsic=0` give equal digests;
- the slow reference (`Sv.Columns.Reference.ReferenceWorld`), which interprets the `ColumnPack` directly, gives the same lanes.

`ColumnWorld.Digest()` hashes slot, template and every lane of every alive slot in slot order. Replay is the initial world plus the records and edits of each frame; v4.0 keeps no journal.

## 5. Host API

| Call | Does |
|---|---|
| `new ColumnWorld(pack, capacity, writers, parts, kernels)` | allocates all memory up front |
| `Spawn(template)`, `Despawn(slot)`, `IsAlive`, `TemplateOf` | slots live in 64-row chunks of one template |
| `Get(slot, lane)`, `Set(slot, lane, value)` | `Set` refreshes the gate at once |
| `Event(slot, e)`, `Entered(slot, e)`, `ChunkAlive`, `ChunkEntered` | read events per slot or per chunk |
| `Writer(w).Send(target, channel, amount, hits)`, `Writer(w).SendFrom(source, target, channel, amount, hits)` | one writer per thread |
| `Step()` or `ColumnRunner.Step()` | single thread, or reduces and resolves on `Workers` |

A world is single-owner: spawn, despawn and set only between frames. Between `BeginFrame` and `EndFrame` those three calls throw `InvalidOperationException`. After `Dispose`, or once its `CompiledPack` is disposed, every call on the world and its writers throws `ObjectDisposedException`; a second `Dispose` is safe. A chunk is listed at most once as having room for its template, so spawn and despawn churn neither grows memory nor lets a reclaimed chunk take a spawn of its old template.

A warm frame allocates 0 bytes on the managed heap. Record buffers grow by doubling; per-writer class-sum blocks are allocated in native memory the first time a writer touches a chunk.

## 6. Kernels

The scalar law (`Law`) is the definition. Two kernels compute the same thing faster; `ColumnWorld.KernelPath` says which one a world uses.

| Path | Chosen by | Does |
|---|---|---|
| `Vector256` | `Best`, when compiled for `net10.0` (`#if NET`) and AVX2 is accelerated | ops 16 rows per pass, condition masks 8 rows per pass, the fold and the reduce (runs of four records with one target) |
| `Vector` | `Vector`, or `Best` when Vector256 is not there, when `System.Numerics.Vector` is accelerated with 4 to 16 ints | ops and condition masks in `Vector<int>`; the fold and the reduce stay scalar. This is the path a netstandard2.1 build such as Unity's takes |
| `Scalar` | `Scalar`, or nothing above is accelerated | `Law` |

The `Vector` path sums a group's terms in int32. It does so only when that is exact: per chunk it takes the largest absolute value M of every lane that a template's ops multiply, and per group it needs `sum of conditional constants + sum of absolute lane amounts * M <= int.MaxValue`; otherwise that group is summed for that chunk by the scalar law. Unconditional constants and the factor are applied in int64 as in the law.

All three are checked against each other and against the reference on fixed examples, on random packs and on lanes too wide for int32 sums.

## 7. JSON

```json
{
  "lanes": [ { "name": "Health", "default": 100 } ],
  "channels": ["Fire", "Healing"],
  "kinds": ["None"],
  "kindLane": null,
  "cards": [ { "name": "regen", "lane": "RegenCard", "ops": [ { "channel": "Healing", "amount": 1, "times": "RegenRate" } ] } ],
  "templates": [ { "name": "unit", "values": { "Health": 50 }, "cards": { "regen": 1 }, "ops": [ { "channel": "Fire", "amount": 2, "when": "Health < 50% of MaxHealth" } ] } ],
  "rules": [
    { "deny": { "channels": ["Fire"], "kinds": null, "when": "FireImmune > 0" } },
    { "denyByMask": { "lane": "ImmuneMask", "byKind": false } },
    { "scale": { "channels": ["Fire"], "times": 2, "when": "Wet > 0", "sourceWhen": null } },
    { "convert": { "from": "Fire", "to": "Healing", "numerator": 1, "denominator": 2, "when": "Converter > 0" } },
    { "absorb": { "budget": "Shield", "channels": ["Fire"] } },
    { "flow": { "lane": "Health", "add": ["Healing"], "subtract": ["Fire"] } },
    { "bounds": { "lane": "Health", "floor": 0, "ceiling": "MaxHealth" } },
    { "event": { "name": "Dead", "enter": "Health <= 0", "stay": null } }
  ]
}
```

Unknown keys, fractions, duplicate keys and broken JSON are errors with their place (`pack.rules[3].convert: ...`, `json line 4 column 9: ...`). `packs/columns/arena.json` is the worked example.

## 8. Unity

`packages/com.sv.columns` (Unity 7, no package dependencies) holds `Sv.Columns` copied verbatim by `eng/columns-sync.sh sync`; `eng/columns-sync.sh check` (part of `eng/v3-verify.sh`) fails when the copy and `src/Sv.Columns` differ. Beside it:

| Type | Does |
|---|---|
| `ColumnStepper(world)` | `Schedule(dependsOn)` runs a frame on the job system: begin, one job per writer for the reduces, one per part for the resolves, end. `Step()` schedules and completes. Nothing may send, `Set`, spawn or despawn until the handle completes. |
| `ColumnHost` (MonoBehaviour) | Opens a world from a JSON `TextAsset` (or `Open(json, capacity, writers, parts, useJobs, kernels)`), steps it in `LateUpdate`, `FixedUpdate` or only on `Step()`, raises `Stepped`, and disposes everything when its object is destroyed. Resolve parts default to four per job thread. |

Game code sends records with `host.World.Writer(w).Send(...)` before the step, one writer per thread. To tie slots to entities, keep the slot in a component.

`Tests/Editor/Canon/Canon.cs` is the mirrored `tests/Sv.Columns.Tests/Canon/Canon.cs`: the .NET suite and the Unity EditMode suite run the same 48 frames on `arena.json` and assert the same pinned digest, on the scalar, vector and best kernels, in one thread, on `Workers` and on Unity jobs, and through `ColumnHost`. The Unity job `sv Unity columns` (`eng/unity-columns-ci.sh` on the self-hosted `unity` runner) compiles the package, runs those tests and prints `SVCOLUMNS` benchmark lines for the same shapes as `Sv.Columns.Bench`.
