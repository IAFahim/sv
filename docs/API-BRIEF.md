# sv: API and architecture brief for a senior engineer

Release `v3.0.0`. This document is meant to be read once, top to bottom, by someone who will judge the design. The host snippets in section 5 are excerpts of one complete program, **`samples/Sv.ApiTour/Program.cs`**, which compiles with warnings as errors; run it yourself and compare:

```sh
dotnet run -c Release --project samples/Sv.ApiTour
```

Its output sections 1 to 11 are the `text` blocks below (only the microsecond timings in section 8 differ per run). The `sv` command-line output in section 6 was produced by the built tool on the same pack. Blocks quoted from source say so. Everything was run on 2026-11-16 (Linux, .NET 10; the Unity facts on Unity 7000.0.0a16). Companion docs: `README.md` (the course), `docs/PRODUCTION-CHECKLIST.md` (what to configure), `contract/` (the normative law), `docs/DEMO.md` (a 10-minute demo).

## 1. What sv is, in one paragraph

sv is a **deterministic game-state engine**. It owns every number of every entity (health, shield, stats, statuses, charges) and the rules between them. Designers write those rules as four JSON files; a compiler turns them into C# kernels (Burst jobs in Unity). Game code sends **records** ("the goblin hits the hero for 30 Physical"), steps a **frame**, and reads results. The same inputs give a **bit-identical** world on any machine, worker count, CPU instruction set or storage layout, and any frame can be **rewound and replayed**. It is built for up to 1,000,000 entities per frame.

Three libraries, one rule: **tl owns time, sv owns state, Gi owns space.** They never reference each other; the host connects them through plain column data.

## 2. The data model

| Concept | Shape | Notes |
|---|---|---|
| Entity | a **slot** (22 bits) in a **chunk** of 64 rows | Slot = `chunk * 64 + row`. A template fills its own chunks, so slots of different templates start 64 apart. |
| Value | a **lane**: one value for the 64 rows of a chunk (256 bytes) | Fixed offset, known at compile time. |
| Value classes | `stat`, `intrinsic`, `event`, plus `channel` and `kind` as vocabularies | `stat` is an integer with a decimal scale (modified by cards and stat ops); `intrinsic` is an integer rules change directly; `event` is a level with enter/stay thresholds. |
| Record (`Effect`) | **16 bytes**: a 64-bit key plus `amount` (32) and `hits` (32) | Key = target 22 bits, source 22, kind 8, channel 8, origin 2, depth 2. |
| Storage | `Shared` (default) or `Dense` | Shared: a lane that equals its template default is the template's read-only lane, copied on first write (about 41-46 bytes per entity at rest). Dense: the table is the identity, one contiguous block per chunk. Both give the same digests. |
| Digest | one 64-bit hash of the whole state | Sum of `CellDigest.Key(lane, slot, bits)` over live cells that differ from the template default. Pool addresses, lane references and free rows are never hashed. |

Integer math everywhere on the hot path (stats use fixed point, basis points: 10,000 = 100%); the generated code is C# 9 so it also runs under Unity 7's compiler and Burst.

## 3. One frame

`Step()` runs these phases (names from `FrameLoop.Marks`; see section 8 for timing):

```
Send       (your code, tl, last frame's emits) records go into per-writer arenas, lock-free
Sealed     dead targets dropped, malformed records dropped, records sorted into ONE canonical order per chunk, exact duplicates merged
Logged     the canonical input is logged (the replay source)
Stats      stat modifiers (core layer, then external layer) are folded into stat lanes
Collected  the chunks that have work are listed
Decided    per target: gates by priority, shields/budgets, emits; then flows turn channel totals into value deltas
Committed  deltas are applied ONCE per entity: sum, then clamp (floor, ceiling)
Evented    event enter/stay thresholds are re-checked; edge rules emit records for the NEXT frame
Digested   the state hash
```

Two properties follow and carry the whole design:

1. **Order independence.** Because records are sorted canonically at Seal and everything is summed and then clamped once, the order you `Send` in never changes the result. That is what lets workers and threads run freely.
2. **One-frame reaction latency.** Records emitted by rules (thorns, lifesteal, edge triggers) arrive next frame, up to depth 3 (thorns of thorns of thorns). There is no same-frame lethal check: a revive acts one frame late. This is documented, not a bug.

