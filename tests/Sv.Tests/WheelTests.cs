using System;
using System.Collections.Generic;
using Sv;
using Xunit;

public unsafe class WheelTests
{
    static Schema Schema() => SchemaBuilder.Create().Stat("speed", 10f).Build();

    [Fact]
    public void CascadeAcrossLevels_FiresAtExactTicks()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var delays = new long[] { 1, 255, 256, 257, 65535, 65536, 65537, (1 << 24) - 1, 1 << 24 };
        var slots = new int[delays.Length];
        for (var i = 0; i < delays.Length; i++)
        {
            slots[i] = board.Acquire();
            board.AddModifier(slots[i], speed, new Modifier(ModifierKind.Added, 1f), 0, delays[i]);
        }

        Assert.Equal(delays.Length, board.PendingExpiries);
        foreach (var delay in delays)
        {
            Assert.Equal(1, board.ExpireModifiers(delay));
            Assert.Equal(0, board.ExpireModifiers(delay));
        }

        Assert.Equal(0, board.PendingExpiries);
    }

    [Fact]
    public void SameBucketDifferentLaps_LongEntryDoesNotFireEarly()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 0, 300);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 2f), 0, 300 + 65536);
        Assert.Equal(2, board.PendingExpiries);
        Assert.Equal(1, board.ExpireModifiers(300));
        Assert.Equal(1, board.PendingExpiries);
        Assert.Equal(0, board.ExpireModifiers(300 + 65535));
        Assert.Equal(1, board.PendingExpiries);
        Assert.Equal(1, board.ExpireModifiers(300 + 65536));
        Assert.Equal(0, board.PendingExpiries);
    }

    [Fact]
    public void LongJump_DrainsEverythingAndReturnsToBase()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        for (var i = 0; i < 32; i++)
        {
            var slot = board.Acquire();
            board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 3f), 0, 100 + (i % 7) + i * 65537L);
        }

        Assert.Equal(32, board.PendingExpiries);
        board.Recalculate();
        Assert.Equal(32, board.ExpireModifiers(long.MaxValue - 1));
        Assert.Equal(0, board.PendingExpiries);
        board.Recalculate();
        for (var slot = 0; slot < board.Capacity; slot++)
        {
            if (board.Output(speed)[slot] != 0f)
            {
                Assert.Equal(10f, board.Output(speed)[slot]);
            }
        }
    }

    [Fact]
    public void OverdueInserts_FireOnNextCallInTickOrder()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        Assert.Equal(0, board.ExpireModifiers(50));
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 0, 50);
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 2f), 0, 40);
        Assert.Equal(2, board.PendingExpiries);
        Assert.Equal(2, board.ExpireModifiers(60));
        Assert.Equal(0, board.PendingExpiries);
    }

    [Fact]
    public void RemovalPaths_UnlinkImmediately()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        var b = board.Acquire();
        var keep = board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 0, 9);
        var byHandle = board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 1, 9);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 1f), 2, 9);
        Assert.Equal(3, board.PendingExpiries);
        board.RemoveModifier(a, speed, byHandle);
        Assert.Equal(2, board.PendingExpiries);
        Assert.Equal(1, board.RemoveSource(b, 2));
        Assert.Equal(1, board.PendingExpiries);
        board.Release(a);
        Assert.Equal(0, board.PendingExpiries);
        Assert.Equal(0, board.ExpireModifiers(9));
    }

    [Fact]
    public void SaveLoad_RebuildsPendingExpiries()
    {
        using var board = Board.Create(Schema(), 64);
        using var state = new SvState();
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 0, 12);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Multi, 1f), 0, 12);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 4f), 0, 300 + 65536L);
        board.Recalculate();
        board.SaveState(state);
        using var fresh = Board.Create(Schema(), 64);
        fresh.LoadState(state);
        Assert.Equal(board.PendingExpiries, fresh.PendingExpiries);
        Assert.Equal(2, fresh.ExpireModifiers(12));
        Assert.Equal(1, fresh.PendingExpiries);
        board.ExpireModifiers(12);
        using var again = new SvState();
        fresh.SaveState(again);
        board.SaveState(state);
        Assert.Equal(state.Bytes.ToArray(), again.Bytes.ToArray());
    }

    [Fact]
    public void UndoRollback_RestoresPendingExpiry()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        board.BeginUndoWindow(2);
        board.BeginUndoFrame();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 3f), 0, 5);
        Assert.Equal(1, board.PendingExpiries);
        board.BeginUndoFrame();
        Assert.Equal(1, board.ExpireModifiers(5));
        board.Recalculate();
        Assert.Equal(10f, board.Output(speed)[a]);
        board.Rollback(1);
        Assert.Equal(1, board.PendingExpiries);
        board.Recalculate();
        Assert.Equal(13f, board.Output(speed)[a]);
        Assert.Equal(1, board.ExpireModifiers(5));
        board.Recalculate();
        Assert.Equal(10f, board.Output(speed)[a]);
    }

    [Fact]
    public void KeyframeRestore_RestoresPendingExpiries()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        board.Recalculate();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 3f), 0, 40 + 65536L);
        Assert.Equal(1, board.PendingExpiries);
        board.Recalculate();
        Assert.Equal(13f, board.Output(speed)[a]);
        var dirty = new int[board.Capacity];
        Array.Fill(dirty, -1);
        dirty[a] = a;
        board.RenumberSlots(dirty, 1);
        Assert.Equal(1, board.PendingExpiries);
        board.Recalculate();
        Assert.Equal(13f, board.Output(speed)[a]);
    }

    [Fact]
    public void SaveLoad_RoundTripsChainedAndUnchainedRecords()
    {
        using var board = Board.Create(Schema(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        var b = board.Acquire();
        board.Recalculate();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 0, 500);
        var forever = board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(1, board.PendingExpiries);
        using var state = new SvState();
        board.SaveState(state);
        using var loaded = Board.Create(board.Schema, 64);
        loaded.LoadState(state);
        Assert.Equal(1, loaded.PendingExpiries);
        loaded.RemoveModifier(b, speed, forever);
        Assert.Equal(1, loaded.PendingExpiries);
        loaded.Recalculate();
        Assert.Equal(10f, loaded.Output(speed)[b]);
        Assert.Equal(11f, loaded.Output(speed)[a]);
        Assert.Equal(1, loaded.ExpireModifiers(500));
        Assert.Equal(0, loaded.PendingExpiries);
        loaded.Recalculate();
        Assert.Equal(10f, loaded.Output(speed)[a]);
    }

    [Fact]
    public void KeyframeRefolds_RecycledRecordsStayOffTheWheel()
    {
        using var source = Board.Create(Schema(), 64);
        var speed = source.Schema.Stat("speed");
        var slot = source.Acquire();
        source.Recalculate();
        using var baseline = new SvState();
        source.SaveState(baseline);
        using var receiver = Board.Create(source.Schema, 64);
        receiver.LoadState(baseline);
        using var keyframe = new SvState();

        source.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 1f), 0, 9000);
        source.Recalculate();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);
        Assert.Equal(1, receiver.PendingExpiries);
        Assert.Equal(source.Digest, receiver.Digest);

        source.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 4f));
        source.Recalculate();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);
        Assert.Equal(1, receiver.PendingExpiries);
        Assert.Equal(source.Digest, receiver.Digest);

        source.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 8f));
        source.Recalculate();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);
        Assert.Equal(1, receiver.PendingExpiries);
        Assert.Equal(source.Digest, receiver.Digest);
        Assert.Equal(23f, receiver.Output(speed)[slot]);
    }

    [Fact]
    public void SameProgram_Twice_ProducesIdenticalSaves()
    {
        var first = RunChurn(1234);
        var second = RunChurn(1234);
        Assert.Equal(first, second);
    }

    static byte[] RunChurn(int seed)
    {
        var rng = new Random(seed);
        using var board = Board.Create(Schema(), 128);
        var speed = board.Schema.Stat("speed");
        var handles = new List<ModifierHandle>[128];
        for (var slot = 0; slot < 128; slot++)
        {
            handles[slot] = new List<ModifierHandle>();
            board.Acquire();
        }

        for (var tick = 1; tick <= 300; tick++)
        {
            for (var op = 0; op < 20; op++)
            {
                var roll = rng.Next(3);
                var slot = rng.Next(128);
                if (roll == 0)
                {
                    var expiry = rng.Next(4) == 0 ? tick + (1L << 24) + rng.Next(1000) : tick + rng.Next(0, 70000);
                    handles[slot].Add(board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 1f), 0, expiry));
                }
                else if (roll == 1 && handles[slot].Count > 0)
                {
                    var at = rng.Next(handles[slot].Count);
                    board.RemoveModifier(slot, speed, handles[slot][at]);
                    handles[slot].RemoveAt(at);
                }
                else
                {
                    board.RemoveSource(slot, 0);
                    handles[slot].Clear();
                }
            }

            board.ExpireModifiers(tick);
            board.Recalculate();
        }

        using var state = new SvState();
        board.SaveState(state);
        return state.Bytes.ToArray();
    }
}
