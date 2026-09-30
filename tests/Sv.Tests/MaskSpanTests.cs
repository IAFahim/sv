using Sv;
using Sv.Frent;
using Xunit;

public unsafe class MaskSpanTests
{
    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Intrinsic("mana", 0, 100)
            .Build();

    [Theory]
    [InlineData(4, 64)]
    [InlineData(1, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(300, 320)]
    public void Create_Rounds_Capacity_Up_To_A_Word_Multiple(int requested, int rounded)
    {
        using var board = Board.Create(TestSchema(), requested);
        Assert.Equal(rounded, board.Capacity);
    }

    [Fact]
    public void Small_Board_Can_Use_The_Enabled_Verb()
    {
        using var board = Board.Create(TestSchema(), 4);
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        for (var i = 0; i < 4; i++)
        {
            value[i] = 10;
        }

        board.RunEnabledChunk(value, 5, Clamp.Constant(0, 100), 0b1101ul, 0, applied);

        Assert.Equal(15, value[0]);
        Assert.Equal(10, value[1]);
        Assert.Equal(15, value[2]);
        Assert.Equal(15, value[3]);
        Assert.Equal(5, applied[0]);
        Assert.Equal(0, applied[1]);
    }

    [Fact]
    public void Tail_Window_At_Offset_256_Works_On_A_300_Slot_Board()
    {
        using var board = Board.Create(TestSchema(), 300);
        var value = stackalloc int[320];
        var applied = stackalloc int[320];
        var words = stackalloc ulong[1];
        words[0] = (1ul << 44) - 1;
        for (var i = 256; i < 300; i++)
        {
            value[i] = 10;
        }

        board.RunEnabledChunk(value, 5, Clamp.Constant(0, 100), words, 1, 256, applied);

        for (var i = 256; i < 300; i++)
        {
            Assert.Equal(15, value[i]);
            Assert.Equal(5, applied[i]);
        }

        Assert.Equal(0, value[255]);
        Assert.Equal(0, applied[255]);
    }

    [Fact]
    public void Word_Span_Form_Matches_Composed_Single_Word_Calls()
    {
        using var board = Board.Create(TestSchema(), 128);
        var a = stackalloc int[128];
        var b = stackalloc int[128];
        var appliedA = stackalloc int[128];
        var appliedB = stackalloc int[128];
        var words = stackalloc ulong[2];
        words[0] = 0x9249_2492_4924_9249ul;
        words[1] = 0x4924_9249_2492_4924ul;
        for (var i = 0; i < 128; i++)
        {
            a[i] = 40 + (i & 7);
            b[i] = a[i];
        }

        board.RunEnabledChunk(a, 25, Clamp.Constant(0, 100), words, 2, 0, appliedA);
        board.RunEnabledChunk(b, 25, Clamp.Constant(0, 100), words[0], 0, appliedB);
        board.RunEnabledChunk(b, 25, Clamp.Constant(0, 100), words[1], 64, appliedB);

        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(b[i], a[i]);
            Assert.Equal(appliedB[i], appliedA[i]);
        }
    }

    [Fact]
    public void Span_Twin_Matches_Pointer_Form()
    {
        using var board = Board.Create(TestSchema(), 128);
        var ptrForm = stackalloc int[128];
        var spanForm = stackalloc int[128];
        var appliedPtr = stackalloc int[128];
        var words = stackalloc ulong[2];
        words[0] = ulong.MaxValue;
        words[1] = (1ul << 13) - 1;
        for (var i = 0; i < 128; i++)
        {
            ptrForm[i] = 50;
        }
        for (var i = 13; i < 64; i++)
        {
            words[0] &= ~(1ul << i);
        }
        for (var i = 0; i < 128; i++)
        {
            spanForm[i] = 50;
        }

        board.RunEnabledChunk(ptrForm, -10, Clamp.Constant(0, 100), words, 2, 0, appliedPtr);
        var spanApplied = new int[128];
        board.RunEnabledChunk(new Span<int>(spanForm, 128), -10, Clamp.Constant(0, 100), words, 2, 0, spanApplied);

        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(spanForm[i], ptrForm[i]);
            Assert.Equal(spanApplied[i], appliedPtr[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    public void Word_Span_Form_Conforms_At_Fixture_Boundary_Counts(int count)
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];
        var words = stackalloc ulong[2];
        words[0] = count >= 64 ? ulong.MaxValue : (count == 0 ? 0ul : (ulong)((1L << count) - 1));
        words[1] = count > 64 ? (count == 128 ? ulong.MaxValue : (ulong)((1L << (count - 64)) - 1)) : 0ul;
        for (var i = 0; i < 128; i++)
        {
            value[i] = 40;
        }

        board.RunEnabledChunk(value, 25, Clamp.Constant(0, 100), words, 2, 0, applied);

        for (var i = 0; i < 128; i++)
        {
            var expected = i < count ? 65 : 40;
            Assert.Equal(expected, value[i]);
            Assert.Equal(i < count ? 25 : 0, applied[i]);
        }
    }

#if SV_CHECKED
    [Fact]
    public void Seam_Tail_Bits_Past_Chunk_Rows_Throw_Under_Checked()
    {
        using var board = Board.Create(TestSchema(), 64);
        var rows = new int[44];
        var slots = new SvSlot[44];
        for (var i = 0; i < 44; i++)
        {
            slots[i] = new SvSlot(i);
        }

        var words = stackalloc ulong[1];
        words[0] = 1ul << 63;
        var applied = new int[64];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChunkSeam.RunEnabledChunk(board, rows, slots, words, 1, 0, 5, Clamp.Constant(0, 100), applied));
    }

    [Fact]
    public void Window_Past_Capacity_Throws_Under_Checked()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var words = stackalloc ulong[2];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            board.RunEnabledChunk(value, 5, Clamp.Constant(0, 100), words, 2, 64));
    }
#endif
}
