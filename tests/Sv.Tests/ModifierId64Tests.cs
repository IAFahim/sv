using System;
using System.Collections.Generic;
using Sv;
using Xunit;

public class ModifierId64Tests
{
    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Stat("power", 10f)
            .Build();

    [Fact]
    public void WrapAtCounterBoundary_IdsStayUniqueOrderedAndRemovable()
    {
        using var board = Board.Create(TestSchema(), 4);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.nextInsertionId = (1L << 40) - 3;

        var ids = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var handle = board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 1f));
            ids.Add(handle.Id);
        }

        Assert.Equal(6, new HashSet<long>(ids).Count);
        for (var i = 1; i < ids.Count; i++)
        {
            Assert.True(ids[i] > ids[i - 1], $"id {ids[i]} not after {ids[i - 1]}");
        }

        Assert.True(ids[1] < (1L << 40));
        Assert.True(ids[2] > (1L << 40));
        board.Recalculate();
        Assert.Equal(16f, board.Stat(slot, power));
        board.RemoveModifier(slot, power, new ModifierHandle(ids[0]));
        board.Recalculate();
        Assert.Equal(15f, board.Stat(slot, power));
    }

    [Fact]
    public void KeyframeMerge_TakesLexicographicMaxOfGenerationAndCounter()
    {
        using var receiver = Board.Create(TestSchema(), 8);
        using var source = Board.Create(TestSchema(), 8);
        var power = receiver.Schema.Stat("power");
        var slot = source.Acquire();
        source.Recalculate();
        using var baseline = new SvState();
        source.SaveState(baseline);
        receiver.LoadState(baseline);

        source.AddModifier(slot, power, new Modifier(ModifierKind.Added, 1f));
        source.insertionGeneration = 2;
        source.nextInsertionId = 9;
        source.Recalculate();

        using var keyframe = new SvState();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);

        var handle = receiver.AddModifier(receiver.Acquire(), power, new Modifier(ModifierKind.Added, 2f));
        Assert.True(handle.Id > ((2L << 40) | 9L), $"id {handle.Id} not past the shipped stream");
        Assert.Equal(2, receiver.insertionGeneration);
    }

    [Fact]
    public void StateMagic_BumpedAndOldMagicRejected()
    {
        using var board = Board.Create(TestSchema(), 8);
        var slot = board.Acquire();
        board.AddModifier(slot, board.Schema.Stat("power"), new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        using var state = new SvState();
        board.SaveState(state);

        var bytes = state.Bytes.ToArray();
        Assert.Equal((byte)'T', bytes[0]);
        bytes[0] = (byte)'S';
        using var stale = new SvState();
        unsafe
        {
            fixed (byte* pinned = bytes)
            {
                stale.BeginWrite();
                stale.Write(pinned, bytes.Length);
                Assert.Throws<System.IO.InvalidDataException>(() => board.LoadState(stale));
            }
        }
    }

    [Fact]
    public void DerivedEntries_FoldBeforeHostModifiers_AtAnyIdScale()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Derived("derived", 5f, ("power", 0.5f)).Build();
        using var board = Board.Create(schema, 8);
        var derived = schema.Stat("derived");
        var slot = board.Acquire();
        board.nextInsertionId = (1L << 40) - 2;
        board.AddModifier(slot, derived, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();

        var expected = 5f + 0.5f * 10f + 1f;
        Assert.Equal(expected, board.Stat(slot, derived));
    }
}
