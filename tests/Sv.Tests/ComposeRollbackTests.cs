using System;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.Tests;

// Atom 5.3 composed half (roadmap issues/0058): the game-level undo window rolls board,
// input ring and event streams back together; rollback equals loading the snapshot saved
// at that completed frame (law L12), and re-driving the same inputs reproduces the same
// frames bit-identically.

public unsafe class ComposeRollbackTests
{
    struct HitEvent
    {
        public int Slot;

        public int Amount;
    }

    struct RollbackWorld : IFrameWorld
    {
        public void Reconcile(Game game)
        {
        }

        public void DispatchEvent(Game game, ushort typeId, void* payload)
        {
            if (typeId == 0)
            {
                var hit = (HitEvent*)payload;
                game.Board.AddModifier(hit->Slot, game.Board.Schema.Stat("power"), new Modifier(ModifierKind.Added, hit->Amount));
            }
        }

        public void RunIntrinsics(Game game)
        {
        }

        public unsafe void RunConditions(Game game)
        {
            var channel = game.Channel<HitEvent>();
            fixed (HitEvent* evt = stackalloc HitEvent[1])
            {
                evt[0] = new HitEvent { Slot = (int)(game.Input.Tick % 16), Amount = 1 + (int)(game.Input.Tick % 3) };
                channel.Write(evt[0]);
            }
        }
    }

    static GameHost<EventLink<HitEvent, Chain>, Chain, Chain, RollbackWorld> Build()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Stat("resolve", 5f).Build();
        var input = InputState.Create(2, 1, 16);
        var host = Game.Create(schema, 32).Event<HitEvent>().Build(new RollbackWorld(), input);
        for (var i = 0; i < 16; i++)
        {
            host.Game.Board.Acquire();
        }

        host.Game.Board.Recalculate();
        return host;
    }

    static void Drive(GameHost<EventLink<HitEvent, Chain>, Chain, Chain, RollbackWorld> host, uint[] axes, ulong[] buttons, uint seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        axes[0] = seed;
        axes[1] = seed * 3u;
        buttons[0] = (ulong)seed << 32 | seed;
        fixed (uint* axesBits = axes)
        fixed (ulong* words = buttons)
        {
            host.Frame.Tick(1f / 60f, axesBits, words);
        }
    }

    [Fact]
    public void Rollback_MatchesTheGameSnapshotAtTheMark()
    {
        using var host = Build();
        var game = host.Game;
        game.BeginUndoWindow(8);
        var marks = new SvState[9];
        var axes = new uint[2];
        var buttons = new ulong[1];
        marks[0] = new SvState();
        game.SaveState(marks[0]);
        var inputs = new (uint[] Axes, ulong[] Buttons)[8];
        for (var frame = 0; frame < 8; frame++)
        {
            inputs[frame] = (new uint[2], new ulong[1]);
            Drive(host, inputs[frame].Axes, inputs[frame].Buttons, 0x9E3779B9u * (uint)(frame + 1));
            marks[frame + 1] = new SvState();
            game.SaveState(marks[frame + 1]);
        }

        Assert.Equal(8, host.UndoFrames);
        for (var depth = 1; depth <= 8; depth++)
        {
            host.Rollback(depth);
            using var after = new SvState();
            game.SaveState(after);
            Assert.True(marks[8 - depth].Bytes.SequenceEqual(after.Bytes), $"depth {depth}: composed state diverges from the mark");
            for (var frame = 8 - depth; frame < 8; frame++)
            {
                Drive(host, inputs[frame].Axes, inputs[frame].Buttons, 0x9E3779B9u * (uint)(frame + 1));
            }

            using var resim = new SvState();
            game.SaveState(resim);
            Assert.True(marks[8].Bytes.SequenceEqual(resim.Bytes), $"depth {depth}: resim diverges from the original run");
        }

        game.EndUndoWindow();
    }

    [Fact]
    public void Rollback_RestoresEventsPendingDrain()
    {
        using var host = Build();
        var game = host.Game;
        var axes = new uint[2];
        var buttons = new ulong[1];
        game.BeginUndoWindow(4);
        Drive(host, axes, buttons, 1);
        var beforeDrain = new SvState();
        game.SaveState(beforeDrain);
        Drive(host, axes, buttons, 2);
        Drive(host, axes, buttons, 3);
        host.Rollback(2);
        var afterRollback = new SvState();
        game.SaveState(afterRollback);
        Assert.True(beforeDrain.Bytes.SequenceEqual(afterRollback.Bytes), "rollback must restore the events published at the mark");
        game.EndUndoWindow();
    }

    [Fact]
    public void RollbackBounds_AndWindowClosedByLoad()
    {
        using var host = Build();
        var game = host.Game;
        var axes = new uint[2];
        var buttons = new ulong[1];
        game.BeginUndoWindow(2);
        Drive(host, axes, buttons, 1);
        Drive(host, axes, buttons, 2);
        Drive(host, axes, buttons, 3);
        Assert.Equal(2, host.UndoFrames);
        Assert.Throws<ArgumentOutOfRangeException>(() => host.Rollback(3));

        host.Rollback(2);
        Drive(host, axes, buttons, 1);
        var saved = new SvState();
        game.SaveState(saved);
        game.LoadState(saved);
        Assert.Equal(0, host.UndoFrames);
        Assert.Throws<InvalidOperationException>(() => host.Rollback(1));
    }
}
