# sv for games

sv keeps **the numbers of every entity**: health, shields, stats, statuses, charges, immunities. A designer writes those values and the rules between them as data. Your code sends **records** ("the goblin hits the hero for 30 Physical") and sv decides what actually happens: gates, shields, cards, reflections, events. It does this the same way every time (deterministic), for up to a million entities per frame, and it can **rewind** and **replay** any frame.

This file is the whole course. Read it top to bottom and you will know how sv works, how to design with it, what it cannot do, and where it will bite you. The code in chapter 1 is a real program in this repository (`samples/Sv.Hello`), and CI runs it on Linux x64, Linux ARM64, macOS and Windows and checks it prints exactly the numbers shown here. Other snippets name the tested file they come from.

**Who it is for:** programmers. A designer edits the four pack files (or the Pack Authoring window in Unity) and never writes C#.

**Status:** release candidate `v3.0.0-rc.2`. Engine `com.sv.engine` 3.0.0, Unity adapter `com.sv.unity.v4` 4.0.0, contract v3.0.0 to v3.2.0 (`contract/`).

## Contents

0. [Why sv, in one example](#0-why-sv-in-one-example)
1. [Run your first pack (10 minutes)](#1-run-your-first-pack-10-minutes)
2. [The model: values, records, one frame](#2-the-model-values-records-one-frame)
3. [The four pack files](#3-the-four-pack-files)
4. [Patterns](#4-patterns)
5. [The host API (.NET)](#5-the-host-api-net)
6. [Unity](#6-unity)
7. [With tl (timelines)](#7-with-tl-timelines)
8. [Determinism, digests, rewind and replay](#8-determinism-digests-rewind-and-replay)
9. [Speed and memory](#9-speed-and-memory)
10. [Prove it on your machine](#10-prove-it-on-your-machine)
11. [Sharp edges: what will bite you](#11-sharp-edges-what-will-bite-you)
12. [Known limits and open risks](#12-known-limits-and-open-risks)
13. [Cheat sheet](#13-cheat-sheet)
14. [Repository](#14-repository)

---

## 0. Why sv, in one example

You need: "a knight has a 15-point shield and a fire ward. A goblin hits him for 30, a dragon breathes fire for 45. The hero has neither." Without sv you write a damage function, then special cases for shields, wards, armour, thorns, lifesteal, immunities, and every new card is a code change and a new bug in the order of operations.

With sv the designer writes **one rule per mechanic, as data** (chapter 1). You get, for free:

- **Designers change it without you.** A new ward is two lines of JSON, not a code change.
- **One order of operations, always.** Gates run by priority, shields absorb in a stated order, totals are summed then clamped once. Two designers cannot invent two orders.
- **Exact replay.** The same records give the same numbers on every machine. Each frame ends with a 64-bit digest of the whole state, and CI compares them on Linux x64 (three CPU modes), Linux ARM64, macOS and Windows.
- **Rewind.** Go back to any of the last 32 epochs (about 2 seconds at 30 Hz by default) and replay.
- **Speed.** 1,000,000 entities with one timeline record each per frame: 7.7 ms per frame on a 6-core desktop, 0 bytes allocated (chapter 9).

---

## 1. Run your first pack (10 minutes)

**Setup:** .NET 10 SDK and this repository.

```sh
git clone https://github.com/IAFahim/sv.git
cd sv
dotnet run -c Release --project samples/Sv.Hello
```

It prints:

```
frame 1: hero health 25, knight health 85 shield 0, hero low health True, digest D0391ABCE50C62DB
frame 2: hero health 75, knight health 85 shield 0, hero low health False, digest 9078B3C66F1DD3FE
```

That is the whole program. The pack is four small files in `samples/Sv.Hello/pack`.

**`values.sv.json`**: every value an entity has, and the channels and kinds a record can carry.

```json
{"schema":1,"nextLane":6,"rows":[
{"id":0,"name":"MaxHealth","class":"stat","default":100.0},
{"id":1,"name":"Health","class":"intrinsic","default":100},
{"id":2,"name":"Shield","class":"intrinsic","default":0},
{"id":3,"name":"FireWard","class":"intrinsic","default":0},
{"id":4,"name":"Kind","class":"intrinsic","default":0,"role":"kind"},
{"lane":5,"word":0,"name":"LowHealth","class":"event","default":false},
{"id":0,"name":"Physical","class":"channel"},
{"id":1,"name":"Fire","class":"channel"},
{"id":2,"name":"Healing","class":"channel"},
{"id":0,"name":"None","class":"kind"},
{"id":1,"name":"Goblin","class":"kind"},
{"id":2,"name":"Dragon","class":"kind"}
]}
```

**`rules.sv.json`**: what happens to records.

```json
{"schema":1,"rows":[
{"id":1,"name":"health_flow","layer":"flow","then":[{"add":["Health","Healing"]},{"subtract":["Health","Physical","Fire"]}]},
{"id":2,"name":"health_bounds","layer":"fold","value":"Health","floor":0,"ceiling":{"stat":"MaxHealth"}},
{"id":3,"name":"fire_ward","card":"fire_ward","layer":"gate","priority":100,"when":{"eq":["record.channel","Fire"]},"then":[{"deny":{}}]},
{"id":4,"name":"shield","layer":"gate","priority":0,"when":{"and":[{"in":["record.channel",["Physical","Fire"]]},{"gt":["self.Shield",0]}]},"then":[{"absorb":{"budget":"Shield","per":"Amount","order":"ByAmountDescending"}}]},
{"id":5,"name":"low_health","layer":"event","event":"LowHealth","enter":{"below":["Health","MaxHealth",30,100]},"stay":{"below":["Health","MaxHealth",40,100]}}
]}
```

**`cards.sv.json`**: things an entity can hold that switch rules on.

```json
{"schema":1,"rows":[
{"id":1,"name":"fire_ward","value":"FireWard"}
]}
```

**`templates.sv.json`**: the kinds of entity you spawn.

```json
{"schema":1,"rows":[
{"id":1,"name":"hero","values":{"Health":100,"MaxHealth":100.0}},
{"id":2,"name":"knight","values":{"Health":100,"MaxHealth":100.0,"Shield":15},"cards":{"fire_ward":1}},
{"id":3,"name":"goblin","values":{"Kind":1}},
{"id":4,"name":"dragon","values":{"Kind":2}}
]}
```

The build compiles the pack into C# (the project imports `build/Sv.Pack.targets`; chapter 5). The program:

```csharp
using Hello.Sv;
using Sv;
using Sv.Host;

using var world = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { Workers = 1 });

var hero = world.Spawn((int)Template.hero);
var knight = world.Spawn((int)Template.knight);
var goblin = world.Spawn((int)Template.goblin);
var dragon = world.Spawn((int)Template.dragon);

world.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
world.Send(hero, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45);
world.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
world.Send(knight, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45);
world.Step();
Report("frame 1");

world.Send(hero, hero, 0, (int)Channel.Healing, 50);
world.Step();
Report("frame 2");

void Report(string when)
{
    Console.WriteLine($"{when}: hero health {world.Read(hero, (int)Intrinsic.Health)}, knight health {world.Read(knight, (int)Intrinsic.Health)} shield {world.Read(knight, (int)Intrinsic.Shield)}, hero low health {world.Level(hero, (int)Event.LowHealth)}, digest {world.LastDigest:X16}");
}
```

Read the numbers:

- **Hero, frame 1:** 30 Physical and 45 Fire, nothing stops them: 100 - 75 = **25**. That is below 30 % of MaxHealth, so the `LowHealth` event **enters**.
- **Knight, frame 1:** the `fire_ward` card's gate (priority 100) **denies** the Fire record. The shield gate **absorbs** 15 of the 30 Physical and is spent: health **85**, shield **0**.
- **Hero, frame 2:** healing 50 gives **75**. `LowHealth` stays only while below 40 %, so it **leaves**.
- **Digest:** a hash of every value of every entity. Run the program on any machine: the same two digests.

`Template`, `Intrinsic`, `Channel`, `Kind` and `Event` are enums generated from the pack, so a renamed value is a compile error, not a silent bug.

---

## 2. The model: values, records, one frame

**Values.** Every entity of a template has every value of the pack, each at a fixed place (a *lane*: one value for 64 entities, 256 bytes). Lanes that still hold the template's defaults are shared, so 1,000,000 entities take 41-46 bytes each at rest.

| Class | What it is | Example |
|---|---|---|
| `stat` | modified by cards and stat ops (added, additive %, multiply), integer with a decimal scale | MaxHealth, MoveSpeed |
| `intrinsic` | an integer the rules change directly; `"role": "kind"` and `"role": "owner"` are special | Health, Shield, Kind, Owner |
| `event` | a true/false condition with enter and stay thresholds; edges can trigger rules or start timelines | LowHealth |
| `channel` | what a record carries: damage types, healing, grants | Physical, Fire, Healing |
| `kind` | who or what sent it, for rules like "immune to snipers" | Goblin, Sniper |

**Records.** Every change is a 16-byte record: target, source, kind, channel, amount, hits, origin, depth. You send them with `Send`, timelines send them through the tl glue, and rules emit more (thorns, lifesteal, reflection) up to depth 3.

**One frame (`Step`).**

```
Send     records arrive from your code, timelines and last frame's emits
Seal     dead targets dropped, records grouped per entity in one canonical order, exact duplicates merged
Log      the frame's input is logged for replay
Decide   per entity: gates by priority, shields and budgets, cards, then the flows sum each channel
Commit   values change once per frame: sum, then clamp (floor, ceiling) once
Events   enter/stay conditions are re-checked; edge rules emit for next frame
Digest   one 64-bit hash of the whole state
```

Because everything is summed and then clamped **once**, the order your code sent records in never matters. That is what makes it deterministic across threads and machines.

---

## 3. The four pack files

Every file is `{"schema":1,"rows":[...]}`. Full grammar: `contract/CONTRACT-v3.0.0.md` and the additions in `CONTRACT-v3.1.0-bricks.md` and `CONTRACT-v3.2.0-stat-layers.md`. The two big working examples are `packs/reference` (the worked example) and `packs/lego` (22 mechanics, explained in `docs/LEGO-REPORT.md`).

### 3.1 `values.sv.json`

`id` is the lane (values) or the number (channels, kinds). Events give `lane` and `word` instead. `nextLane` is the first free lane. Stats take a float `default` in designer units; intrinsics take an integer.

### 3.2 `rules.sv.json`: the layers

| Layer | Runs | Does | Example from the packs |
|---|---|---|---|
| `gate` | per record, by `priority` (high first) | `deny`, `allow`, shape the amount (`added`, `additive`, `multi`), `absorb` from a budget, `spend` | sniper ward, shield, immunity mask |
| `emit` | after gates, per record or per source | sends new records: `to` `Self` or `Source`, amount `Taken`, `Offered`, `NotTaken` or a number | thorns, lifesteal, mirror |
| `flow` | once per entity | turns channel totals into value changes: `add`, `subtract`, `set`, `max`, `min` | health_flow |
| `fold` | at commit | `floor` and `ceiling` for a value, from numbers or stats | health_bounds |
| `event` | after commit | `enter` and `stay` thresholds | low_health |
| `edge` | on an event's enter or leave | emits records | panic_charge |
| `chain` | while true | adds tags other rules test with `any` | cursed blocks healing |

Conditions read `record.channel`, `record.kind`, `record.source`, `self.<Value>`, `source.<Value>`, and use `eq`, `ne`, `gt`, `ge`, `in`, `bit`, `below`, `and`, `any`, `roll`. A rule with `"card"` runs only for entities holding that card.

### 3.3 `cards.sv.json`

A card names the intrinsic that counts how many the entity holds, and optionally stat `modifiers` that apply while it is held:

```json
{"id":9,"name":"charge_ward","value":"ChargeWard","modifiers":[{"stat":"MoveSpeed","multi":-0.5}]}
```

(from `packs/reference/cards.sv.json`)

### 3.4 `templates.sv.json`

Starting values, cards, and `forwards`: a part that passes a channel to its owner.

```json
{"id":5,"name":"hand","forwards":[{"channel":"Healing","to":"Owner"}]}
```

(from `packs/reference/templates.sv.json`: healing sent to a hand reaches the entity in the hand's `Owner` value)

---

## 4. Patterns

All of these are rows in `packs/reference` or `packs/lego`, each with scenario tests.

| You want | Write | Pack row |
|---|---|---|
| damage and healing | a `flow` with `add` and `subtract`, a `fold` with floor 0 and ceiling MaxHealth | reference `health_flow`, `health_bounds` |
| immune to one attacker type | a `gate` that denies on `record.kind` | reference `sniper_ward` |
| immunity by bits ("fire and curse") | a `gate` with `{"bit": ["self.Immunity", "record.channel"]}` | reference `immunity_mask` |
| a shield that soaks damage | a `gate` with `absorb` from an intrinsic budget | reference `shield` |
| thorns | an `emit` to `Source`, amount `Taken`, `depthLimit` 0 | reference `thorns` |
| lifesteal | an `emit` to `Self` on `Healing` when damage was taken | reference `leech` |
| "can't heal while cursed" | a `chain` that tags, and a `gate` that tests the tag with `any` | reference `cursed`, `cursed_blocks_healing` |
| a status that ticks (poison) | a status entity that a timeline drives (chapter 7) | NorthStar3 `poison_status` |
| low health starts a timeline | an event with `"starts": "<timeline>"` | NorthStar3 `LowHealth` |
| a part passes to its owner | template `forwards` | reference `hand`, `shield` |

---

## 5. The host API (.NET)

**Generate the code.** In your game's `.csproj`:

```xml
<PropertyGroup>
  <SvPackNamespace>MyGame.Sv</SvPackNamespace>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/sv/src/Sv.Host/Sv.Host.csproj" />
  <ProjectReference Include="path/to/sv/src/Sv.Pack/Sv.Pack.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>
<Import Project="path/to/sv/build/Sv.Pack.targets" />
```

(the same lines as `samples/Sv.Hello/Sv.Hello.csproj`). The pack lives in a `pack` folder next to the project (set `SvPackFolder` to move it). The build writes `Values.g.cs` (the enums and `Layout`), `Kernels.g.cs` (your rules as code), `HostKernels.g.cs` and `PackData.g.cs` (the pack as code, so nothing is parsed at run time). It regenerates when a pack file or the generator changes.

**The calls you use:**

| Call | Does |
|---|---|
| `new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { ... })` | the world |
| `Spawn((int)Template.x)` / `Kill(slot)` | between frames only |
| `Send(target, source, kind, channel, amount, hits = 1, origin = Origin.Direct)` | a record for this frame |
| `Modify(StatOp.Add(...))`, `Modify(StatOp.RemoveSource(...))` | stat modifiers |
| `Step()` | one frame |
| `Read(slot, lane)`, `Level(slot, event)` | values and events |
| `Events`, `EventCount` | this frame's event edges (`HostEvent`: event, slot, entered) |
| `LastDigest`, `Digest()` | the state hash |
| `Rewind(epoch)` | back to the start of an epoch |
| `Late(frame, records, count)`, `Resolve()` | input that arrives late (networking) |

**Options that matter** (`HostOptions`): `EntityCapacity` (default 65,536), `Workers` (default 1; 11 was best on a 6-core, 12-thread desktop), `Parts` (64), `DigestEvery` (1), `RingEpochs` and `FramesPerEpoch` (32 and 2: the rewind window), `Arena` (grow, or a hard cap for the browser), `EventCapacity` (16,384 per channel per frame), `Late` (`Clamp` or `Reject`). More in `docs/PRODUCTION-CHECKLIST.md`.

---

## 6. Unity

**Use exactly Unity 7000.0.0a16** with Entities 7.0.0, Collections 7.0.0 and Burst 2.0.0 (the same editor as tl.unity). Add to `Packages/manifest.json`, pinned to a release tag:

```json
"com.sv.engine": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.engine#v3.0.0-rc.3",
"com.sv.unity.v4": "https://github.com/IAFahim/sv.git?path=/packages/com.sv.unity.v4#v3.0.0-rc.3"
```

The repository is private: Unity clones with your local git credentials, so your account needs access. With a local checkout use `"file:<sv checkout>/packages/com.sv.engine"` (and the same for the adapter) instead. `SV_GIT_REF=<tag> bash eng/player-smoke.sh` builds a player from exactly these git packages.

Generate with **Window > sv > Pack Authoring** (Regenerate) or `sv pack <folder> --host unity --namespace <ns> --out <dir>`. That adds `PackJobs.g.cs`: every frame phase as a Burst job. Then:

```csharp
var world = MyGame.Sv.PackWorld.Create(new HostOptions { Workers = 11, EntityCapacity = 100_000 });
```

`SvWorld` has the same calls as chapter 5. For ECS, `SvEntities.Publish` and `SvEntities.Attach` give entities slots and `SvStepSystem` steps the world. Details and the tl setup: `docs/UNITY.md`.

What is proven in Unity: every phase runs as Burst at 1 and 11 job workers with digests equal to .NET (97 EditMode tests, also from a fresh clone into a new project), and a built **Linux player** (CoreCLR, Burst compiled ahead of time) replays 37 recorded scenarios with every digest equal to .NET (`eng/player-smoke.sh`).

---

## 7. With tl (timelines)

sv and tl do not reference each other; a few generated lines connect them. `sv pack <folder> --glue tl` writes `Glue.g.cs`: an outbox column per timeline channel, the clips, the tracks that feed them, and `TimelineRequests` (an event value with `"starts": "poison"` starts that timeline on enter and stops it on leave).

The tested frame order (`tests/Sv.NorthStar3/Rig.cs`, and the same in Unity):

1. tl ticks, and the generated tl system writes each status entity's outbox cell.
2. `Outbox.Send(host, cells, channel, kindLane, targetLane)` turns each non-zero cell into one `Origin.Timeline` record from the status entity to its target, and zeroes the cell.
3. Your own `Send`, `Modify`, `Spawn`, `Kill`.
4. `Step()`.
5. `TimelineRequests.Apply(players, host.Events, host.EventCount)`.

A ticking poison is a **status entity** (template `poison_status`) whose timeline sends Poison to the entity it is aimed at, so a ward on the target can still stop each tick. In Unity, tl runs on its plain host (no SubScene needed): `docs/UNITY.md`, section "tl in Unity".

---

## 8. Determinism, digests, rewind and replay

- **Rules that keep it deterministic:** integers only for state; one canonical record order; sum then clamp once; no float maths, wall-clock time, unseeded random or dictionary order in your glue.
- **Digest:** `LastDigest` after every `Step` (`DigestEvery = 1`). Log `(Frame, LastDigest)`; two machines that disagree tell you the first frame they differ.
- **Rewind:** `Rewind(epoch)` returns the world to the start of that epoch, including records in flight. The last `RingEpochs` epochs are kept.
- **Replay:** external records are logged per frame; derived records (thorns, events) are recreated. `Late` and `Resolve` fold input that arrives after its frame.
- **Twin journals:** `tests/Sv.Twin.Fixtures` records every call into a text journal; Unity and the player replay those journals and compare every digest. Use the same idea to compare your game across hosts.

---

## 9. Speed and memory

Measured on a Ryzen 5 8500G (6 cores, 12 threads), 1,000,000 entities, 11 workers, the `lego` pack (55 rules), digest every frame:

| Load per frame | Time | |
|---|---|---|
| 100,000 random records | 2.4-2.8 ms | |
| 1,000,000 timeline records (one per entity) | 7.7-7.8 ms | the 8.3 ms gate (25 % of a 30 Hz frame) passes |
| 1,000,000 uniformly random cross-entity records | about 18 ms | a stress test, ceiling 19 ms (decision D20) |
| memory at rest | 41-46 B per entity | |
| warm allocation | 0 B on sv's threads | |

tl's own work is not included (chapter 12). The full report: `docs/BENCH-REPORT.md`.

---

## 10. Prove it on your machine

```sh
bash eng/v3-verify.sh
```

Runs every suite under three CPU modes (default, no AVX2, scalar), the integration suites, the checked build and this README's chapter 1 sample, and the mirror checks. It ends with `v3-verify: all green`. Set `TL_ROOT` if tl is not next to sv. Unity: `eng/m8-run/gate.sh` (set-up in `docs/M8-HANDOFF.md`) and `bash eng/player-smoke.sh`.

---

## 11. Sharp edges: what will bite you

| You see | Cause | Do this |
|---|---|---|
| `sv: the compiled kernels were built against another pack` | the generated code is from another version of the pack | rebuild; in Unity press Regenerate |
| `sv: spawn, kill and rewind happen between frames, after every writer is absorbed` | you spawned or killed inside a frame | do it before `Step` |
| `sv: the world is full` | more entities than `EntityCapacity` | raise it |
| `sv: resolve late inputs right after Step, ...` | `Late`/`Resolve` called at the wrong time | call them right after `Step` |
| a value changes one frame later than you expected | changes from this frame's records commit at the end of the frame; edge emits arrive next frame | read after `Step` |
| a revive does not save the entity this frame | there is no same-frame lethal check | design for one frame of delay (chapter 12) |
| an event fires for many rows as "dense" | more edges than `EventCapacity` on a channel this frame | read the edge words, or raise the capacity |
| digests differ between two runs on one machine | glue code: an unzeroed array, unseeded random, wall-clock, dictionary order | see `docs/PRODUCTION-CHECKLIST.md`, "Reading a digest mismatch" |
| digests differ only after you changed the generator | stale generated code | delete `obj/SvPack/.svpack.key` and rebuild |

---

## 12. Known limits and open risks

Honest list.

- **One Unity editor.** Only Unity 7000.0.0a16 (an alpha, CoreCLR only, C# 9) is supported, as for tl.unity. Unity 6 is not.
- **Players.** Only a Linux CoreCLR player has been run. Windows, macOS, mobile and console players, and IL2CPP, have not.
- **tl at a million.** tl's own kind job takes about 8.45 ms at 1,000,000 rows on its own, so sv plus tl at that size does not fit a 30 Hz budget of 8.3 ms. That is tl's cost (issue 0149); size your scenes for both.
- **Random load.** A million records with random targets **and** random sources per frame costs about 18 ms (decision D20). Timeline and gameplay load that is grouped by entity is inside the gate.
- **Reaction depth.** A record can cause records up to depth 3 (thorns of thorns of thorns). Deeper chains are cut.
- **Sizes.** At most 4,194,303 entities (22-bit slots), 256 channels and 256 kinds per pack, 64 exclusive gate groups, masks of 64 bits.
- **Same-frame lethal check.** A revive acts one frame late.
- **History memory.** With the full rewind ring and heavy activity, memory grows to about 450-760 bytes per entity (`docs/BENCH-REPORT.md`).
- **tl's ECS timeline path in Unity** (SubScene, Playable Director) is not built for the sv glue; tl's plain host is.
- **Install** is by git URL with a tag (chapter 6) or a `file:` path; there is no package registry release yet, and the repository is private.

---

## 13. Cheat sheet

```
values.sv.json   stat | intrinsic | event | channel | kind        what every entity has
rules.sv.json    gate (priority) -> emit -> flow -> fold -> event -> edge; chain tags
cards.sv.json    a card turns rules on, and can carry stat modifiers
templates.sv.json  starting values, cards, forwards (part -> owner)

build            build/Sv.Pack.targets -> Values, Kernels, HostKernels, PackData (.g.cs)
.NET             new FrameLoop<HostKernels>(PackData.Create(), options)
Unity            PackWorld.Create(options)  (sv pack --host unity)
tl               sv pack --glue tl; Outbox.Send per channel before Step; TimelineRequests.Apply after
frame            Send / Modify / Spawn / Kill  ->  Step  ->  Read / Level / Events  ->  LastDigest
prove            bash eng/v3-verify.sh   (Unity: eng/m8-run/gate.sh, eng/player-smoke.sh)
```

---

## 14. Repository

| Path | What |
|---|---|
| `src/Sv.Core`, `src/Sv.Host` | the engine: lanes, records, the frame, digest, rewind |
| `src/Sv.Rules` | the pack reader, the reference interpreter (the oracle) and the code generator |
| `src/Sv.Pack` | the `sv` tool: `sv pack`, `sv run` |
| `src/Sv.Web`, `src/Sv.Wasm` | the browser designer and simulator (`docs/WEB.md`) |
| `packages/com.sv.engine`, `packages/com.sv.unity.v4` | the Unity packages |
| `packs/reference`, `packs/lego` | the two big example packs |
| `samples/Sv.Hello` | chapter 1 |
| `tests/` | laws, scenarios, differential tests (compiled against the oracle), twin journals, NorthStar3 (tl) |
| `contract/` | the contract texts; `issues/` the decisions and their receipts; `docs/` the guides |
| `docs/PRODUCTION-CHECKLIST.md` | what to do for a real project test |

Agents: read `AGENTS.md` first.

**Licence:** MIT. See `LICENSE`.
