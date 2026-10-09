using System.Collections.Generic;
using System.Threading;
using Sv;
using Xunit;

public unsafe class EventsParallelTests
{
    public struct Tagged
    {
        public int Writer;

        public int Seq;
    }

    [Fact]
    public void ParallelWriters_DrainInWriterIndexThenAppendOrder()
    {
        const int writers = 8;
        const int per = 20_000;
        using var streams = EventStreams.Create(writers, 64);
        var channels = new Channel<Tagged>[writers];
        for (var w = 0; w < writers; w++)
        {
            channels[w] = streams.CreateChannel<Tagged>(w);
        }

        var threads = new Thread[writers];
        for (var w = 0; w < writers; w++)
        {
            var writer = w;
            threads[w] = new Thread(() =>
            {
                for (var i = 0; i < per; i++)
                {
                    channels[writer].Write(new Tagged { Writer = writer, Seq = i });
                }
            });
            threads[w].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        streams.Swap();
        var expectedWriter = 0;
        var expectedSeq = 0;
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            var evt = *(Tagged*)payload;
            Assert.Equal(expectedWriter, evt.Writer);
            Assert.Equal(expectedSeq, evt.Seq);
            expectedSeq++;
            if (expectedSeq == per)
            {
                expectedWriter++;
                expectedSeq = 0;
            }
        }

        Assert.Equal(writers, expectedWriter);
    }

    [Fact]
    public void SharedWriter_ConcurrentAppendsLoseNothing()
    {
        const int threadCount = 4;
        const int per = 25_000;
        using var streams = EventStreams.Create(1, 64);
        var channel = streams.CreateChannel<Tagged>();
        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var id = t;
            threads[t] = new Thread(() =>
            {
                for (var i = 0; i < per; i++)
                {
                    channel.Write(new Tagged { Writer = id, Seq = i });
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        streams.Swap();
        var next = new int[threadCount];
        var total = 0;
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            var evt = *(Tagged*)payload;
            Assert.Equal(next[evt.Writer], evt.Seq);
            next[evt.Writer]++;
            total++;
        }

        Assert.Equal(threadCount * per, total);
    }
}
