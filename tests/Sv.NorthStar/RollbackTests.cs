using System.Runtime.InteropServices;
using Sv.Compose;
using Xunit;

namespace Sv.NorthStar;

public sealed unsafe class RollbackTests
{
    const int Rows = 256;

    public struct Gained
    {
        public int Slot;

        public int Amount;
    }

    struct GainedMaker : IEventMaker<Gained>
    {
        public Gained Make(int row, int value) => new() { Slot = row, Amount = value };
    }

    struct RollWorld : IFrameWorld
    {
        public int* Mana;

        public StatId MaxMana;

        public void Reconcile(Game game)
        {
        }

        public void DispatchEvent(Game game, ushort typeId, void* payload)
        {
            var evt = (Gained*)payload;
            if (evt->Amount > 3)
            {
                game.Board.AddModifier(evt->Slot, MaxMana, new Modifier(ModifierKind.Added, 1.5f), 1, game.Input.Tick + 3);
            }
        }

        public void RunIntrinsics(Game game)
        {
            game.Board.ExpireModifiers(game.Input.Tick);
            game.Board.RunIntrinsicsChunk(Mana, (int)(game.Input.AxisBits(0) % 9) - 2, Clamp.StatMax(game.Board, MaxMana), 0, Rows, game.Applied);
        }

        public void RunConditions(Game game)
        {
            var maker = new GainedMaker();
            game.Streams.EmitFromNonZero(game.Channel<Gained>(), game.Applied, 0, Rows, ref maker);
            NativeMemory.Clear(game.Applied, Rows * sizeof(int));
        }
    }

    [Fact]
    public void GameLoad_ResimulatesFramesBitIdentically()
    {
        var schema = SchemaBuilder.Create().Stat("max_mana", 20f).Intrinsic("mana").Build();
        using var input = InputState.Create(1, 1, 64);
        var mana = (int*)NativeMemory.AllocZeroed(Rows * sizeof(int));
        var savedMana = (int*)NativeMemory.AllocZeroed(Rows * sizeof(int));
        var body = (byte*)NativeMemory.Alloc(1 << 20);
        try
        {
            var world = new RollWorld { Mana = mana, MaxMana = schema.Stat("max_mana") };
            using var host = Game.Create(schema, Rows).Event<Gained>().Build(world, input);
            for (var i = 0; i < Rows; i++)
            {
                host.Game.Board.Acquire();
            }

            using var state = new SvState();
            var axes = stackalloc uint[1];
            var buttons = stackalloc ulong[1];
            var sawTimed = false;
            ulong Step(int frame)
            {
                axes[0] = (uint)(frame * 2654435761u);
                buttons[0] = (ulong)frame;
                host.Frame.Tick(1f / 60f, axes, buttons);
                sawTimed |= host.Game.Board.PendingExpiries > 0 || host.Game.Board.SetCount > 1;
                var digest = Tape.WriteBody(host.Game, 1f / 60f, body);
                for (var i = 0; i < Rows; i++)
                {
                    digest = (digest ^ (uint)mana[i]) * 0x100000001B3ul;
                }

                return digest;
            }

            for (var frame = 0; frame < 12; frame++)
            {
                Step(frame);
            }

            host.Game.SaveState(state);
            Buffer.MemoryCopy(mana, savedMana, Rows * sizeof(int), Rows * sizeof(int));
            var expected = new ulong[18];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = Step(12 + i);
            }

            Assert.True(sawTimed, "scenario must exercise timed modifiers");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                host.Game.LoadState(state);
                Buffer.MemoryCopy(savedMana, mana, Rows * sizeof(int), Rows * sizeof(int));
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], Step(12 + i));
                }
            }
        }
        finally
        {
            NativeMemory.Free(mana);
            NativeMemory.Free(savedMana);
            NativeMemory.Free(body);
        }
    }
}
