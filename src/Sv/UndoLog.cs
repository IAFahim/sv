using System;
using System.Runtime.InteropServices;

namespace Sv;

internal enum UndoArrayId
{
    Records,
    Expiries,
    DirtyTouched,
    SetBuckets,
    SetStat,
    SetHash,
    SetComputed,
    SetContentOffset,
    SetContentCount,
    SetContentSpan,
    SetRefCount,
    SetFolded,
    SetFreeNext,
    SetFreedAt,
    ContentArena,
    BlockBuckets,
    BlockSets,
    BlockHash,
    BlockRefCount,
    BlockFreeNext,
}

internal unsafe struct UndoQuarantine
{
    public UndoArrayId Id;

    public bool Fresh;

    public void* Pointer;

    public int Bytes;

    public int Frame;

    public int First;

    public int Last;
}

internal sealed unsafe class UndoLog : IDisposable
{
    struct Frame
    {
        public byte* Entries;

        public int Used;

        public int Capacity;
    }

    readonly Frame[] frames;

    readonly int window;

    int top = -1;

    int bottom;

    UndoQuarantine[] quarantine;

    int quarantineCount;

    int* order;

    int orderCapacity;

    bool disposed;

    public UndoLog(int window)
    {
        if (window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "sv: undo window must hold at least one frame");
        }

