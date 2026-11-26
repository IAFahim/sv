# sv v4 columns: the guide

sv v4 (`src/Sv.Columns`, Unity package `com.sv.columns`) does one thing: it folds many integer contributions per entity into named lanes, once per frame, in a fixed order, with the same result on every machine, thread count and instruction set. It is a preview in the v3.0.0 tag: its API may change before 4.0.0, and v3 games do not need it.

This file is the course: what v4 is for, a program you can run, the host API, Unity, and how a v3 pack maps onto it. The law itself is `docs/v4-spec.md`; the reasons and the numbers are `docs/v4-design.md`.

## 1. When to use v4, and when v3

| You need | Use |
|---|---|
| standing effects on many entities (damage over time, regen, auras, counters), plus one-off hits, at up to a million entities per frame | v4 |
| a rule that reacts to each hit (thorns, lifesteal, per-hit absorb, rolls), statuses as entities, rewind and late input | v3 |

v4 is fast because effects that live with a template or card are compiled into column kernels and never become records: 1,000,000 entities x 500 ops take 22 ms on 4 cores (v3: about 15 s as records), and 20.3 ms in Unity on jobs. One-off hits are 8-byte records reduced into the same per-entity sums before the same fold.

## 2. Run it (5 minutes)

`samples/Sv.Columns.Hello` is a complete program. CI runs it on Linux x64, Linux ARM64, macOS and Windows, with and without AVX2, and checks it prints exactly `samples/Sv.Columns.Hello/expected.txt`:

```sh
dotnet run -c Release --project samples/Sv.Columns.Hello
```

```
frame 1: knight 100 hp  8 shield 0 burning | cleric  48 hp | warded 100 hp | entered - | dropped 0 | digest A33AAC6BBA86AC82
frame 2: knight  13 hp  0 shield 2 burning | cleric  51 hp | warded 100 hp | entered knight Low | dropped 0 | digest 5E481CA782A9E1B2
frame 3: knight   8 hp  0 shield 1 burning | cleric  54 hp | warded 100 hp | entered - | dropped 0 | digest 60DE921E9A0305DB
frame 4: knight   3 hp  0 shield 0 burning | cleric  57 hp | warded 100 hp | entered - | dropped 1 | digest 192C6EBD085898FD
frame 5: knight   0 hp  0 shield 0 burning | cleric  60 hp | warded 100 hp | entered knight Dead | dropped 1 | digest 303628A7402FCA4C
frame 6: knight   0 hp  0 shield 0 burning | cleric  63 hp | warded 100 hp | entered - | dropped 1 | digest B21C3D2264B0B4D2
```

What happened, frame by frame (the pack is `samples/Sv.Columns.Hello/pack.json`):

1. The wolf bites the knight for 12 Physical: the knight's 20 shield absorbs it (`absorb` rule), 8 shield left. The wolf also bites the warded unit, whose `BeastWard` lane denies Physical from the `Beast` kind (`deny` rule), so nothing lands. A sword with no source hits the cleric for 15; the cleric's `regen` card heals 3 in the same frame: 60 - 15 + 3 = 48.
2. The game sets the knight's `Burning` lane to 3, which turns on the `burning` card: 5 Fire and 1 tick off `Burning` per frame. A sword hits the knight 3 times for 30 (`hits: 3`). Physical 90 and Fire 5 make a pool of 95; the last 8 shield is drawn from Physical first (the order the `absorb` rule lists): 100 - 82 - 5 = 13. 13 is below 30% of 100, so `Low` enters.
3. and 4. Burning ticks 5 a frame and runs out. In frame 4 the game sends a record to slot 100,000, which is outside the world: it is refused and counted in `Dropped`, and it hits nobody.
5. The wolf bites again; `Health` hits its floor of 0 and `Dead` enters. The cleric keeps healing toward `MaxHealth`.

The digest hashes every lane of every live entity. Two machines that print the same digest hold the same world.

## 3. The pack

A pack is plain data: lanes (named int32 values with defaults), channels (kinds of contribution), source kinds, cards (ops that apply while a lane is above 0), templates (lane values, cards, ops) and rules, applied in this order every frame:

| Rule | Does |
|---|---|
| `deny`, `denyByMask` | the target refuses a channel from some source kinds while a condition holds, or by bits of a lane |
| `scale` | multiplies a channel by a small integer while a condition holds on the target, the source, or both |
| `convert` | moves a fraction (power-of-two denominator) of one channel's sum into another |
| `absorb` | a budget lane soaks up the listed channels, in the order listed |
| `flow` | adds or subtracts channel sums into lanes |
| `bounds` | floor and ceiling, constants or other lanes |
| `event` | a named level with enter and stay conditions; `entered` is true on the frame it starts |

