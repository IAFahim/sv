using Sv;
using Xunit;

[Collection(HeavyAllocationCollection.Name)]
public class SetChurnTests
{
    const int Slots = 10_000;

    const int PerTick = 250;

    static (long[] Memory, int MaxSets, long WarmBytes) Churn(int rounds, int ticksPerRound, int undoFrames)
    {
        var schema = SchemaBuilder.Create().Stat("max_hp", 100f).Build();
        var maxHp = schema.Stat("max_hp");
        using var board = Board.Create(schema, Slots);
        board.BindSink(maxHp);
        for (var i = 0; i < Slots; i++)
        {
            board.Acquire();
        }

        if (undoFrames > 0)
        {
            board.BeginUndoWindow(undoFrames);
        }

        var random = new Random(0xC4021);
        var ops = new StatOp[PerTick * 2];
        var scratch = new StatOp[PerTick * 2];
        var writes = new StatWrite[4096];
        var liveSlot = new int[PerTick];
        var liveSource = new int[PerTick];
        var live = 0;
        var source = 1;
        var memory = new long[rounds];
        var maxSets = 0;
        long warm = 0;
        for (var round = 0; round < rounds; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var t = 0; t < ticksPerRound; t++)
            {
                if (undoFrames > 0)
                {
                    board.BeginUndoFrame();
                }

                var n = 0;
                for (var i = 0; i < live; i++)
                {
                    ops[n++] = StatOp.RemoveSource(liveSlot[i], liveSource[i]);
                }

                for (var i = 0; i < PerTick; i++)
                {
                    var slot = random.Next(Slots);
                    ops[n++] = StatOp.Add(slot, maxHp, ModifierKind.Added, random.Next(9) - 4, source);
                    liveSlot[i] = slot;
                    liveSource[i] = source++;
                }

                live = PerTick;
                StatOps.Canonicalize(ops.AsSpan(0, n), scratch.AsSpan(0, n));
                board.Apply(ops.AsSpan(0, n));
                board.Recalculate();
                while (board.TakeBroadcast(writes) > 0)
                {
                }

                maxSets = Math.Max(maxSets, board.SetCount);
            }

            if (round >= 2)
            {
                warm += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            memory[round] = board.MemoryBytes;
        }

        return (memory, maxSets, warm);
    }

    [Fact]
    public void SteadyModifierChurn_KeepsMemoryFlat_AndAllocatesNothingWarm()
    {
        var (memory, maxSets, warm) = Churn(12, 96, 0);
        for (var round = 3; round < memory.Length; round++)
        {
            Assert.True(memory[round] == memory[2], $"round {round}: {memory[round]} B after {memory[2]} B at round 2");
        }

        Assert.True(maxSets < 600, $"live sets peaked at {maxSets}");
        Assert.Equal(0, warm);
    }

    [Fact]
    public void SteadyModifierChurn_UnderAnUndoWindow_KeepsMemoryFlat()
    {
        var (memory, _, _) = Churn(10, 96, 4);
        for (var round = 4; round < memory.Length; round++)
        {
            Assert.True(memory[round] == memory[3], $"round {round}: {memory[round]} B after {memory[3]} B at round 3");
        }
    }
}