The layer contract (each layer has one job, plain input and output types, an alone-test, a readable dump, and a slow reference form) is the table in `AGENTS.md`.

## 4. The pack: designers' four files

A pack is `values.sv.json`, `rules.sv.json`, `cards.sv.json`, `templates.sv.json` (each `{"schema":1,"rows":[...]}`). The pack used throughout this brief is `samples/Sv.Hello/pack`:

```json
{"id":1,"name":"health_flow","layer":"flow","then":[{"add":["Health","Healing"]},{"subtract":["Health","Physical","Fire"]}]},
{"id":2,"name":"health_bounds","layer":"fold","value":"Health","floor":0,"ceiling":{"stat":"MaxHealth"}},
{"id":3,"name":"fire_ward","card":"fire_ward","layer":"gate","priority":100,"when":{"eq":["record.channel","Fire"]},"then":[{"deny":{}}]},
{"id":4,"name":"shield","layer":"gate","priority":0,"when":{"and":[{"in":["record.channel",["Physical","Fire"]]},{"gt":["self.Shield",0]}]},"then":[{"absorb":{"budget":"Shield","per":"Amount","order":"ByAmountDescending"}}]},
{"id":5,"name":"low_health","layer":"event","event":"LowHealth","enter":{"below":["Health","MaxHealth",30,100]},"stay":{"below":["Health","MaxHealth",40,100]}}
```

Rule layers: `gate` (deny/allow/shape/absorb/spend, by priority), `emit` (send new records: to `Self` or `Source`, amount `Taken`, `Offered`, `NotTaken` or a number), `flow` (channel totals into value changes), `fold` (floor and ceiling at commit), `event` and `edge` (thresholds, and rules on enter/leave), `chain` (tags other rules test with `any`). A rule with `"card"` runs only for entities holding that card. Full grammar: `contract/CONTRACT-v3.0.0.md` plus the v3.1.0 (bricks) and v3.2.0 (stat layers) amendments.

### The compiler

`PackReader` parses and checks each file; `PackCompiler.Plan` validates the whole pack (names, ids, cycles, gate ties); a **10-pass pipeline** (Bind, Validate1, Lower, Stratify, Order, Split, Optimise, Partition, Emit, Validate2) produces C#. The tool:

```text
$ dotnet run -c Release --project src/Sv.Pack -- pack samples/Sv.Hello/pack --out o1 --namespace Demo.Sv
sv pack: samples/Sv.Hello/pack -> o1, 6 files, 6 written, PackHash 0EB68FE438A2EF3D
  Values.g.cs     2,585 B   enums (Stat, Intrinsic, Event, Channel, Kind, Template) and Layout
  Kernels.g.cs   23,675 B   your rules as code
  HostKernels.g.cs, PackData.g.cs (the pack as code: no JSON at run time), pack.svb, Rules.map.txt

$ dotnet run -c Release --project src/Sv.Pack -- pack samples/Sv.Hello/pack --out o2 --namespace Demo.Sv --host unity   # adds PackJobs.g.cs
sv pack: samples/Sv.Hello/pack -> o2, 7 files, 7 written, PackHash 0EB68FE438A2EF3D
```

(The two sizes are the real byte counts of the generated files.) In a .NET project, `build/Sv.Pack.targets` runs this on build and regenerates when a pack file or the generator changes. The enums are generated, so a renamed value is a **compile error**, not a silent bug.

A real excerpt of what a rule becomes (`Kernels.g.cs`, the Decide kernel; the per-target generic specialises on how many channels the target received, so the common single-channel case has no dispatch):

```csharp
switch ((present & (present - 1ul)) == 0ul ? Bits.Tzcnt(present) : -1)
{
    case 0: totalled |= Target<Lone0>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
    case 1: totalled |= Target<Lone1>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
    case 2: totalled |= Target<Lone2>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
    default: totalled |= Target<Mixed>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
}
```

## 5. The host API (.NET)

Everything below is `Sv.Host.FrameLoop<TKernels>`. Add `using Hello.Sv; using Sv; using Sv.Host;`.

### 5.1 Create, spawn, send, step, read

```csharp
using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 1024 });

var hero   = host.Spawn((int)Template.hero);
var knight = host.Spawn((int)Template.knight);
var goblin = host.Spawn((int)Template.goblin);

host.Send(hero,   goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
host.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30, hits: 2);
host.Step();

Console.WriteLine($"hero {host.Read(hero, (int)Intrinsic.Health)}  knight {host.Read(knight, (int)Intrinsic.Health)} shield {host.Read(knight, (int)Intrinsic.Shield)}");
Console.WriteLine($"LastDigest {host.LastDigest:X16}  Digest() {host.Digest():X16}");
```

