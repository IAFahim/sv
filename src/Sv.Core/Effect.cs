using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    public enum Origin : byte
    {
        Direct = 0,
        Timeline = 1,
        Reflected = 2,
        Triggered = 3,
    }

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

    [StructLayout(LayoutKind.Sequential, Size = 20)]
    public readonly struct EffectFields
    {
        public readonly int Target;
        public readonly int Source;
        public readonly byte Kind;
        public readonly byte Channel;
        public readonly Origin Origin;
        public readonly byte Depth;
        public readonly int Amount;
        public readonly int Hits;

        public EffectFields(int target, int source, byte kind, byte channel, Origin origin, int depth, int amount, int hits)
        {
            Target = target & EffectKey.SlotMask;
            Source = source & EffectKey.SlotMask;
            Kind = kind;
            Channel = channel;
            Origin = (Origin)((byte)origin & 3);
            Depth = (byte)(depth & 3);
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
        public const int RowBits = 6;
        public const int Rows = 1 << RowBits;
        public const int RowMask = Rows - 1;
        public const int ChunkLimit = 1 << (SlotBits - RowBits);
        public const int TargetShift = 42;
        public const int ChunkShift = TargetShift + RowBits;
        public const int SourceShift = 20;
        public const int KindShift = 12;
        public const int ChannelShift = 4;
        public const int OriginShift = 2;
        public const ulong TargetField = (ulong)SlotMask << TargetShift;
        public const ulong SourceField = (ulong)SlotMask << SourceShift;
        public const ulong KindField = 0xFFul << KindShift;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Pack(int target, int source, byte kind, byte channel, Origin origin, int depth) =>
            ((ulong)(uint)(target & SlotMask) << TargetShift)
            | ((ulong)(uint)(source & SlotMask) << SourceShift)
            | ((ulong)kind << KindShift)
            | ((ulong)channel << ChannelShift)
            | ((ulong)((uint)origin & 3u) << OriginShift)
            | (ulong)(uint)(depth & 3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Effect Pack(in EffectFields fields) =>
            new Effect(Pack(fields.Target, fields.Source, fields.Kind, fields.Channel, fields.Origin, fields.Depth), fields.Amount, fields.Hits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EffectFields Unpack(in Effect effect) =>
            new EffectFields(Target(effect.Key), Source(effect.Key), Kind(effect.Key), Channel(effect.Key), OriginOf(effect.Key), Depth(effect.Key), effect.Amount, effect.Hits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Target(ulong key) => (int)(key >> TargetShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Source(ulong key) => (int)(key >> SourceShift) & SlotMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Kind(ulong key) => (byte)(key >> KindShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Channel(ulong key) => (byte)(key >> ChannelShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Origin OriginOf(ulong key) => (Origin)((key >> OriginShift) & 3ul);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Depth(ulong key) => (int)(key & 3ul);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Chunk(ulong key) => (int)(key >> ChunkShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Row(ulong key) => (int)(key >> TargetShift) & RowMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WithTarget(ulong key, int target) => (key & ~TargetField) | ((ulong)(uint)(target & SlotMask) << TargetShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WithoutSource(ulong key) => key | SourceField;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WithoutKind(ulong key) => key & ~KindField;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SlotOf(int chunk, int row) => (chunk << RowBits) | (row & RowMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ChunkOf(int slot) => slot >> RowBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RowOf(int slot) => slot & RowMask;
    }

    public static class EffectLaw
    {
        public const long TermLimit = 1L << 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Term(int taken, int hits)
        {
            var product = (long)taken * hits;
            return product > TermLimit ? TermLimit : product < -TermLimit ? -TermLimit : product;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Saturate32(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int StoredDelta(long targetSum) => Saturate32(targetSum);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Precedes(in Effect first, in Effect second) =>
            first.Key < second.Key | (first.Key == second.Key & first.Amount < second.Amount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SameKeyAndAmount(in Effect first, in Effect second) => first.Key == second.Key & first.Amount == second.Amount;

        public static int Compare(in Effect first, in Effect second) =>
            first.Key < second.Key ? -1 : first.Key > second.Key ? 1 : first.Amount < second.Amount ? -1 : first.Amount > second.Amount ? 1 : 0;

        public static int Compare(in EffectFields first, in EffectFields second)
        {
            if (first.Target != second.Target)
            {
                return first.Target < second.Target ? -1 : 1;
            }

            if (first.Source != second.Source)
            {
                return first.Source < second.Source ? -1 : 1;
            }

            if (first.Kind != second.Kind)
            {
                return first.Kind < second.Kind ? -1 : 1;
            }

            if (first.Channel != second.Channel)
            {
                return first.Channel < second.Channel ? -1 : 1;
            }

            if (first.Origin != second.Origin)
            {
                return first.Origin < second.Origin ? -1 : 1;
            }

            if (first.Depth != second.Depth)
            {
                return first.Depth < second.Depth ? -1 : 1;
            }

            return first.Amount < second.Amount ? -1 : first.Amount > second.Amount ? 1 : 0;
        }
    }
}