Conditions are text: `"Health < 30% of MaxHealth and not event Dead"`. Every limit is checked when the pack compiles, and every problem is reported with its place (`pack.rules[3].convert: ...`). `packs/columns/arena.json` is the worked example with all of these; the full grammar and limits are in `docs/v4-spec.md` sections 1 and 7.

## 4. The host API

```csharp
using var pack = PackJson.Read(json).Compile();
using var world = new ColumnWorld(pack, capacity: 65_536, writerCount: 4, resolveParts: 64);

var knight = world.Spawn(pack.Template("knight"));
world.Set(knight, pack.Lane("Burning"), 3);
world.Writer(0).Send(knight, pack.Channel("Physical"), 30, hits: 3);
world.Writer(1).SendFrom(wolf, knight, pack.Channel("Physical"), 12);
world.Step();
var hp = world.Get(knight, pack.Lane("Health"));
```

- Look names up once and keep the indexes; every call after that is integer only and allocates nothing.
- `Send(target, channel, amount, hits)` is a hit with no source. `SendFrom(source, target, channel, amount, hits)` carries the source's kind and its scale flags, read when you send. The names differ on purpose: one name with both shapes lets a four-`int` call silently take the wrong one.
- One writer per thread. Records sent before `Step` are consumed by it; their order, and how they are split across writers, never changes the result.
- Spawn, despawn and set only between frames; during a frame they throw. Slots live in 64-entity chunks of one template, so keep the slot in your own entity data.
- `Event(slot, e)`, `Entered(slot, e)` read events per entity; `ChunkAlive(chunk)` and `ChunkEntered(chunk, e)` give 64 entities as one bit mask.
- `Dropped` counts records that hit nobody: a target outside the world, a channel past 15, a dead target, or a (channel, kind) the pack does not declare.
- `Step()` runs on one thread. `new ColumnRunner(world, new Workers(n)).Step()` (`Sv.Host`) runs the reduces and resolves on `n` worker threads with the same result.

## 5. Unity

```json
"com.sv.columns": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.columns#v3.0.0"
```

Unity 7 (CoreCLR), no package dependencies. `ColumnHost` (a MonoBehaviour) opens a world from a JSON `TextAsset`, steps it in `LateUpdate`, `FixedUpdate` or on demand, raises `Stepped` and disposes everything with its object. `ColumnStepper(world).Schedule(dependsOn)` runs a frame on the job system. The package is `src/Sv.Columns` copied verbatim (`eng/columns-sync.sh`), and its EditMode tests assert the same pinned digest as .NET on every kernel, on jobs and through `ColumnHost`.

## 6. From v3 to v4

| v3 | v4 |
|---|---|
| value (`values.sv.json`) | lane |
| channel, kind | channel, source kind (up to 16 channels, 4 kinds) |
| a status that ticks (a timeline sending records) | a card: a lane that switches it on, ops per frame, a timer channel that flows back into the lane (the sample's `burning`) |
| card with stat modifiers | card ops, or template ops |
| gate, immunity | `deny`, `denyByMask` |
| shield | `absorb` |
| crit or execute multiplier | `scale` (integer factor; with `sourceWhen` it reads the attacker) |
| convert a share of damage into healing | `convert` |
| `FrameLoop.Send(target, source, kind, channel, amount, hits)` | `SendFrom(source, target, channel, amount)` (the source's kind comes from its kind lane) |
| digest, determinism across machines | the same, `ColumnWorld.Digest()` |

What does not port: rules that react to each record (thorns, lifesteal, reflections, per-hit absorb, rolls), statuses as entities, rewind and late input. Keep those in v3 or in game code. A v4 pack and a v3 pack are different files; nothing converts one into the other yet.

## 7. Sharp edges

- **Integers only.** Lanes are int32; sums are int64 and saturate to int32 once, when stored. `convert` rounds down once per entity per frame, on the sum.
- **Frame-start reads.** Gates, scale, convert and op conditions read the lanes as they were when the frame started: a hit that drops health below 30% turns on a low-health op or scale next frame, not this one. Events are the exception: they read the lanes just stored, so `Low` enters on the frame of the hit (sample frame 2).
- **Big record amounts saturate.** `amount * hits` is stored exactly up to plus or minus 2^27 - 1 per record, then saturates.
- **No journal.** Replay is the starting world plus every frame's records and edits, run again; v4.0 keeps no history of its own.
- **Preview.** The API and the pack format may change before 4.0.0; each change will be in `CHANGELOG.md`.
