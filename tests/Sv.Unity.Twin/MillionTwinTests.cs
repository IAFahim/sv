using Sv.NorthStar2;
using Xunit;
using Xunit.Abstractions;

namespace Sv.Unity.Twin
{
    public class MillionTwinTests
    {
        public const int Ticks = 6;

        public const int Million = 1 << 20;

        public static readonly ulong[] Pinned1000 = { 0x939FBDEE2E7DEE50UL, 0xE5CE5FA31824A22EUL, 0x75035C43BBDC14AFUL, 0xE9C2A3B5AB8D2EC5UL, 0xE9C2A3B5AB8D2EC5UL, 0xE9C2A3B5AB8D2EC5UL };

        public static readonly ulong[] PinnedMillion = { 0xC02336ECA6D817FAUL, 0x139FA001187F8423UL, 0x08D2B1F0B05E6B55UL, 0x2E7A61280D20C9F4UL, 0x2E7A61280D20C9F4UL, 0x2E7A61280D20C9F4UL };

        readonly ITestOutputHelper output;

        public MillionTwinTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static List<Command>[] Journal(int rows)
        {
            var t0 = new List<Command>();
            for (var r = 0; r < rows; r++)
            {
                if (r % 3 == 0)
                {
                    t0.Add(Command.Start(r, FrameHost.Brawl));
                }
                else if (r % 3 == 1)
                {
                    t0.Add(Command.Start(r, FrameHost.Guard));
                }
            }

            t0.Add(Command.Add(5, 0, ModifierKind.Added, 50f, 7));
            t0.Add(Command.Add(6, 0, ModifierKind.Added, -60f, 8, 2));
            t0.Add(Command.Send(17, -40));
            t0.Add(Command.Send(17, 5));
            t0.Add(Command.Send(900, -100));
            t0.Add(Command.Local(2, -3));
            t0.Add(Command.Local(2, -4));
            var t1 = new List<Command> { Command.Remove(5, 7), Command.Send(3, -1_000_000), Command.Send(3, 999), Command.Local(8, -1), Command.Add(10, 1, ModifierKind.Multi, 0.5f, 9) };
            var t2 = new List<Command> { Command.Send(999, 3), Command.Local(11, -50), Command.Add(12, 0, ModifierKind.Added, -70f, 11), Command.Send(0, 0) };
            var result = new List<Command>[Ticks];
            result[0] = t0;
            result[1] = t1;
            result[2] = t2;
            for (var t = 3; t < Ticks; t++)
            {
                result[t] = new List<Command>();
            }

            return result;
        }

        public static ulong[] Run(int rows, int workers)
        {
            var journal = Journal(rows);
            using var host = new FrameHost(rows, workers);
            var digests = new ulong[Ticks];
            for (var t = 0; t < Ticks; t++)
            {
                digests[t] = host.Step(journal[t]);
            }

            return digests;
        }

        static string Hex(ulong[] digests) => string.Join(" ", digests.Select(d => d.ToString("X16")));

        [Fact]
        public void ThousandRowsSixTicks_StartsWithTheNorthStar2Pins()
        {
            var one = Run(1000, 1);
            output.WriteLine($"twin rows=1000 workers=1 digests={Hex(one)}");
            Assert.Equal(NorthStar2Tests.Baked, one.Take(3).ToArray());
            Assert.Equal(one, Run(1000, 11));
            Assert.Equal(Pinned1000, one);
        }

        [Fact]
        public void MillionRowsSixTicks_OneDigestPerTickAtOneAndElevenWorkers()
        {
            var eleven = Run(Million, 11);
            output.WriteLine($"twin rows={Million} workers=11 digests={Hex(eleven)}");
            Assert.Equal(eleven, Run(Million, 1));
            Assert.Equal(PinnedMillion, eleven);
        }
    }
}
