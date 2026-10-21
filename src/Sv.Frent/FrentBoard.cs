using System;
using Frent;

namespace Sv.Frent;

public readonly struct SvSlot
{
    public readonly int Slot;

    public SvSlot(int slot)
    {
        Slot = slot;
    }
}

public sealed class FrentBoard : IDisposable
{
    public Board Board { get; }

    public FrentBoard(Schema schema, int capacity)
    {
        Board = Board.Create(schema, capacity);
    }

    public int Bind(Entity entity)
    {
        if (entity.Has<SvSlot>())
        {
            var existing = entity.Get<SvSlot>().Slot;
            if (!Board.IsLive(existing))
            {
                throw new InvalidOperationException($"sv.frent: entity carries stale SvSlot {existing} — reconcile or rebuild the binding before binding again");
            }

            return existing;
        }

        var slot = Board.Acquire();
        try
        {
            entity.Add(new SvSlot(slot));
        }
        catch
        {
            Board.Release(slot);
            throw;
        }

        return slot;
    }

    public void Unbind(Entity entity)
    {
        var slot = entity.Get<SvSlot>().Slot;
        Board.Release(slot);
        entity.Remove<SvSlot>();
    }

    public bool TrySlot(Entity entity, out int slot)
    {
        if (entity.Has<SvSlot>())
        {
            slot = entity.Get<SvSlot>().Slot;
            return true;
        }

        slot = -1;
        return false;
    }

    public void Dispose()
    {
        Board.Dispose();
    }
}
