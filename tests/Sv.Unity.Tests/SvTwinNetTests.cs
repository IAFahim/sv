using System;
using Sv;
using Sv.Unity.Tests;
using Xunit;

public sealed unsafe class NetTwinHost : ISvTwinHost, IDisposable
{
    public const int HpColumn = 1;
    public const int ManaColumn = 5;
    public const int ManaCeiling = 50;

    readonly Board board;
    readonly int[] hp = new int[512];
    readonly int[] hpDelta = new int[512];
    readonly int[] mana = new int[512];
    readonly int[] manaDelta = new int[512];
    readonly long[] inbox = new long[512];
    readonly float[] maxHp = new float[512];
    readonly StatOp[] ops = new StatOp[256];
    readonly StatOp[] scratch = new StatOp[256];
    readonly StatWrite[] writes = new StatWrite[1024];
    readonly int rows;
    int opCount;
    long tick;

    public NetTwinHost(Schema schema, int rows)
    {
        this.rows = rows;
        board = Board.Create(schema, 512);
        board.BindSink(schema.Stat("max_hp"));
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(i, board.Acquire());
            hp[i] = 50 + i % 40;
            mana[i] = 10;
            maxHp[i] = 100f;
        }
    }

    public void AddDelta(int row, int intrinsic, int amount)
    {
        if (intrinsic == 0)
        {
            hpDelta[row] += amount;
        }
        else
        {
            manaDelta[row] += amount;
        }
    }

    public void Send(int row, int amount) => inbox[row] += amount;

    public void Enqueue(StatOp op) => ops[opCount++] = op;

    public void Frame()
    {
        board.ExpireModifiers(tick);
        if (opCount > 0)
        {
            StatOps.Canonicalize(ops.AsSpan(0, opCount), scratch.AsSpan(0, opCount));
            board.Apply(ops.AsSpan(0, opCount));
            opCount = 0;
        }

        board.Recalculate();
        int taken;
        while ((taken = board.TakeBroadcast(writes)) > 0)
        {
            for (var i = 0; i < taken; i++)
            {
                var bits = writes[i].Bits;
                maxHp[writes[i].Slot] = *(float*)&bits;
            }
        }

        fixed (int* h = hp, hd = hpDelta, m = mana, md = manaDelta)
        fixed (long* box = inbox)
        fixed (float* max = maxHp)
        {
            for (var start = 0; start < rows; start += 64)
            {
                var count = Math.Min(64, rows - start);
                var hpWord = new FoldWord { Value = h + start, Delta = hd + start, Ceiling = max + start, FloorConstant = 0 };
                Fold.Word(in hpWord, box + start, count);
                var manaWord = new FoldWord { Value = m + start, Delta = md + start, FloorConstant = 0, CeilingConstant = ManaCeiling };
                Fold.Word(in manaWord, count);
            }
        }

        tick++;
    }

    public ulong Digest()
    {
        var cells = 0ul;
        unchecked
        {
            for (var i = 0; i < rows; i++)
            {
                cells += CellDigest.Key(HpColumn, i, (uint)hp[i]);
                cells += CellDigest.Key(ManaColumn, i, (uint)mana[i]);
            }

            return board.Digest + cells;
        }
    }

    public void Dispose() => board.Dispose();
}

public class SvTwinNetTests
{
    public const ulong Pinned = 0xDA47AD9B5CD74335ul;

    [Fact]
    public void FoldScenarioDigestIsPinnedOnDotNet()
    {
        var schema = SchemaBuilder.Create().Stat("max_hp", 100f).Intrinsic("hp").Intrinsic("mana").Build();
        using var host = new NetTwinHost(schema, SvTwinScenario.Rows);
        var digest = SvTwinScenario.Run(host, schema.Stat("max_hp"));
        Console.WriteLine($"svtwin net digest {digest:X16}");
        Assert.Equal(Pinned, digest);
    }
}
