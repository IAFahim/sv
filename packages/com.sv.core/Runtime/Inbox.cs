using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sv
{
    public static unsafe class Inbox
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Send(long* inbox, int targetSlot, int amount) => Interlocked.Add(ref inbox[targetSlot], amount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Take(long* inbox, int slot, int local)
        {
            var total = IntrinsicLaw.Saturate(local + inbox[slot]);
            inbox[slot] = 0;
            return total;
        }
    }

    public sealed unsafe class InboxBuffer : IDisposable
    {
        readonly NativeArena arena = new NativeArena();

        long* cells;

        public InboxBuffer(int length)
        {
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(length), $"sv: inbox of {length} cells is empty");
            }

            Length = length;
            cells = arena.AllocAlignedZeroed<long>(length);
        }

        public int Length { get; }

        public long* Pointer
        {
            get
            {
                if (cells == null)
                {
                    throw new ObjectDisposedException(nameof(InboxBuffer));
                }

                return cells;
            }
        }

        public Column<long> Column => new Column<long>(Pointer, Length);

        public void Clear()
        {
            var at = Pointer;
            for (var i = 0; i < Length; i++)
            {
                at[i] = 0;
            }
        }

        public void Dispose()
        {
            if (cells == null)
            {
                return;
            }

            arena.FreeAligned(cells, Length);
            cells = null;
            arena.Dispose();
        }
    }
}
