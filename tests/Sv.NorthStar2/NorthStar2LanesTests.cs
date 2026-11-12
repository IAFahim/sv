using Xunit;

namespace Sv.NorthStar2
{
    public class NorthStar2LanesTests
    {
        static ulong[] Run(int workers, int cells, int shuffle)
        {
            var journal = NorthStar2Tests.Journal();
            using var host = new FrameHost(NorthStar2Tests.Rows, workers, useLanes: true, laneCells: cells);
            var digests = new ulong[journal.Length];
            for (var t = 0; t < journal.Length; t++)
            {
                digests[t] = host.Step(journal[t], shuffle);
            }

            return digests;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(11)]
        public void InboxLanes_ReproduceTheBakedDigests_AtEveryWorkerCount(int workers)
        {
            Assert.Equal(NorthStar2Tests.Baked, Run(workers, InboxLanes.DefaultCells, 0));
            Assert.Equal(NorthStar2Tests.Baked, Run(workers, 16, 0));
            Assert.Equal(NorthStar2Tests.Baked, Run(workers, 16, 0x1A2B + workers));
        }
    }
}
