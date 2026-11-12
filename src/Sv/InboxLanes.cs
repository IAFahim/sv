using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sv
{
    public unsafe struct InboxLane
    {
        long* inbox;
        int* slots;
        long* sums;
        int* used;
        int usedCount;
        int mask;
        int shift;
        int sent;
        int evicted;
        int bypass;

        public const int ProbeSends = 4096;

        public const int BypassSends = 61440;

        internal InboxLane(long* inbox, int* slots, long* sums, int* used, int cells)
        {
            this.inbox = inbox;
            this.slots = slots;
            this.sums = sums;
            this.used = used;
            usedCount = 0;
            sent = 0;
            evicted = 0;
            bypass = 0;
            mask = cells - 1;
            shift = 32 - Bits.Tzcnt((uint)cells);
            for (var i = 0; i < cells; i++)
            {
                slots[i] = -1;
            }
        }

        public int Pending => usedCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send(int targetSlot, int amount)
        {
            if (bypass > 0)
            {
                bypass--;
                Interlocked.Add(ref inbox[targetSlot], amount);
                return;
            }

            if (++sent == ProbeSends)
            {
                bypass = evicted > ProbeSends / 2 ? BypassSends : 0;
                sent = 0;
                evicted = 0;
            }

            var at = (int)(((uint)targetSlot * 0x9E3779B1u) >> shift) & mask;
            var held = slots[at];
            if (held == targetSlot)
            {
                sums[at] += amount;
                return;
            }

            if (held < 0)
            {
                slots[at] = targetSlot;
                sums[at] = amount;
                used[usedCount++] = at;
                return;
            }

            evicted++;
            Interlocked.Add(ref inbox[held], sums[at]);
            slots[at] = targetSlot;
            sums[at] = amount;
        }

        public bool Bypassing => bypass > 0;

        public void Flush()
        {
            for (var i = 0; i < usedCount; i++)
            {
                var at = used[i];
                Interlocked.Add(ref inbox[slots[at]], sums[at]);
                slots[at] = -1;
            }

            usedCount = 0;
            sent = 0;
            evicted = 0;
            bypass = 0;
        }
    }

    public sealed unsafe class InboxLanes : IDisposable
    {
        public const int DefaultCells = 1024;

        readonly NativeArena arena = new NativeArena();

        InboxLane* lanes;

        readonly int* slots;

        readonly long* sums;

        readonly int* used;

        readonly int stride;

        public InboxLanes(InboxBuffer inbox, int workers, int cells = DefaultCells)
        {
            if (inbox == null)
            {
                throw new ArgumentNullException(nameof(inbox));
            }

            if (workers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workers), $"sv: inbox lanes need at least one worker, got {workers}");
            }

            if (cells < 16 || cells > 1 << 20 || (cells & (cells - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cells), $"sv: inbox lane cells must be a power of two in [16, 1048576], got {cells}");
            }

            Workers = workers;
            Cells = cells;
            stride = cells + 16;
            var total = checked(workers * stride);
            lanes = arena.AllocAlignedZeroed<InboxLane>(workers * 2);
            slots = arena.AllocAlignedZeroed<int>(total);
            sums = arena.AllocAlignedZeroed<long>(total);
            used = arena.AllocAlignedZeroed<int>(total);
            var cellsPointer = inbox.Pointer;
            for (var w = 0; w < workers; w++)
            {
                lanes[w * 2] = new InboxLane(cellsPointer, slots + (nint)w * stride, sums + (nint)w * stride, used + (nint)w * stride, cells);
            }
        }

        public int Workers { get; }

        public int Cells { get; }

        public InboxLane* Lane(int worker)
        {
            if (lanes == null)
            {
                throw new ObjectDisposedException(nameof(InboxLanes));
            }

            if ((uint)worker >= (uint)Workers)
            {
                throw new ArgumentOutOfRangeException(nameof(worker), $"sv: worker {worker} outside [0, {Workers})");
            }

            return lanes + worker * 2;
        }

        public void FlushAll()
        {
            for (var w = 0; w < Workers; w++)
            {
                Lane(w)->Flush();
            }
        }

        public void Dispose()
        {
            if (lanes == null)
            {
                return;
            }

            var total = Workers * stride;
            arena.FreeAligned(lanes, Workers * 2);
            arena.FreeAligned(slots, total);
            arena.FreeAligned(sums, total);
            arena.FreeAligned(used, total);
            lanes = null;
            arena.Dispose();
        }
    }
}
