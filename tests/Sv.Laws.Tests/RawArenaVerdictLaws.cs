namespace Sv.Laws.Tests;

public sealed unsafe class RawArenaVerdictLaws
{
    static void SendCount(EffectWriter* writer, int count, int seedOffset)
    {
        for (var i = 0; i < count; i++)
        {
            var key = EffectKey.Pack((seedOffset + i) & 63, 1, 0, 0, Origin.Direct, 0);
            Assert.True(writer->Send(key, 1, 1));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void HardCapAfterCompletedLayerCountsSingleIntervalPerLayer(int writers)
    {
        using var raw = new RawArena(1, writers, ArenaPolicy.HardCap);
        var writer = raw.Writer(0);
        var exactBudget = checked((int)(raw.Capacity - 3L * writers * ArenaCore.BlockRecords));

        SendCount(writer, 1, 0);
        var layer1 = raw.Mark();
        SendCount(writer, 1, 1);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(layer1));

        var layer2Fit = raw.Mark();
        SendCount(writer, exactBudget - 2, 2);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(layer2Fit));
        Assert.Equal(0L, raw.DroppedLayers);
        Assert.Equal(0L, raw.Missed);

        raw.Reset();

        SendCount(writer, 1, 0);
        var resetLayer1 = raw.Mark();
        SendCount(writer, 1, 1);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(resetLayer1));

        var layer2Overflow = raw.Mark();
        SendCount(writer, exactBudget - 1, 2);
        Assert.Equal(LayerVerdict.Discarded, raw.EndLayer(layer2Overflow));
        Assert.Equal(1L, raw.DroppedLayers);
        Assert.Equal(exactBudget - 1L, raw.DroppedRecords);
    }

    [Fact]
    public void HardCapAcrossThreeCompletedLayersCountsEachLayerOnce()
    {
        const int writers = 4;
        using var raw = new RawArena(1, writers, ArenaPolicy.HardCap);
        var writer = raw.Writer(0);
        var budget = checked((int)(raw.Capacity - 4L * writers * ArenaCore.BlockRecords));

        SendCount(writer, 1, 0);
        var m1 = raw.Mark();
        SendCount(writer, 1, 1);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m1));

        var m2 = raw.Mark();
        SendCount(writer, 1, 2);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m2));

        var m3Fit = raw.Mark();
        SendCount(writer, budget - 3, 3);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m3Fit));

        raw.Reset();

        SendCount(writer, 1, 0);
        m1 = raw.Mark();
        SendCount(writer, 1, 1);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m1));

        m2 = raw.Mark();
        SendCount(writer, 1, 2);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m2));

        var m3Overflow = raw.Mark();
        SendCount(writer, budget - 2, 3);
        Assert.Equal(LayerVerdict.Discarded, raw.EndLayer(m3Overflow));
    }

    [Fact]
    public void GrowModeFitsWithinSingleIntervalSlackWithoutRerun()
    {
        const int writers = 4;
        using var raw = new RawArena(1, writers, ArenaPolicy.Grow);
        var writer = raw.Writer(0);
        var exactBudget = checked((int)(raw.Capacity - 3L * writers * ArenaCore.BlockRecords));

        SendCount(writer, 1, 0);
        var m1 = raw.Mark();
        SendCount(writer, 1, 1);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m1));

        var m2 = raw.Mark();
        SendCount(writer, exactBudget - 2, 2);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(m2));
        Assert.Equal(0, raw.Grows);
    }
}
