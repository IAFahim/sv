using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests;

public sealed unsafe class InputLogCapHostTests
{
    static LoggedRecord[] CopyFrame(InputLog log, long frame)
    {
        var count = log.RecordCount(frame);
        var copy = new LoggedRecord[count];
        if (count > 0)
        {
            fixed (LoggedRecord* ptr = copy)
            {
                Assert.Equal(count, log.CopyRecords(frame, ptr, count));
            }
        }

        return copy;
    }

    [Fact]
    public void FrameLoopCapIsDeterministicAcrossWorkerCountsAndBoundsLog()
    {
        const int cap = 6;
        const int frames = 40;
        var plan = Packs.Reference;
        var pack = HostPack.From(plan);
        var opts1 = new HostOptions
        {
            Workers = 1,
            Parts = 8,
            EntityCapacity = 512,
            InputLogFrameLimit = cap,
            InputLogFrameBytes = 8192L,
        };
        var opts4 = new HostOptions
        {
            Workers = 4,
            Parts = 8,
            EntityCapacity = 512,
            InputLogFrameLimit = cap,
            InputLogFrameBytes = 8192L,
        };

        using var host1 = new FrameLoop<ReferenceKernels>(pack, opts1);
        using var host4 = new FrameLoop<ReferenceKernels>(pack, opts4);
        var template = Packs.Template(plan, "player");
        var channel = Packs.Id(plan, Sv.Rules.ValueClass.Channel, "Physical");

        var targets1 = new int[96];
        var targets4 = new int[96];
        for (var i = 0; i < 96; i++)
        {
            targets1[i] = host1.Spawn(template);
            targets4[i] = host4.Spawn(template);
            Assert.Equal(targets1[i], targets4[i]);
        }

        for (var frame = 0; frame < frames; frame++)
        {
            for (var i = 0; i < 96; i++)
            {
                var amount = 5 + ((frame + i) % 13);
                host1.Send(targets1[i], EffectKey.NoSource, 0, channel, amount);
                host4.Send(targets4[i], EffectKey.NoSource, 0, channel, amount);
            }

            host1.Step();
            host4.Step();
            Assert.Equal(host1.LastDigest, host4.LastDigest);
            Assert.Equal(cap, host1.Log.RecordCount(frame));
            Assert.Equal(cap, host4.Log.RecordCount(frame));

            var copy1 = CopyFrame(host1.Log, frame);
            var copy4 = CopyFrame(host4.Log, frame);
            for (var i = 0; i < cap; i++)
            {
                Assert.Equal(copy1[i].Key, copy4[i].Key);
                Assert.Equal(copy1[i].Amount, copy4[i].Amount);
                Assert.Equal(copy1[i].Hits, copy4[i].Hits);
            }
        }

        Assert.Equal(frames * 90L, host1.Log.DroppedRecords);
        Assert.Equal(host1.Log.DroppedRecords, host4.Log.DroppedRecords);
        Assert.Equal(frames, host1.Log.CappedFrames);
        Assert.Equal(frames, host4.Log.CappedFrames);
    }

    [Fact]
    public void FrameLoopRejectsNegativeInputLogLimits()
    {
        var pack = HostPack.From(Packs.Reference);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameLoop<ReferenceKernels>(pack, new HostOptions { InputLogFrameLimit = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameLoop<ReferenceKernels>(pack, new HostOptions { InputLogFrameBytes = -1L }));
    }
}