```text
slots: hero 0 knight 64 goblin 128   (a template fills its own 64-entity chunk)
frame before Step: 0   frame after Step: 1
hero health 70  knight health 55 shield 0
LastDigest 01D5EE875E9A6337  Digest() 01D5EE875E9A6337
```

The knight took 30 Physical twice (`hits: 2` is 60 total): its shield absorbed 15 and health took 45.

| Call | Does |
|---|---|
| `Spawn(templateIndex)` / `Kill(slot)` | between frames only; `Spawn` returns the slot (throws `sv: the world is full`); a killed slot is `Dying` and is not reused while its death epoch is inside the rewind ring |
| `Send(target, source, kind, channel, amount, hits = 1, origin = Direct)` | one record for this frame; returns `false` for amount 0 or hits <= 0, or a full arena; **single thread** (section 5.6) |
| `Modify(StatOp)` | stat modifiers (section 5.3) |
| `Step()` | one frame |
| `Read(slot, lane)` / `Level(slot, eventId)` | a value / an event's level; read after `Step` |
| `Events`, `EventCount` | this frame's event edges (`HostEvent`: `Event`, `Slot`, `Entered`) |
| `Frame`, `LastDigest`, `Digest()` | frame number, the digest of the last step, a fresh digest |
| `Rewind(epoch)`, `Late(frame, records, count)`, `Resolve()` | time travel (5.4, 5.5) |
| `Sender(i)` | a per-thread writer (5.6) |
| `Dispose()` | frees native memory; calls afterwards throw `ObjectDisposedException` |

**Sharp edge, template numbers:** the host's `Spawn` takes the 0-based template **index** (what the generated `Template` enum holds), but the pack's own `id` numbers templates from 1 here, and the **reference interpreter and scenario files use the id/name**. Mixing them silently spawns the wrong template (or none).

### 5.2 Events

```csharp
host.Send(hero, EffectKey.NoSource, 0, (int)Channel.Physical, 75);
host.Step();
for (var at = 0; at < host.EventCount; at++)
{
    var change = host.Events[at];
    Console.WriteLine($"event {(Event)change.Event} slot {change.Slot} {(change.Entered ? "entered" : "left")}  Level now: {host.Level(change.Slot, change.Event)}");
}
// then Send 50 Healing and Step again
```

```text
event LowHealth slot 0 entered  Level now: True
event LowHealth slot 0 left  Level now: False
```

Events have hysteresis (enter below 30%, stay below 40%), so they do not flap. An `edge` rule can emit records when an event enters or leaves, and the tl glue can start timelines on edges.

### 5.3 Stat operations