        this.window = window;
        frames = new Frame[window];
        quarantine = new UndoQuarantine[8];
    }

    public int Window => window;

    public int Frames => top - bottom + 1;

    public int Top => top;

    public int Bottom => bottom;

    public int QuarantineCount => quarantineCount;

    public void OpenFrame()
    {
        if (top - bottom + 1 == window)
        {
            bottom++;
        }

        top++;
        ref var frame = ref frames[top % window];
        frame.Used = 0;
    }

    public void Note(void* address, int bytes)
    {
        ref var frame = ref frames[top % window];
        var need = sizeof(void*) + sizeof(int) + bytes;
        if (frame.Used + need > frame.Capacity)
        {
            var capacity = frame.Capacity == 0 ? 256 : frame.Capacity;
            while (frame.Used + need > capacity)
            {
                capacity *= 2;
            }

            frame.Entries = frame.Entries == null
                ? (byte*)NativeMemory.Alloc((nuint)capacity)
                : (byte*)NativeMemory.Realloc(frame.Entries, (nuint)capacity);
            frame.Capacity = capacity;
        }

        var cursor = frame.Entries + frame.Used;
        *(void**)cursor = address;
        *(int*)(cursor + sizeof(void*)) = bytes;
        Buffer.MemoryCopy(address, cursor + sizeof(void*) + sizeof(int), bytes, bytes);
        frame.Used += need;
    }

    public void NoteRange<T>(T* address, int count) where T : unmanaged
    {
        if (count > 0)
        {
            Note(address, count * sizeof(T));
        }
    }

    public T* ReallocTracked<T>(NativeArena memory, UndoArrayId id, T* old, int oldCount, int newCount) where T : unmanaged
    {
        var fresh = (T*)memory.AllocBytes(newCount * sizeof(T));
        var shared = oldCount < newCount ? oldCount : newCount;
        if (shared > 0)
        {
            Buffer.MemoryCopy(old, fresh, (long)shared * sizeof(T), (long)shared * sizeof(T));
        }

        NoteReplaced(id, old, oldCount * sizeof(T), 0, top);
        NoteReplaced(id, fresh, newCount * sizeof(T), top, int.MaxValue);
        return fresh;
    }

    void NoteReplaced(UndoArrayId id, void* pointer, int bytes, int first, int last)
    {
        if (pointer == null || bytes == 0)
        {
            return;
        }

        for (var i = 0; i < quarantineCount; i++)
        {
            ref var existing = ref quarantine[i];
            if (!existing.Fresh && existing.Pointer == pointer)
            {
                existing.Last = last;
                return;
            }
        }

        AppendQuarantine(new UndoQuarantine { Id = id, Fresh = false, Pointer = pointer, Bytes = bytes, Frame = top, First = first, Last = last });
    }

    public void QuarantineFresh(void* pointer, int bytes)
    {
        AppendQuarantine(new UndoQuarantine { Id = 0, Fresh = true, Pointer = pointer, Bytes = bytes, Frame = top });
    }

    void AppendQuarantine(UndoQuarantine entry)
    {
        if (quarantineCount == quarantine.Length)
        {
            Array.Resize(ref quarantine, quarantine.Length * 2);
        }

        quarantine[quarantineCount++] = entry;
    }

    public void CommitFramesBelow(int ordinal)
    {
        for (var i = 0; i < quarantineCount; i++)
        {
            if (quarantine[i].Fresh && quarantine[i].Frame < ordinal)
            {
                quarantine[i] = quarantine[--quarantineCount];
                i--;
            }
        }
    }

    public void DropFreshFrom(int ordinal, NativeArena memory)
    {
        for (var i = 0; i < quarantineCount; i++)
        {
            if (!quarantine[i].Fresh || quarantine[i].Frame < ordinal)
            {
                continue;
            }

            memory.FreeBytes(quarantine[i].Pointer, quarantine[i].Bytes);
            quarantine[i] = quarantine[--quarantineCount];
            i--;
        }
    }

    public ref UndoQuarantine QuarantineAt(int index)
    {
        return ref quarantine[index];
    }

    public void RemoveQuarantineAt(int index)
    {
        quarantine[index] = quarantine[--quarantineCount];
    }

    public void UndoFrames(int fromOrdinal, int toOrdinal)
    {
        for (var ordinal = fromOrdinal; ordinal >= toOrdinal; ordinal--)
        {
            ref var frame = ref frames[ordinal % window];
            var entries = frame.Entries;
            var used = frame.Used;
            frame.Used = 0;
            if (entries == null || used == 0)
            {
                continue;
            }

            var count = 0;
            var cursor = 0;
            while (cursor < used)
            {
                var len = *(int*)(entries + cursor + sizeof(void*));
                if (len < 0 || len > 1 << 20 || cursor + sizeof(void*) + sizeof(int) + len > used)
                {
                    throw new InvalidOperationException($"sv: corrupt undo entry stream (frame {ordinal}, offset {cursor}, length {len})");
                }

                if (count == orderCapacity)
                {
                    GrowOrder(count, used - cursor);
                }

                order[count++] = cursor;
                cursor += sizeof(void*) + sizeof(int) + len;
            }

            for (var i = count - 1; i >= 0; i--)
            {
                var at = entries + order[i];
                var address = *(void**)at;
                var bytes = *(int*)(at + sizeof(void*));
                Buffer.MemoryCopy(at + sizeof(void*) + sizeof(int), address, bytes, bytes);
            }
        }
    }

    void GrowOrder(int count, int bytesLeft)
    {
        var needed = count + bytesLeft / (sizeof(void*) + sizeof(int)) + 1;
        var capacity = orderCapacity == 0 ? 128 : orderCapacity;
        while (capacity <= needed)
        {
            capacity *= 2;
        }

        order = order == null
            ? (int*)NativeMemory.Alloc((nuint)capacity * sizeof(int))
            : (int*)NativeMemory.Realloc(order, (nuint)capacity * sizeof(int));
        orderCapacity = capacity;
    }

    public void TruncateTo(int ordinal)
    {
        top = ordinal - 1;
        if (top < bottom - 1)
        {
            top = bottom - 1;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        for (var i = 0; i < window; i++)
        {
            if (frames[i].Entries != null)
            {
                NativeMemory.Free(frames[i].Entries);
                frames[i].Entries = null;
            }
        }

        if (order != null)
        {
            NativeMemory.Free(order);
            order = null;
        }

        disposed = true;
    }
}
