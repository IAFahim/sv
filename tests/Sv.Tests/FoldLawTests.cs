using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class FoldLawTests
{
    static readonly int[] Lattice = { int.MinValue, -1, 0, 1, int.MaxValue };
    static readonly float[] StatLattice = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 3e9f, -3e9f, 2147483648f, -2147483648f, 0f, -0f, 99.9f, -5.5f, 100f, 1f };
    static readonly long[] InboxLattice = { 0, 1, -1, int.MaxValue, int.MinValue, 1L << 40, -(1L << 40), long.MaxValue / 4, long.MinValue / 4 };
    static readonly FoldWidth[] Widths = { FoldWidth.Scalar, FoldWidth.Width128, FoldWidth.Width256, FoldWidth.Auto };
    static readonly ulong[] MaskShapes = { ulong.MaxValue, 0ul, 0x5555_5555_5555_5555ul, 0xAAAA_AAAA_AAAA_AAAAul, 1ul, 1ul << 63, 0x00FF_00FF_00FF_00FFul, 0xF0F0_0000_FFFF_000Ful, 0x0037_FFFF_FFFF_FF5Aul, 0x8000_0000_0000_0001ul };

    enum BoundMode
    {
        None,
        Constant,
        Rows,
        Stats,
        StatFloor,
        StatCeiling,
    }

    sealed class Board64 : IDisposable
    {
        public readonly int Rows;
        public readonly int* Value;
        public readonly int* Delta;
        public readonly long* Inbox;
        public readonly int* FloorRows;
        public readonly int* CeilingRows;
        public readonly float* FloorStats;
        public readonly float* CeilingStats;

        public Board64(int rows)
        {
            Rows = rows;
            Value = (int*)NativeMemory.AllocZeroed((nuint)rows, sizeof(int));
            Delta = (int*)NativeMemory.AllocZeroed((nuint)rows, sizeof(int));
            Inbox = (long*)NativeMemory.AllocZeroed((nuint)rows, sizeof(long));
            FloorRows = (int*)NativeMemory.AllocZeroed((nuint)rows, sizeof(int));
            CeilingRows = (int*)NativeMemory.AllocZeroed((nuint)rows, sizeof(int));
            FloorStats = (float*)NativeMemory.AllocZeroed((nuint)rows, sizeof(float));
            CeilingStats = (float*)NativeMemory.AllocZeroed((nuint)rows, sizeof(float));
        }

        public void Fill(Random random)
        {
            for (var i = 0; i < Rows; i++)
            {
                Value[i] = random.Next(3) == 0 ? Lattice[random.Next(Lattice.Length)] : random.Next(-200, 200);
                Delta[i] = random.Next(3) == 0 ? Lattice[random.Next(Lattice.Length)] : random.Next(-300, 300);
                Inbox[i] = random.Next(3) == 0 ? InboxLattice[random.Next(InboxLattice.Length)] : random.Next(-300, 300);
                FloorRows[i] = random.Next(4) == 0 ? Lattice[random.Next(Lattice.Length)] : random.Next(-150, 50);
                CeilingRows[i] = random.Next(4) == 0 ? Lattice[random.Next(Lattice.Length)] : random.Next(-50, 150);
                FloorStats[i] = random.Next(3) == 0 ? StatLattice[random.Next(StatLattice.Length)] : random.Next(-150, 50) + 0.25f;
                CeilingStats[i] = random.Next(3) == 0 ? StatLattice[random.Next(StatLattice.Length)] : random.Next(-50, 150) + 0.75f;
            }
        }

        public void CopyFrom(Board64 other)
        {
            Buffer.MemoryCopy(other.Value, Value, Rows * 4L, Rows * 4L);
            Buffer.MemoryCopy(other.Delta, Delta, Rows * 4L, Rows * 4L);
            Buffer.MemoryCopy(other.Inbox, Inbox, Rows * 8L, Rows * 8L);
            Buffer.MemoryCopy(other.FloorRows, FloorRows, Rows * 4L, Rows * 4L);
            Buffer.MemoryCopy(other.CeilingRows, CeilingRows, Rows * 4L, Rows * 4L);
            Buffer.MemoryCopy(other.FloorStats, FloorStats, Rows * 4L, Rows * 4L);
            Buffer.MemoryCopy(other.CeilingStats, CeilingStats, Rows * 4L, Rows * 4L);
        }

        public Bounds BoundsFor(BoundMode mode) => mode switch
        {
            BoundMode.None => Bounds.None,
            BoundMode.Constant => Bounds.Constant(-40, 90),
            BoundMode.Rows => Bounds.Rows(FloorRows, CeilingRows),
            _ => default,
        };

        public void Dispose()
        {
            NativeMemory.Free(Value);
            NativeMemory.Free(Delta);
            NativeMemory.Free(Inbox);
            NativeMemory.Free(FloorRows);
            NativeMemory.Free(CeilingRows);
            NativeMemory.Free(FloorStats);
            NativeMemory.Free(CeilingStats);
        }
    }

    static void RunFold(FoldWidth width, Board64 b, BoundMode mode, Window window, bool withInbox, ulong* changed, int changedLength)
    {
        Kernel.FoldWith(width, new Column<int>(b.Value, b.Rows), window, new Column<int>(b.Delta, b.Rows), BoundsOf(b, mode), new Column<ulong>(changed, changedLength), withInbox ? new Column<long>(b.Inbox, b.Rows) : default);
    }

    static Bounds BoundsOf(Board64 b, BoundMode mode)
    {
        if (mode <= BoundMode.Rows)
        {
            return b.BoundsFor(mode);
        }

        return StatBounds(mode == BoundMode.StatCeiling ? null : b.FloorStats, mode == BoundMode.StatFloor ? null : b.CeilingStats);
    }

    static Bounds StatBounds(float* floor, float* ceiling)
    {
        var ctor = typeof(Clamp).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = parameters[i].ParameterType.IsPointer ? System.Reflection.Pointer.Box(null, parameters[i].ParameterType) : parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Activator.CreateInstance(parameters[i].ParameterType);
        }

        args[0] = ClampKind.StatBounds;
        args[5] = System.Reflection.Pointer.Box(floor, typeof(float*));
        args[6] = System.Reflection.Pointer.Box(ceiling, typeof(float*));
        var clamp = (Clamp)ctor.Invoke(args);
        var boundsCtor = typeof(Bounds).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        return (Bounds)boundsCtor.Invoke(new object[] { clamp });
    }

    static ulong ReferenceRow(Board64 b, BoundMode mode, int slot, bool withInbox)
    {
        if (mode == BoundMode.Rows)
        {
            var before = b.Value[slot];
            var moved = withInbox ? Inbox.Take(b.Inbox, slot, b.Delta[slot]) : b.Delta[slot];
            var after = IntrinsicLaw.Step(before, moved, b.FloorRows[slot], b.CeilingRows[slot]);
            b.Value[slot] = after;
            b.Delta[slot] = 0;
            return after != before ? 1ul : 0ul;
        }

        var word = new FoldWord
        {
            Value = b.Value + slot,
            Delta = b.Delta + slot,
            Floor = mode == BoundMode.Stats || mode == BoundMode.StatFloor ? b.FloorStats + slot : null,
            Ceiling = mode == BoundMode.Stats || mode == BoundMode.StatCeiling ? b.CeilingStats + slot : null,
            FloorConstant = mode == BoundMode.Constant ? -40 : int.MinValue,
            CeilingConstant = mode == BoundMode.Constant ? 90 : int.MaxValue,
        };
        return withInbox ? Fold.Word(word, b.Inbox + slot, 1) : Fold.Word(word, 1);
    }

    static void AssertSame(Board64 expected, Board64 actual, string label)
    {
        for (var i = 0; i < expected.Rows; i++)
        {
            Assert.True(expected.Value[i] == actual.Value[i], $"{label}: value row {i} {expected.Value[i]} != {actual.Value[i]}");
            Assert.True(expected.Delta[i] == actual.Delta[i], $"{label}: delta row {i} {expected.Delta[i]} != {actual.Delta[i]}");
            Assert.True(expected.Inbox[i] == actual.Inbox[i], $"{label}: inbox row {i} {expected.Inbox[i]} != {actual.Inbox[i]}");
        }
    }

    [Fact]
    public void Step_Lattice_EqualsSaturateThenCeilingWinsClamp()
    {
        var value = stackalloc int[1];
        var delta = stackalloc int[1];
        var floorRow = stackalloc int[1];
        var ceilingRow = stackalloc int[1];
        foreach (var before in Lattice)
        {
            foreach (var d in Lattice)
            {
                foreach (var floor in Lattice)
                {
                    foreach (var ceiling in Lattice)
                    {
                        var low = Math.Min(floor, ceiling);
                        var expected = (int)Math.Min(Math.Max((long)before + d, low), ceiling);
                        Assert.Equal(expected, IntrinsicLaw.Step(before, d, floor, ceiling));
                        value[0] = before;
                        delta[0] = d;
                        floorRow[0] = floor;
                        ceilingRow[0] = ceiling;
                        Kernel.Run(new Column<int>(value, 1), Window.Range(0, 1), new Delta(delta), Bounds.Rows(floorRow, ceilingRow));
                        Assert.Equal(expected, value[0]);
                        var word = new FoldWord { Value = value, Delta = delta, FloorConstant = floor, CeilingConstant = ceiling };
                        value[0] = before;
                        delta[0] = d;
                        var changed = Fold.Word(word, 1);
                        Assert.Equal(expected, value[0]);
                        Assert.Equal(0, delta[0]);
                        Assert.Equal(expected != before ? 1ul : 0ul, changed);
                        Assert.Equal(IntrinsicLaw.Saturate((long)expected - before), IntrinsicLaw.Applied(before, expected));
                    }
                }
            }
        }
    }

    [Fact]
    public void Word_StatBounds_NaNAndInfinite_UseClause16Conversion()
    {
        var value = stackalloc int[1];
        var delta = stackalloc int[1];
        var floor = stackalloc float[1];
        var ceiling = stackalloc float[1];
        foreach (var f in StatLattice)
        {
            foreach (var c in StatLattice)
            {
                foreach (var before in Lattice)
                {
                    value[0] = before;
                    delta[0] = 7;
                    floor[0] = f;
                    ceiling[0] = c;
                    Fold.Word(new FoldWord { Value = value, Delta = delta, Floor = floor, Ceiling = ceiling }, 1);
                    Assert.Equal(IntrinsicLaw.Step(before, 7, Clamp.BoundOf(f), Clamp.BoundOf(c)), value[0]);
                }
            }
        }
    }

    [Fact]
    public void EveryWidth_EveryBound_EveryMaskShape_EqualsScalarReference()
    {
        const int rows = 64 * 6 + 37;
        var random = new Random(0x5F01D);
        using var seed = new Board64(rows);
        using var expected = new Board64(rows);
        using var actual = new Board64(rows);
        var words = stackalloc ulong[6];
        var changed = stackalloc ulong[8];
        foreach (var mode in Enum.GetValues<BoundMode>())
        {
            foreach (var withInbox in new[] { false, true })
            {
                for (var shape = 0; shape < MaskShapes.Length + 3; shape++)
                {
                    seed.Fill(random);
                    var masked = shape < MaskShapes.Length;
                    var start = masked ? 3 + shape % 5 : shape - MaskShapes.Length;
                    var count = masked ? 0 : rows - start - shape;
                    if (masked)
                    {
                        for (var w = 0; w < 5; w++)
                        {
                            words[w] = w == 4 ? (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 1) : MaskShapes[(shape + w) % MaskShapes.Length];
                        }
                    }

                    var window = masked ? Window.Masked(start, words, 5) : Window.Range(start, count);
                    var wordCount = masked ? 5 : (count + 63) >> 6;
                    expected.CopyFrom(seed);
                    var expectedChanged = new ulong[wordCount];
                    for (var w = 0; w < wordCount; w++)
                    {
                        var live = masked ? words[w] : (count - w * 64 >= 64 ? ulong.MaxValue : (1ul << (count - w * 64)) - 1);
                        for (var bit = 0; bit < 64; bit++)
                        {
                            if ((live & (1ul << bit)) != 0)
                            {
                                expectedChanged[w] |= ReferenceRow(expected, mode, start + w * 64 + bit, withInbox) << bit;
                            }
                        }
                    }

                    foreach (var width in Widths)
                    {
                        actual.CopyFrom(seed);
                        for (var w = 0; w < 8; w++)
                        {
                            changed[w] = 0xDEADul;
                        }

                        RunFold(width, actual, mode, window, withInbox, changed, 8);
                        var label = $"{width} {mode} inbox={withInbox} shape={shape}";
                        AssertSame(expected, actual, label);
                        for (var w = 0; w < wordCount; w++)
                        {
                            Assert.True(expectedChanged[w] == changed[w], $"{label}: changed word {w} {expectedChanged[w]:X16} != {changed[w]:X16}");
                        }

                        Assert.Equal(0xDEADul, changed[wordCount]);
                    }
                }
            }
        }
    }

    [Fact]
    public void RangeWord_MultiRowReference_EqualsKernelFold()
    {
        const int rows = 64 * 3;
        var random = new Random(77);
        using var a = new Board64(rows);
        using var b = new Board64(rows);
        a.Fill(random);
        b.CopyFrom(a);
        var changed = stackalloc ulong[3];
        Kernel.Fold(new Column<int>(b.Value, rows), Window.Range(0, rows), new Column<int>(b.Delta, rows), StatBounds(b.FloorStats, b.CeilingStats), new Column<ulong>(changed, 3));
        for (var w = 0; w < 3; w++)
        {
            var bits = Fold.Word(new FoldWord { Value = a.Value + w * 64, Delta = a.Delta + w * 64, Floor = a.FloorStats + w * 64, Ceiling = a.CeilingStats + w * 64 }, 64);
            Assert.Equal(bits, changed[w]);
        }

        AssertSame(a, b, "multi-row");
    }

    [Fact]
    public void PartitionInvariance_ShuffledDisjointSubWindows_EqualOneWindow()
    {
        const int rows = 64 * 8;
        var random = new Random(9);
        using var whole = new Board64(rows);
        using var parts = new Board64(rows);
        var all = stackalloc ulong[8];
        var split = stackalloc ulong[16];
        for (var round = 0; round < 20; round++)
        {
            whole.Fill(random);
            parts.CopyFrom(whole);
            foreach (var mode in new[] { BoundMode.Stats, BoundMode.Rows })
            {
                for (var w = 0; w < 8; w++)
                {
                    all[w] = round % 2 == 0 ? ulong.MaxValue : (ulong)random.NextInt64() | 1ul;
                    var cut = (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 3);
                    split[w * 2] = all[w] & cut;
                    split[w * 2 + 1] = all[w] & ~cut;
                }

                RunFold(FoldWidth.Auto, whole, mode, Window.Masked(0, all, 8), true, null, 0);
                var order = Enumerable.Range(0, 16).OrderBy(_ => random.Next()).ToArray();
                foreach (var piece in order)
                {
                    if (split[piece] == 0)
                    {
                        continue;
                    }

                    var w = piece / 2;
                    RunFold(FoldWidth.Width256, parts, mode, Window.Masked(w * 64, split + piece, 1), true, null, 0);
                }

                AssertSame(whole, parts, $"round {round} {mode}");
            }
        }

        whole.Fill(random);
        parts.CopyFrom(whole);
        RunFold(FoldWidth.Auto, whole, BoundMode.Stats, Window.Range(0, rows), false, null, 0);
        var cuts = new[] { 0, 5, 64, 100, 129, 300, 301, 511, rows };
        foreach (var piece in Enumerable.Range(0, cuts.Length - 1).OrderBy(_ => random.Next()))
        {
            RunFold(FoldWidth.Width128, parts, BoundMode.Stats, Window.Range(cuts[piece], cuts[piece + 1] - cuts[piece]), false, null, 0);
        }

        AssertSame(whole, parts, "ranges");
    }

    [Fact]
    public void Shuffle_HundredOrdersOfContributions_GiveIdenticalBits()
    {
        const int rows = 64;
        var random = new Random(1234);
        var contributions = new List<(int Row, int Amount, bool Remote)>();
        for (var i = 0; i < 4000; i++)
        {
            var amount = random.Next(4) == 0 ? (random.Next(2) == 0 ? (1 << 20) : -(1 << 20)) : random.Next(-1000, 1000);
            contributions.Add((random.Next(rows), amount, random.Next(3) == 0));
        }

        using var b = new Board64(rows);
        int[]? firstValue = null;
        ulong firstChanged = 0;
        var changed = stackalloc ulong[1];
        for (var order = 0; order < 100; order++)
        {
            for (var i = 0; i < rows; i++)
            {
                b.Value[i] = i * 3 - 90;
                b.Delta[i] = 0;
                b.Inbox[i] = 0;
                b.FloorStats[i] = -50f;
                b.CeilingStats[i] = i % 7 == 0 ? float.NaN : 75.5f;
            }

            foreach (var c in contributions.OrderBy(_ => random.Next()))
            {
                if (c.Remote)
                {
                    Inbox.Send(b.Inbox, c.Row, c.Amount);
                }
                else
                {
                    b.Delta[c.Row] = unchecked(b.Delta[c.Row] + c.Amount);
                }
            }

            RunFold(Widths[order % Widths.Length], b, BoundMode.Stats, Window.Range(0, rows), true, changed, 1);
            var value = new int[rows];
            for (var i = 0; i < rows; i++)
            {
                value[i] = b.Value[i];
                Assert.Equal(0, b.Delta[i]);
                Assert.Equal(0L, b.Inbox[i]);
            }

            if (firstValue == null)
            {
                firstValue = value;
                firstChanged = changed[0];
            }
            else
            {
                Assert.Equal(firstValue, value);
                Assert.Equal(firstChanged, changed[0]);
            }
        }
    }

    [Fact]
    public void Inbox_ParallelSends_EqualSequentialSum()
    {
        using var inbox = new InboxBuffer(128);
        var p = inbox.Pointer;
        Parallel.For(0, 64_000, i => Inbox.Send(p, i % 128, i % 2 == 0 ? int.MaxValue : -(i % 1000)));
        for (var slot = 0; slot < 128; slot++)
        {
            long expected = 0;
            for (var i = slot; i < 64_000; i += 128)
            {
                expected += i % 2 == 0 ? int.MaxValue : -(i % 1000);
            }

            Assert.Equal(expected, p[slot]);
        }
    }

    [Fact]
    public void Inbox_Saturation_TakeAndFold()
    {
        using var inbox = new InboxBuffer(130);
        Assert.Equal(0L, (long)inbox.Pointer % 64);
        var p = inbox.Pointer;
        p[0] = (long)int.MaxValue * 4;
        Assert.Equal(int.MaxValue, Inbox.Take(p, 0, 1));
        Assert.Equal(0L, p[0]);
        p[1] = int.MinValue;
        Assert.Equal(int.MinValue, Inbox.Take(p, 1, -1));
        p[2] = -(long)int.MaxValue * 3;
        Assert.Equal(int.MinValue, Inbox.Take(p, 2, int.MaxValue));
        p[3] = 10;
        Assert.Equal(7, Inbox.Take(p, 3, -3));
        for (var i = 0; i < 4096; i++)
        {
            Inbox.Send(p, 4, int.MaxValue);
        }

        var value = stackalloc int[130];
        var delta = stackalloc int[130];
        value[4] = int.MaxValue - 5;
        delta[4] = int.MaxValue;
        Kernel.Fold(new Column<int>(value, 130), Window.Range(0, 130), new Column<int>(delta, 130), Bounds.None, default, inbox.Column);
        Assert.Equal(int.MaxValue, value[4]);
        Assert.Equal(0L, p[4]);
        inbox.Pointer[9] = 3;
        inbox.Clear();
        Assert.Equal(0L, inbox.Pointer[9]);
        inbox.Dispose();
        Assert.Throws<ObjectDisposedException>(() => inbox.Length + (long)inbox.Pointer);
    }

    [Fact]
    public void Condition_Hysteresis_EntersBelowThirtyPercent_RearmsAboveForty()
    {
        var hp = new[] { 100, 31, 30, 29, 35, 39, 40, 41, 29, 10, 45, 0 };
        var state = 0ul;
        var edges = new List<int>();
        var trace = new List<bool>();
        for (var tick = 0; tick < hp.Length; tick++)
        {
            var enter = Condition.Below(hp[tick], 100, 30, 100) ? 1ul : 0ul;
            var stay = Condition.Below(hp[tick], 100, 40, 100) ? 1ul : 0ul;
            var next = Condition.Next(state, enter, stay);
            if (Condition.Edges(state, next) != 0)
            {
                edges.Add(tick);
            }

            state = next;
            trace.Add(state != 0);
        }

        Assert.Equal(new[] { false, false, false, true, true, true, false, false, true, true, false, true }, trace);
        Assert.Equal(new[] { 3, 6, 8, 10, 11 }, edges);
        Assert.True(Condition.Below(int.MaxValue - 1, int.MaxValue, 1, 1));
        Assert.False(Condition.Below(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue));
        Assert.True(Condition.Below(int.MinValue, int.MaxValue, 0, 1));
        Assert.Equal(0b1010ul, Condition.Edges(0b0110ul, Condition.Next(0b0110ul, 0b1000ul, 0b0100ul)) & 0b1010ul);
    }

    [Fact]
    public void CellDigest_ColumnSlotKeyed_OrderFree()
    {
        Assert.NotEqual(CellDigest.Key(0, 5, 100), CellDigest.Key(1, 5, 100));
        Assert.NotEqual(CellDigest.Key(0, 5, 100), CellDigest.Key(0, 6, 100));
        Assert.NotEqual(0ul, unchecked(CellDigest.Key(0, 5, 100) + CellDigest.Key(1, 5, 100)));
        var values = stackalloc int[200];
        for (var i = 0; i < 200; i++)
        {
            values[i] = i * 31 - 77;
        }

        var forward = CellDigest.Sum(3, values, 0, 200);
        var halves = unchecked(CellDigest.Sum(3, values, 0, 77) + CellDigest.Sum(3, values, 77, 123));
        Assert.Equal(forward, halves);
        var words = stackalloc ulong[4] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, (1ul << 8) - 1 };
        Assert.Equal(forward, CellDigest.Sum(3, values, 0, words, 4));
        var moved = CellDigest.Move(forward, 3, 10, (uint)values[10], 999u);
        values[10] = 999;
        Assert.Equal(CellDigest.Sum(3, values, 0, 200), moved);
        Assert.NotEqual(CellDigest.Sum(4, values, 0, 200), CellDigest.Sum(3, values, 0, 200));
    }

    [Fact]
    public void Fold_Warm_AllocatesNothing()
    {
        const int rows = 4096;
        using var b = new Board64(rows);
        b.Fill(new Random(3));
        var changed = stackalloc ulong[rows / 64];
        var words = stackalloc ulong[rows / 64];
        for (var i = 0; i < rows / 64; i++)
        {
            words[i] = 0x0F0F_FFFF_0000_1234ul;
        }

        var value = new Column<int>(b.Value, rows);
        var delta = new Column<int>(b.Delta, rows);
        var inbox = new Column<long>(b.Inbox, rows);
        var ch = new Column<ulong>(changed, rows / 64);
        var bounds = Bounds.Rows(b.FloorRows, b.CeilingRows);
        Kernel.Fold(value, Window.Range(0, rows), delta, bounds, ch, inbox);
        Kernel.Fold(value, Window.Masked(0, words, rows / 64), delta, bounds, ch);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Kernel.Fold(value, Window.Range(0, rows), delta, bounds, ch, inbox);
            Kernel.Fold(value, Window.Masked(0, words, rows / 64), delta, bounds, ch);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Fold_Guards_RejectShortColumns()
    {
        var value = stackalloc int[64];
        var delta = stackalloc int[64];
        var changed = stackalloc ulong[1];
        var v = new Column<int>(value, 64);
        Assert.Throws<ArgumentException>(() => Kernel.Fold(v, Window.Range(0, 64), new Column<int>(delta, 63), Bounds.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.Fold(v, Window.Range(1, 64), new Column<int>(delta, 64), Bounds.None));
        Assert.Throws<ArgumentException>(() => Kernel.Fold(v, Window.Range(0, 64), new Column<int>(delta, 64), Bounds.None, new Column<ulong>(changed, 1), new Column<long>((long*)changed, 1)));
        Kernel.Fold(v, Window.Range(0, 0), new Column<int>(delta, 64), Bounds.None);
    }
}
