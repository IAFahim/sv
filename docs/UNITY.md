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
add `SvSlot`, the value and stat columns in the same `ComponentTypeSet` as tl's columns.

## 3. Register the links once, install the host

```csharp
var links = new SvLinkRegistry();
links.Stat<MaxHp>(schema.Stat("max_hp"));
var hp = links.Intrinsic<Hp, HpDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.OfStat<MaxHp>(), remote: true);
links.Condition<LowHp>(hp, 30, 100, 40, 100, capacity: 65536);      // enter below 30 %, leave at 40 %
links.Link<HpDelta>(kindId, LinkMode.Delta);                          // one line per use, for Validate and Hash
var host = SvHost.Install(world, board, links);                      // throws on a LinkFault
```

`Install` creates `SvBeginSystem` (first in `SimulationSystemGroup`) and `SvEndSystem` (last).
tl's systems run between them. Limits: 4 dense intrinsics, 8 stat columns, stat index below 64.

## 4. Per frame (section 2.4 of the plan)

| Where | Call |
|---|---|
| main thread, before the group | `host.Enqueue(StatOp.Add(...))`, `host.Enqueue(StatOp.RemoveSource(...))` |
| `SvBeginSystem` | expire, canonical Apply, Recalculate, scatter of changed stats, fold job |
| tl systems | kinds read store columns and add into deltas |
| a kind that writes another row | `SvInboxWriter inbox = host.InboxWriter(hp);` in the job, then `inbox.Send(targetSlot, amount)` |
| `SvEndSystem` | derive events, digest every `host.DigestEvery` ticks |
| main thread, after the group | `host.Events(hp)` (ascending slot), `host.Channel(hp).Dense`, `host.Digest` |

Deltas written in tick t are folded at the start of tick t+1. The fold is
`after = clamp(before + saturate32(delta + inbox), min(floor, ceiling), ceiling)`; it clears the
delta and the inbox. Above the channel capacity no per-row events are delivered: one Dense record,
and `host.EdgeBit(hp, slot)` stays readable until the next fold.

## 5. Rows

- `host.Bind(entity)` gives a slot; `host.Unbind(slot)` frees it. The `SvSlot` component is written
  by the next `SvBeginSystem`.
- Waves: `host.Spawn(entityManager, prefab, templateSlot, spawned)` instantiates a prefab that
  already carries the template's stat values and assigns slots with `Board.Spawn`.
- Structural changes cost sv nothing: sv keeps slot-indexed arrays only, never chunk pointers.

## 6. Rules that keep it exact

- Remote writes are integers through the inbox. Floats never cross rows.
- Every delta writer goes through a read-write type handle (tl's generated job does). A write
  through a cached pointer bypasses the chunk change filter and is not folded.
- All sends to one slot serialize (about 10 ns each): a measured, documented limit.
