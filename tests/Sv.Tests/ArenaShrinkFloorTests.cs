using Sv;
using Xunit;

public unsafe class ArenaShrinkFloorTests
{
    [Fact]
    public void IdleSwaps_NeverShrinkAnArenaToZero_SoTheNextAppendCompletes()
    {
        using var streams = EventStreams.Create(1, 1 << 20);
        var channel = streams.CreateChannel<long>();
        for (var i = 0; i < 4; i++)
        {
            streams.Swap();
        }

        Assert.True(streams.ArenaBytes >= 2 * 4096, $"arena shrank to {streams.ArenaBytes} B; growth from 0 B never terminates");
        for (var i = 0; i < 1000; i++)
        {
            channel.Write(i);
        }

        streams.Swap();
        var read = 0;
        while (streams.TryReadNext(out _, out _, out _))
        {
            read++;
        }

        Assert.Equal(1000, read);
    }
}
