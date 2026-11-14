using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    public interface IForward
    {
        int Forward(int slot, int channel);
    }

    public struct NoForward : IForward
    {
        public int Forward(int slot, int channel) => -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RouteTable
    {
        public ulong* Live;
        public ulong* Forwarding;
        public ulong* Channels;
        public int Chunks;

        public RouteTable(ulong* live, ulong* forwarding, int chunks)
            : this(live, forwarding, chunks, null)
        {
        }

        public RouteTable(ulong* live, ulong* forwarding, int chunks, ulong* channels)
        {
            Live = live;
            Forwarding = forwarding;
            Channels = channels;
            Chunks = chunks;
        }
    }

    public static unsafe class Route
    {
        public const int Kept = 0;
        public const int Moved = 1;
        public const int Dead = -1;
        public const int Fault = -2;
        public const int Malformed = -3;
        public const int MaxHops = 4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLive(in RouteTable table, int slot)
        {
            var chunk = slot >> EffectKey.RowBits;
            if ((uint)chunk >= (uint)table.Chunks)
            {
                return false;
            }

            return ((table.Live[chunk] >> (slot & EffectKey.RowMask)) & 1ul) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Deliver<TForward>(in RouteTable table, ref TForward forward, ref ulong key)
            where TForward : struct, IForward
        {
            var slot = EffectKey.Target(key);
            var chunk = slot >> EffectKey.RowBits;
            if ((uint)chunk >= (uint)table.Chunks)
            {
                return Dead;
            }

            var bit = 1ul << (slot & EffectKey.RowMask);
            if ((table.Live[chunk] & bit) == 0)
            {
                return Dead;
            }

            if (table.Channels != null)
            {
                var channel = EffectKey.Channel(key);
                var source = EffectKey.Source(key);
                if (((table.Channels[channel >> 6] >> (channel & 63)) & 1ul) == 0ul
                    || (source != EffectKey.NoSource && (uint)(source >> EffectKey.RowBits) >= (uint)table.Chunks))
                {
                    return Malformed;
                }
            }

            if (table.Forwarding == null || (table.Forwarding[chunk] & bit) == 0)
            {
                return Kept;
            }

            return Follow(table, ref forward, ref key, slot);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Follow<TForward>(in RouteTable table, ref TForward forward, ref ulong key, int slot)
            where TForward : struct, IForward
        {
            var channel = EffectKey.Channel(key);
            var at = slot;
            var hops = 0;
            while ((table.Forwarding[at >> EffectKey.RowBits] & (1ul << (at & EffectKey.RowMask))) != 0)
            {
                var next = forward.Forward(at, channel);
                if (next < 0)
                {
                    break;
                }

                if (hops == MaxHops)
                {
                    return Fault;
                }

                hops++;
                if (!IsLive(table, next))
                {
                    return Dead;
                }

                at = next;
            }

            if (at == slot)
            {
                return Kept;
            }

            key = EffectKey.WithTarget(key, at);
            return Moved;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Emit(ref EffectWriter next, ulong cause, int to, int emitterKind, int channel, Origin origin, int amount, int hits)
        {
            var depth = EffectKey.Depth(cause) + 1;
            if (depth > EffectKey.DepthLimit)
            {
                return false;
            }

            return next.Send(EffectKey.Pack(to, EffectKey.Target(cause), (byte)emitterKind, (byte)channel, origin, depth), amount, hits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Reflect(ref EffectWriter next, ulong cause, int emitterKind, int amount, int hits)
        {
            var source = EffectKey.Source(cause);
            if (source == EffectKey.NoSource)
            {
                return false;
            }

            return Emit(ref next, cause, source, emitterKind, EffectKey.Channel(cause), Origin.Reflected, amount, hits);
        }
    }
}
