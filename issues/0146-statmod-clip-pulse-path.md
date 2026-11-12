# Issue 0146 - Modifier clips: pulse at clip Start/End becomes a StatOp inside Burst (plan mode M)

Labels: `ai`, `area:unity`, `area:core`, `open`, `plan-gap`.

## Problem

The plan (plan-sv.md lines 68 and 399) defines mode M: a clip's Start pulse adds a modifier, its End pulse removes it
with `RemoveSource`, source id `0x40000000 | timeline << 8 | playbackSlot`, no expiry, no wheel entry, End idempotent.
The adapter has no such path. The only producer of `StatOp` is the main-thread `SvHost.Enqueue`. The two NorthStar2
Unity tests build the clip source id by hand, so the path is simulated, not shipped. docs/USING-WITH-TL.md row M
describes behaviour that does not exist in the adapter.

## Wanted

- A Burst-safe op sink a tl kind can write to from a job: per-worker `StatOp` lists (or a per-row op buffer),
  `Apply` writes `SvOp` into it, `SvBeginSystem` merges, canonicalizes (existing `StatOps.Canonicalize`) and applies.
- A toolbox clip, written once:

```csharp
[TlClip("Stats/Modifier")]
public struct StatModClip { public GameStat Stat; public ModifierKind Kind; public float Value; }

public struct StatMod : ITlTrack<StatModClip>
{
    [Pulse(typeof(SvModifier), On = ClipEvent.Start)] public static SvOp Start(in TlFrame<StatModClip> f) => ...;
    [Pulse(typeof(SvModifier), On = ClipEvent.End)] public static SvOp End(in TlFrame<StatModClip> f) => ...;
}
```

- Reverse playback swaps Start and End (tl does this); rewind or interrupt gives two Ends for one Start: the second is a
  no-op. Rollback of a keyframe must leave the modifier set equal to the one at that tick.

## Done when

- A timeline JSON with a `StatModClip` changes `MaxHp` at Start, restores it at End, and again after reverse.
- Digest equal at 1 and 11 workers and equal to a .NET twin; 0 B warm; no main-thread `Enqueue` in the path.
- docs/USING-WITH-TL.md row M and docs/UNITY.md describe what exists.

## Receipts of the gap

`grep -n StatOp packages/com.sv.unity/SvHost.cs` shows only the managed queue (lines 20, 88, 231, 299, 300).
