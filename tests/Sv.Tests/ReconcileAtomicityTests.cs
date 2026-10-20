using System;
using Frent;
using Sv;
using Sv.Frent;
using Xunit;

[Collection("Frent")]
public unsafe class ReconcileAtomicityTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("power", 1f)
        .Intrinsic("hp")
        .Build();

    [Fact]
    public void Permute_PaddingSlotsAreZeroNotGarbage()
    {
        var column = stackalloc int[128];
        for (var i = 0; i < 128; i++)
        {
            column[i] = -1;
        }

        var oldToNew = new int[128];
        for (var i = 0; i < 128; i++)
        {
            oldToNew[i] = -1;
        }

        oldToNew[0] = 0;
        oldToNew[1] = 64;
        Columns.Permute(column, oldToNew, 65);

        Assert.Equal(-1, column[0]);
        Assert.Equal(-1, column[64]);
        for (var i = 1; i < 64; i++)
        {
            Assert.Equal(0, column[i]);
        }
    }

    [Fact]
    public void Reconcile_UnboundLiveSlot_ThrowsBeforeAnyRewrite()
    {
        using var world = new World();
        using var population = new Population(world, TestSchema(), 128);
        var entity = world.Create(new Health { Value = 5 });
        var slot = population.Bind(entity);
        population.Board.Acquire();

        Assert.Throws<InvalidOperationException>(() => population.Reconcile());

        Assert.Equal(slot, entity.Get<SvSlot>().Slot);
        Assert.Equal(2, population.Board.LiveCount);
        Assert.True(population.TrySlot(entity, out var still));
        Assert.Equal(slot, still);
    }

    [Fact]
    public void Reconcile_AllBound_RenumbersAtomically()
    {
        using var world = new World();
        using var population = new Population(world, TestSchema(), 128);
        var entities = new Entity[5];
        for (var i = 0; i < 5; i++)
        {
            entities[i] = world.Create(new Health { Value = i });
            population.Bind(entities[i]);
        }

        population.Unbind(entities[2]);
        entities[2].Delete();

        population.Reconcile();

        Assert.Equal(4, population.Board.LiveCount);
        var seen = 0;
        for (var i = 0; i < 5; i++)
        {
            if (i == 2)
            {
                continue;
            }

            var slot = entities[i].Get<SvSlot>().Slot;
            Assert.True(slot >= 0 && slot < 4, $"entity {i} remapped to {slot}");
            seen |= 1 << slot;
        }

        Assert.Equal(0b1111, seen);
    }
}