Stats are modified by **stat ops** with a **source id**; removing a source removes everything it added. Shapes: `Added` (raw units of the stat's scale), `Additive` and `Multi` (basis points: 5000 is +50%), and `Cap` (a ceiling: a Cap of 120 leaves a stat of 100 unchanged). Two layers: `Core` (including derived stats) is applied first, then `External` on top of that result: Core `Added` 50 then External `Multi` 10000 turns a stat of 100 into (100 + 50) x 2 = 300 (section 3b of the tour prints the shapes and this layer case).

```csharp
host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Added, 5000, source: 7));
host.Step();   // 100 -> 5100
host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Multi, 5000, source: 8));
host.Step();   // (100 + 5000) x 1.5 = 7650
host.Modify(StatOp.RemoveSource(hero, 7));
host.Step();   // 100 x 1.5 = 150
```

```text
MaxHealth raw: 100
after Added +5000 (source 7): 5100
after Multi +5000 (source 8): 7650
after RemoveSource 7:         150
```

Canonical order is `RemoveSource` before `Add` within a frame, so a **buff refresh** (remove and re-add one source in the same frame) keeps the new buff (contract v3.0.1 clause 7).

### 5.4 Rewind and replay

The journal keeps the last `RingEpochs` (default 32) epochs of lane deltas, `FramesPerEpoch` (default 2) frames each. `Rewind(e)` restores the state at the **start** of epoch `e`.

```csharp
var epoch = host.World.EpochAtOrBefore(4);        // the epoch that holds frame 4
host.Rewind(epoch);                                // Frame is now the first frame of that epoch
for (var frame = host.Frame; frame < 9; frame++)   // YOU play the inputs again (rollback netcode)
{
    host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 3);
    host.Step();
}
```

```text
Epoch 5, oldest in ring 1, rewinding to epoch 3 (state at the start of frame 4)
Rewind returned True, Frame is now 4
straight digest 561E56E6B82FB175   rewound and replayed 561E56E6B82FB175   equal: True
ring holds epochs 1..5; Rewind(oldest - 1) False, Rewind(current + 1) False, Rewind(oldest) True
```

`Rewind` returns `false` outside the ring. A game-started `Rewind` also drops the logged input after the rewound frame (so a re-played frame is logged once); inputs from before are yours to replay.

### 5.5 Late input (networking)

A record that arrives after its frame is inserted at that frame and the world is replayed from the newest epoch at or before it. `Late` and `Resolve` must be called right after `Step`, before the next frame's input.

```csharp
var late = new LoggedRecord(EffectKey.Pack(hero, goblin, (byte)Kind.Goblin, (byte)Channel.Physical, Origin.Direct, 0), 11, 1);
long placedAt = host.Late(2, &late, 1);   // unsafe context; -1 if not accepted
bool resolved = host.Resolve();           // rewinds and replays the logged input with the late record merged in
```

```text
Late(2, ...) placed it at frame 2; Resolve True; ReplayFaults 0
digest after Resolve E0C1850B296ACE47   a straight run that had it at frame 2 E0C1850B296ACE47   equal: True
```

`HostOptions.Late` is `Clamp` (a too-old input moves to the oldest frame in the ring) or `Reject` (`Late` returns -1). `Resolve` **throws** if the input-log cap (`InputLogFrameLimit`, `InputLogFrameBytes`) dropped records its replay would need, instead of replaying less.

### 5.6 Threads: one rule

`FrameLoop.Send` is single-threaded per frame and **throws** if a second thread uses it in the same frame. For several game threads set `ExternalWriters = n` and give each thread its own writer:

```csharp
using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { ExternalWriters = 4 });
// thread i:
EffectWriter* writer = host.Sender(i);
writer->Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, amount: 1, hits: 1);
```

```text
4 threads x 10 sends of 1 Physical: hero health 60 (100 - 40)
FrameLoop.Send from a second thread in one frame: InvalidOperationException: sv: FrameLoop.Send was called from two threads in one frame; give each game thread its own writer (HostOptions.ExternalWriters and Sender(i))
```

With one writer under `ArenaPolicy.Grow` the raw arena grows by itself when a frame sends more than `RawCapacity`; with several writers or `HardCap` a full arena makes `Send` return `false` and `ExternalDropped` counts it.

### 5.7 Determinism is a tested property, not a hope

The digest must not depend on how the work is scheduled. Same world, same sends:

```text
workers  1 parts 64 Shared digest EA2F6A6F8F610424
workers  4 parts 64 Shared digest EA2F6A6F8F610424
workers 11 parts 16 Shared digest EA2F6A6F8F610424
workers  4 parts 64 Dense  digest EA2F6A6F8F610424
```

The test suite checks this at workers 1/2/4/7/11, parts 16/64, `Shared` and `Dense`, with AVX2 on, AVX2 off and all SIMD off (`DOTNET_EnableAVX2=0`, `DOTNET_EnableHWIntrinsic=0`), and pinned to 2 CPUs (`taskset -c 0,1`), because timing bugs only show there.

### 5.8 The oracle: a slow, simple reference engine

`Sv.Rules.Reference.Interpreter` runs a pack with no compiler and no tricks. It is the **oracle** that the compiled kernels must equal, value for value and digest for digest, on every frame. (Careful: it takes the template **id**, not the index.)

```csharp
var plan = PackCompiler.Compile(new PackSource(values, rules, cards, templates)).Value!;
var interpreter = new Ref.Interpreter(plan);
var state = interpreter.NewWorld(256);
int IdOf(string name) => plan.Schema.Templates.First(t => t.Name == name).Id;
var inputs = new Ref.FrameInputs();
inputs.Spawn(0, IdOf("hero")); inputs.Spawn(64, IdOf("knight")); inputs.Spawn(128, IdOf("goblin"));
inputs.Send(0,  128, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, RecordOrigin.Direct);
inputs.Send(64, 128, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, RecordOrigin.Direct);
state = interpreter.Step(state, inputs).State;
```

```text
reference: hero health 70, knight health 85 shield 0
compiled:  hero health 70, knight health 85 shield 0
```

### 5.9 Observability

```csharp
var marks = host.Marks;   // Stopwatch timestamps; phase i took marks[i] - marks[i-1]
```

```text
last frame, microseconds per phase (a one-entity world): Sealed 18us, Logged 1us, Stats 0us, Collected 0us, Decided 4us, Committed 0us, Evented 3us, Digested 0us, Ended 1us
raw arena grows 0, external drops 0, input log dropped 0
```

`host.Raw.Grows`, `host.ExternalDropped`, `host.Log.DroppedRecords`, `host.ReplayFaults` and `host.World.TableBytes` expose the counters. For a million entities see `docs/BENCH-REPORT.md`.

### 5.10 Packs as data, and misuse that fails loudly

```csharp
var compiled = PackCompiler.Compile(new PackSource(values, rules, cards, templates));
// compiled.Ok, compiled.Value (PackPlan), compiled.Diagnostics; PackHash.Of(pack); PackWriter for canonical JSON
```

```text
Compile ok True, PackHash 0EB68FE438A2EF3D, generated Layout.PackHash 0EB68FE438A2EF3D
negative rule id: ok False; rules.sv.json(253): error SV0009: rule id -3 is negative; ids are 0 or more

ArgumentOutOfRangeException: sv: kind and channel are 0 to 255 (Parameter 'kind')
ObjectDisposedException: sv: this FrameLoop was disposed
InvalidOperationException: sv: the world is full
```

`PackHash` is part of state identity: the generated kernels carry it, and the host throws `sv: the compiled kernels were built against another pack` if the pack and kernels differ. Every user-facing error message is listed in README chapter 11.

## 6. The command line

```text
sv pack  <folder> [--out <dir>] [--namespace <ns>] [--diagnostics text|json] [--host dotnet|unity] [--glue tl]
sv run   <pack> <scenario|folder> [--host reference|compiled] [--kernels <dll>] [--workers <n>] [--parts <n>] [--policy shared|dense]
sv trace <pack> <scenario> [--entity <name>] [--frame <n>]
```

`sv` below is `dotnet run -c Release --project src/Sv.Pack --` (or the built `sv`). A scenario (`samples/Sv.ApiTour/hello.scenario.json`) is a designer-readable test: entities, sends per frame, and expected values. `sv run` checks the expectations with the reference interpreter and prints the digest:

```json
{"schema":1,"name":"hello","frames":2,
"entities":[{"name":"Hero","template":"hero"},{"name":"Knight","template":"knight"},{"name":"Goblin","template":"goblin"},{"name":"Dragon","template":"dragon"}],
"sends":[
{"frame":0,"target":"Hero","source":"Goblin","channel":"Physical","amount":30},
{"frame":0,"target":"Hero","source":"Dragon","channel":"Fire","amount":45},
{"frame":0,"target":"Knight","source":"Goblin","channel":"Physical","amount":30},
{"frame":0,"target":"Knight","source":"Dragon","channel":"Fire","amount":45},
{"frame":1,"target":"Hero","source":"Hero","channel":"Healing","amount":50}
],
"expect":[
{"frame":0,"entity":"Hero","value":"Health","equals":25},
{"frame":0,"entity":"Knight","value":"Health","equals":85},
{"frame":0,"entity":"Knight","value":"Shield","equals":0},
{"frame":1,"entity":"Hero","value":"Health","equals":75}]}
```

```text
$ dotnet run -c Release --project src/Sv.Pack -- run samples/Sv.Hello/pack samples/Sv.ApiTour/hello.scenario.json
scenario hello (reference)
  frame 0 Hero.Health = 25 ok
  frame 0 Knight.Health = 85 ok
  frame 0 Knight.Shield = 0 ok
  frame 1 Hero.Health = 75 ok
  digest 9078B3C66F1DD3FE
```

`sv trace` prints every layer of every frame, which is how a designer sees **why** a number is what it is:

```text
$ dotnet run -c Release --project src/Sv.Pack -- trace samples/Sv.Hello/pack samples/Sv.ApiTour/hello.scenario.json --entity Knight --frame 0
frame 1
spawn #64 template knight
seal #64 from #128 kind Goblin Physical 30 x1 Direct depth 0
seal #64 from #192 kind Dragon Fire 45 x1 Direct depth 0
gate #64 from #128 kind Goblin Physical 30 x1 Direct depth 0 -> allow by default taken 30 x1
gate #64 from #192 kind Dragon Fire 45 x1 Direct depth 0 -> deny by fire_ward(3) taken 0 x1
absorb #64 from #128 kind Goblin Physical 30 x1 Direct depth 0 by shield(4) 15 amount
remove #64 from #192 kind Dragon Fire 45 x1 Direct depth 0
total #64 Physical 15
flow #64 health_flow(1) Health -15 from Physical
flow #64 absorb spends Shield -15
fold #64 Health 100 -> 85 delta -15 floor 0 ceiling 100
fold #64 Shield 15 -> 0 delta -15 floor -2147483648 ceiling 2147483647
frame 0 Hero.Health = 25 ok
frame 0 Knight.Health = 85 ok
frame 0 Knight.Shield = 0 ok
frame 1 Hero.Health = 75 ok
digest 9078B3C66F1DD3FE
```

(`sv trace` numbers the first simulated frame 1; the expectations count from 0.)The same trace, with every frame and a filter by entity and frame, is in the **web designer** (`eng/web.sh publish`, `eng/web.sh serve`): a 5.6 MB WebAssembly page that runs the same compiler and the reference interpreter, so designers check and simulate packs without installing anything.

## 7. Unity 7 (Burst, ECS)

`sv pack --host unity` adds `PackJobs.g.cs`: every frame phase as a `[BurstCompile(FloatMode = FloatMode.Strict, ...)]` job, and a generated `PackWorld`:

```csharp
// quoted from the generated PackJobs.g.cs
public static class PackWorld
{
    public static SvWorld<HostKernels> Create(HostOptions options) => new SvWorld<HostKernels>(PackData.Create(), options, new PackJobs());
}
```

`SvWorld<TKernels>` implements `ISvWorld` (`Spawn`, `Kill`, `Send`, `Modify`, `Step`, `Read`, `Level`, `Digest`, `Frame`, `LastDigest`, plus `ProbeOf(phase)`, which reports whether a phase really ran as Burst code). Plain use (this is the Hello sample, which prints the same two lines and digests as the .NET program; verified in a fresh project made only from the two git URLs):

```csharp
using var world = PackWorld.Create(new HostOptions { Workers = 1 });
var hero = world.Spawn((int)Template.hero);
world.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, Origin.Direct);
world.Step();
```

ECS glue (quoted from `packages/com.sv.unity.v4/Runtime/SvSlot.cs`): `SvEntities.Publish(entityManager, world)` creates the world handle entity; `SvEntities.Attach(entityManager, entity, world, template)` spawns a slot and adds `SvSlot` and `SvSlotCleanup`; `SvStepSystem` (`[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]`) kills the slots of destroyed entities (the cleanup component) and calls `world.Step()` each frame. `SvWorlds.DisposeAll()` runs before every assembly reload. **Window > sv > Pack Authoring** loads a pack folder, validates, saves and regenerates.

Proof in Unity: every phase runs as Burst at 1 and 11 job workers with digests equal to .NET (98 EditMode tests with tl, 88 without; one test enters Play mode and checks the auto-created `SvStepSystem` in the default world). A built Linux player with ahead-of-time Burst replays 37 recorded scenarios at 1 and 11 workers, every digest equal to .NET.

## 8. tl (timelines)

`sv pack --glue tl` emits outbox columns, kinds, clips and tracks for `timeline` channels. `Outbox.Send(host, cells, channel, kindLane, targetLane)` turns non-zero outbox cells into `Origin.Timeline` records. A poison status is an **entity**; a timeline drives it, and the host collects its outbox into records. The same chain runs on the .NET host and in Unity (tl's plain host): the NorthStar3 example is digest-pinned (`A566FAEC8837BA57`, `2D6A06776AE25DCB`). tl's ECS/SubScene path in Unity is not built.

## 9. Why it is correct: the proof stack

| Layer of proof | What it checks | Where |
|---|---|---|
| Laws | each layer alone against its slow scalar or `BigInteger` form | `tests/Sv.Laws.Tests` (573) |
| Differential | compiled kernels == reference interpreter on every value and digest, every frame, on 300 generated packs per variant, also under a hard-capped arena and odd capacities | `tests/Sv.Diff.Tests` (58) |
| Scheduling | digests equal at 5 worker counts x 2 part counts x 2 storages, ISA modes, 2 contended CPUs | same suites, `eng/v3-verify.sh` |
| Fuzzing | `eng/fuzz.sh`: hundreds of thousands of random packs; harnesses for send order, rewind/replay, malformed packs (with C# compile of whatever is accepted) and pack round trip | `docs/FUZZING.md` |
| Red team | an adversarial review with valid game mechanics at extremes: 54 attacks, 10 findings (4 critical), all fixed and kept as tests | `issues/0500-redteam-report.md` |
| Pins | digests of benchmark worlds and packs never move without a decision | `docs/PRODUCTION-CHECKLIST.md` |
| Unity | Burst jobs equal the .NET host, in the editor and in a built player | `eng/m8-run/gate.sh`, `eng/player-smoke.sh` |

One command for everything that runs on a laptop: `bash eng/v3-verify.sh` (ends with `v3-verify: all green`).

## 10. Performance, honestly

Ryzen 5 8500G, Linux, 1,000,000 entities, the 55-rule `lego` pack, 11 workers, digest every frame (`docs/BENCH-REPORT.md`, update rc.4):

| Load | Frame total | Notes |
|---|---|---|
| B: 1,000,000 records, chunk-bucketed | **7.66 ms** | the gate is 8.3 ms (30 Hz with room for a game); passes |
| A: 100,000 records | 2.33 ms | |
| r: 1,000,000 records, random targets **and** sources | 17.64 ms | a stress benchmark with a 19.0 ms ceiling (decision D20); it does **not** meet the 8.3 ms gate |

Warm frames allocate **0 bytes** on sv's threads (tested). Memory at rest 41-46 bytes per entity; with the full rewind ring and heavy activity about 450-760 bytes per entity. Seal, Log and Send are bandwidth-bound (they stream the records several times); Decide is compute-bound.

## 11. Known limits and open risks

- **One Unity editor:** only Unity 7000.0.0a16 (an alpha, CoreCLR only, C# 9) is supported.
- **Players:** only a Linux CoreCLR player has been run. Windows, macOS, mobile and console, and IL2CPP, have not (IL2CPP is blocked on this machine by a missing system library; the Windows legs need another machine; issue #99).
- **CI:** the last fully green CI is `cb23af8`. Since then (PRs 102, 104 and 105) GitHub Actions refuses to start jobs because of an account billing block (issue #103); those merges are proven locally on Linux x64 only.
- **Random load** (a million records with random targets and sources per frame) costs about 18 ms, above the 8.3 ms gate.
- **Reaction depth** is 3; deeper chains are cut. A revive acts one frame late.
- **Sizes:** at most 4,194,303 entities (22-bit slots), 256 channels and 256 kinds per pack, 64 exclusive gate groups.
- **API sharp edges:** template index (host) vs id (reference and scenarios, section 5.1); `Send` is single-threaded (5.6); `EntityCapacity` is chunk-granular because each template fills its own 64-entity chunks; a game `Rewind` drops later logged input (5.4); `Resolve` right after `Step` only.
- **tl's ECS timeline path in Unity** is not built for the sv glue.
- **Distribution:** install is by git URL with a tag from the public repository; there is no package registry release.

## 12. Repository map

| Path | Job |
|---|---|
| `src/Sv.Core` | storage, records, Seal, arenas, stat law, journal and input log; `netstandard2.1` and `net10.0`, C# 9, no allocations on warm paths |
| `src/Sv.Rules` | pack reader and writer, the 10-pass compiler and C# emitter, the reference interpreter |
| `src/Sv.Host` | `FrameLoop`, `HostPack`, workers, outbox; `net10.0` |
| `src/Sv.Pack` | the `sv` tool and the scenario runner |
| `src/Sv.Web`, `src/Sv.Wasm` | the browser designer |
| `packages/com.sv.engine`, `com.sv.unity.v4` | the Unity packages (a mirror of the engine sources, and the adapter) |
| `contract/` | the normative law: layouts, ordering, errors, determinism |
| `issues/` | every finding and decision, with receipts |
| `eng/` | verify, fuzz, player smoke, web and sync scripts |
