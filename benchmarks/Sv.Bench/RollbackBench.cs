using System;
using System.Runtime.InteropServices;
using Sv;
using Sv.Compose;

// Roadmap 5.3 receipt: an 8-frame rollback plus the 8-frame resimulation on a full
// 1M-entity board at Δ = 128 (roadmap issues/0058 §2.2: 128 changes per frame — here 96
// modifier edits plus the 32 edits expiring from four frames ago). One iteration =
// Rollback(8) + 8 re-driven frames (same inputs, same edits), returning to the same top
// mark, so iterations are steady-state cycles.

struct RbHitEvent
{
    public int Slot;

    public int Amount;
}

struct RbWorld : IFrameWorld
{
    public StatId Power;

    public void Reconcile(Game game)
    {
    }

    public unsafe void DispatchEvent(Game game, ushort typeId, void* payload)
    {
        var hit = (RbHitEvent*)payload;
        game.Board.AddModifier(hit->Slot, Power, new Modifier(ModifierKind.Added, hit->Amount));
    }

    public void RunIntrinsics(Game game)
    {
    }

    public void RunConditions(Game game)
    {
    }
}

internal sealed unsafe class RollbackBench : BenchBase
{
    const int Entities = 1 << 20;

    const int Window = 8;

    const int Edits = 96;

    GameHost<EventLink<RbHitEvent, Chain>, Chain, Chain, RbWorld> host;

    Channel<RbHitEvent> channel;

    uint[] axes;

    ulong[] buttons;

    long tick;

    ulong markDigest;

    ulong checksum;

    internal static RollbackBench New()
    {
        return new RollbackBench();
    }

    RollbackBench()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Stat("resolve", 5f).Build();
        var world = new RbWorld { Power = schema.Stat("power") };
        host = Game.Create(schema, Entities).Event<RbHitEvent>().Build(world, InputState.Create(2, 1, 64));
        channel = host.Game.Channel<RbHitEvent>();
        axes = new uint[2];
        buttons = new ulong[1];
        var board = host.Game.Board;
        for (var i = 0; i < Entities; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        host.BeginUndoWindow(Window);
        for (var frame = 0; frame < Window; frame++)
        {
            Frame();
        }

        markDigest = host.Game.Board.Digest;
    }

    void Frame()
    {
        var seed = 0x9E3779B9u * (uint)(tick + 1);
        var board = host.Game.Board;
        var power = host.World.Power;
        var evt = new RbHitEvent { Amount = 1 + (int)(seed % 5) };
        for (var i = 0; i < Edits / 3; i++)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            evt.Slot = (int)(seed % (uint)Entities);
            channel.Write(evt);
            board.AddModifier((int)(seed % (uint)Entities), power, new Modifier(ModifierKind.Multi, 1 + (seed % 3)), 0, tick + 4);
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            board.AddModifier((int)(seed % (uint)Entities), power, new Modifier(ModifierKind.Added, 1 + (seed % 3)));
        }

        board.ExpireModifiers(tick);
        axes[0] = seed;
        axes[1] = seed * 3u;
        buttons[0] = ((ulong)seed << 32) | seed;
        fixed (uint* axesBits = axes)
        fixed (ulong* words = buttons)
        {
            host.Frame.Tick(1f / 60f, axesBits, words);
        }

        tick++;
    }

    public override long Frame(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            host.Rollback(Window);
            tick -= Window;
            for (var frame = 0; frame < Window; frame++)
            {
                Frame();
            }

            checksum = checksum * 31 + host.Game.Board.Digest;
        }

        return (long)checksum;
    }

    public override string Verify()
    {
        return host.Game.Board.Digest == markDigest ? "ok" : $"digest drifted ({host.Game.Board.Digest:X16} vs {markDigest:X16})";
    }

    public override int RowsPerFrame => 1;

    public override void Dispose()
    {
        host.Dispose();
    }
}
