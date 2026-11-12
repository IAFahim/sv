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
- All sends to one slot serialize (about 10 ns each, `Interlocked.Add`); the per-worker fallback is core work (C3).
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
