using Sv;
using Xunit;

public class DigestCadenceTests
{
    [Fact]
    public void Default_IsEveryTickInDebug_AndEveryEightInRelease()
    {
#if DEBUG
        Assert.True(DigestCadence.IsDebugBuild);
        Assert.Equal(1, DigestCadence.Default.Every);
#else
        Assert.False(DigestCadence.IsDebugBuild);
        Assert.Equal(8, DigestCadence.Default.Every);
#endif
        Assert.Equal(8, DigestCadence.ReleaseEvery);
        Assert.Equal(DigestCadence.Release, new DigestCadence(8));
        Assert.Equal(1, DigestCadence.EveryTick.Every);
        Assert.Equal(1L, default(DigestCadence).LastDue(1));
        Assert.True(default(DigestCadence).Due(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigestCadence(0));
    }

    [Fact]
    public void Due_AndLastDue_FollowTheCadence()
    {
        var every8 = DigestCadence.Release;
        var due = Enumerable.Range(0, 40).Where(t => every8.Due(t)).ToArray();
        Assert.Equal(new[] { 0, 8, 16, 24, 32 }, due);
        Assert.All(Enumerable.Range(0, 40), t => Assert.True(DigestCadence.EveryTick.Due(t)));
        Assert.Equal(16, every8.LastDue(16));
        Assert.Equal(16, every8.LastDue(23));
        Assert.Equal(24, every8.LastDue(24));
        Assert.Equal(-8, every8.LastDue(-1));
        Assert.Equal(-8, every8.LastDue(-8));
        Assert.Equal(41, DigestCadence.EveryTick.LastDue(41));
    }

    [Fact]
    public unsafe void SparseDigests_EqualTheEveryTickDigests_OnTheirTicks()
    {
        const int Rows = 256;
        var values = new int[Rows];
        var every = new ulong[33];
        var sparse = new Dictionary<long, ulong>();
        var cadence = new DigestCadence(4);
        var random = new Random(0xD16E);
        fixed (int* at = values)
        {
            for (long tick = 0; tick <= 32; tick++)
            {
                for (var i = 0; i < 20; i++)
                {
                    values[random.Next(Rows)] += random.Next(-5, 6);
                }

                every[tick] = CellDigest.Sum(1, at, 0, Rows);
                if (cadence.Due(tick))
                {
                    sparse[tick] = CellDigest.Sum(1, at, 0, Rows);
                }
            }
        }

        Assert.Equal(9, sparse.Count);
        foreach (var pair in sparse)
        {
            Assert.Equal(every[pair.Key], pair.Value);
            Assert.Equal(pair.Key, cadence.LastDue(pair.Key + 3));
        }
    }
}
