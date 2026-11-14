using System.Collections.Generic;

namespace Sv.Rules.Reference
{
    public readonly struct RefRecord
    {
        public const int SlotBits = 22;
        public const int SlotLimit = 1 << SlotBits;
        public const int NoSource = SlotLimit - 1;

        public int Target { get; }

        public int Source { get; }

        public int Kind { get; }

        public int Channel { get; }

        public RecordOrigin Origin { get; }

        public int Depth { get; }

        public int Amount { get; }

        public int Hits { get; }

        public RefRecord(int target, int source, int kind, int channel, RecordOrigin origin, int depth, int amount, int hits)
        {
            Target = target;
            Source = source;
            Kind = kind;
            Channel = channel;
            Origin = origin;
            Depth = depth;
            Amount = amount;
            Hits = hits;
        }

        public ulong Key =>
            ((ulong)(uint)(Target & (SlotLimit - 1)) << 42)
            | ((ulong)(uint)(Source & (SlotLimit - 1)) << 20)
            | ((ulong)(uint)(Kind & 0xFF) << 12)
            | ((ulong)(uint)(Channel & 0xFF) << 4)
            | ((ulong)((uint)Origin & 3u) << 2)
            | (ulong)(uint)(Depth & 3);

        public RefRecord WithTarget(int target) => new RefRecord(target, Source, Kind, Channel, Origin, Depth, Amount, Hits);

        public RefRecord WithAmount(int amount, int hits) => new RefRecord(Target, Source, Kind, Channel, Origin, Depth, amount, hits);

        public bool HasSource => Source != NoSource;

        public static int Compare(RefRecord first, RefRecord second)
        {
            var a = first.Key;
            var b = second.Key;
            if (a != b)
            {
                return a < b ? -1 : 1;
            }

            return first.Amount < second.Amount ? -1 : first.Amount > second.Amount ? 1 : 0;
        }
    }

    public sealed class RecordOrder : IComparer<RefRecord>
    {
        public static readonly RecordOrder Canonical = new RecordOrder();

        public int Compare(RefRecord x, RefRecord y) => RefRecord.Compare(x, y);
    }

    public enum CommandKind : byte
    {
        Spawn = 0,
        Kill = 1,
    }

    public readonly struct StructuralCommand
    {
        public CommandKind Kind { get; }

        public int Slot { get; }

        public int Template { get; }

        public StructuralCommand(CommandKind kind, int slot, int template)
        {
            Kind = kind;
            Slot = slot;
            Template = template;
        }

        public static StructuralCommand Spawn(int slot, int template) => new StructuralCommand(CommandKind.Spawn, slot, template);

        public static StructuralCommand Kill(int slot) => new StructuralCommand(CommandKind.Kill, slot, -1);
    }

    public enum StatOpKind : byte
    {
        Add = 0,
        Remove = 1,
    }

    public readonly struct RefStatOp
    {
        public StatOpKind Kind { get; }

        public int Slot { get; }

        public int Stat { get; }

        public long Handle { get; }

        public ShapeKind Shape { get; }

        public int Value { get; }

        public StatLayer Layer { get; }

        public RefStatOp(StatOpKind kind, int slot, int stat, long handle, ShapeKind shape, int value)
            : this(kind, slot, stat, handle, shape, value, StatLayer.Core)
        {
        }

        public RefStatOp(StatOpKind kind, int slot, int stat, long handle, ShapeKind shape, int value, StatLayer layer)
        {
            Kind = kind;
            Slot = slot;
            Stat = stat;
            Handle = handle;
            Shape = shape;
            Value = value;
            Layer = layer;
        }

        public static int Compare(RefStatOp first, RefStatOp second)
        {
            if (first.Slot != second.Slot)
            {
                return first.Slot < second.Slot ? -1 : 1;
            }

            if (first.Handle != second.Handle)
            {
                return first.Handle < second.Handle ? -1 : 1;
            }

            if (first.Kind != second.Kind)
            {
                return first.Kind < second.Kind ? -1 : 1;
            }

            if (first.Layer != second.Layer)
            {
                return first.Layer < second.Layer ? -1 : 1;
            }

            if (first.Stat != second.Stat)
            {
                return first.Stat < second.Stat ? -1 : 1;
            }

            if (first.Shape != second.Shape)
            {
                return first.Shape < second.Shape ? -1 : 1;
            }

            return first.Value < second.Value ? -1 : first.Value > second.Value ? 1 : 0;
        }
    }

    public sealed class StatOpOrder : IComparer<RefStatOp>
    {
        public static readonly StatOpOrder Canonical = new StatOpOrder();

        public int Compare(RefStatOp x, RefStatOp y) => RefStatOp.Compare(x, y);
    }

    public sealed class FrameInputs
    {
        public List<StructuralCommand> Commands { get; } = new List<StructuralCommand>();

        public List<RefStatOp> StatOps { get; } = new List<RefStatOp>();

        public List<RefRecord> Records { get; } = new List<RefRecord>();

        public FrameInputs Send(int target, int source, int kind, int channel, int amount, int hits = 1, RecordOrigin origin = RecordOrigin.Direct)
        {
            Records.Add(new RefRecord(target, source, kind, channel, origin, 0, amount, hits));
            return this;
        }

        public FrameInputs Spawn(int slot, int template)
        {
            Commands.Add(StructuralCommand.Spawn(slot, template));
            return this;
        }

        public FrameInputs Kill(int slot)
        {
            Commands.Add(StructuralCommand.Kill(slot));
            return this;
        }
    }

    public readonly struct OutwardEvent
    {
        public int Event { get; }

        public int Slot { get; }

        public bool Entered { get; }

        public int DenseCount { get; }

        public OutwardEvent(int raised, int slot, bool entered, int denseCount)
        {
            Event = raised;
            Slot = slot;
            Entered = entered;
            DenseCount = denseCount;
        }

        public bool IsDense => DenseCount > 0;
    }

    public sealed class StepFaults
    {
        public int DroppedDead;
        public int DroppedZero;
        public int DroppedMalformed;
        public int DepthFault;
        public int RouteFault;
        public int EmitNowhere;
        public int CommandFault;
        public int StatOpFault;
        public int OverflowLevel;
    }
}
