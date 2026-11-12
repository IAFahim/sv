using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Sv.NorthStar2
{
    public unsafe class NorthStar2Tests
    {
        public const int Rows = 1000;

        public static readonly ulong[] Baked = { 0x939FBDEE2E7DEE50UL, 0xE5CE5FA31824A22EUL, 0x75035C43BBDC14AFUL };

        public const ulong BakedFold = 0x0D945D8FD131DA2AUL;

        public const ulong BakedLinks = 0xA47434070C42E4ACUL;

        readonly ITestOutputHelper output;

        public NorthStar2Tests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static List<Command>[] Journal()
        {
            var t0 = new List<Command>();
            for (var r = 0; r < Rows; r++)
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
            var t1 = new List<Command>
            {
                Command.Remove(5, 7),
                Command.Send(3, -1_000_000),
                Command.Send(3, 999),
                Command.Local(8, -1),
                Command.Add(10, 1, ModifierKind.Multi, 0.5f, 9),
            };
            var t2 = new List<Command>
            {
                Command.Send(999, 3),
                Command.Local(11, -50),
                Command.Add(12, 0, ModifierKind.Added, -70f, 11),
                Command.Send(0, 0),
            };
            return new[] { t0, t1, t2 };
        }

        public static ulong[] Run(int workers, int shuffle = 0)
        {
            var journal = Journal();
            using var host = new FrameHost(Rows, workers);
            var digests = new ulong[journal.Length];
            for (var t = 0; t < journal.Length; t++)
            {
                digests[t] = host.Step(journal[t], shuffle);
            }

            return digests;
        }

        static string Hex(ulong[] digests) => string.Join(" ", digests.Select(d => d.ToString("X16")));

        [Fact]
        public void ThreeTicks_LiveEqualsReplay_AndMatchTheBakedDigests()
        {
            var live = Run(1);
            var replay = Run(1);
            output.WriteLine($"NorthStar2 digests: {Hex(live)}");
            Assert.Equal(live, replay);
            Assert.Equal(Baked, live);
        }

        [Fact]
        public void Semantics_SumThenClamp_Remote_Expiry_Events()
        {
            var journal = Journal();
            using var host = new FrameHost(Rows, 1);
            host.Step(journal[0]);
            Assert.Equal(95, host.Hp(0));
            Assert.Equal(150f, host.MaxHpHost(5));
            Assert.Equal(40f, host.MaxHpHost(6));
            Assert.Equal(35, host.Hp(6));
            Assert.Equal(30, host.Hp(17));
            Assert.False(host.IsLow(17));
            Assert.Equal(0, host.Hp(900));
            Assert.True(host.IsLow(900));
            Assert.Equal(68, host.Hp(2));
            host.Step(journal[1]);
            Assert.Equal(85, host.Hp(0));
            Assert.Equal(68, host.Hp(2));
            Assert.Equal(0, host.Hp(3));
            Assert.True(host.IsLow(3));
            Assert.Equal(100f, host.MaxHpHost(5));
            Assert.Equal(15f, host.ArmorHost(1));
            Assert.NotEqual(10f, host.ArmorHost(10));
            host.Step(journal[2]);
            Assert.Equal(141, Enumerable.Range(0, Rows).Single(r => host.Target(r) == 0 && r % 3 == 0));
            Assert.Equal(85 - 1 - 7, host.Hp(0));
            Assert.Equal(100f, host.MaxHpHost(6));
            Assert.Equal(30f, host.MaxHpHost(12));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(11)]
        public void WorkerSweep_EveryCountGivesOneDigestPerTick(int workers)
        {
            Assert.Equal(Run(1), Run(workers));
        }

        [Fact]
        public void HundredShuffles_OfContributionsAndInboxSends_GiveOneDigestPerTick()
        {
            var reference = Run(1);
            for (var shuffle = 1; shuffle <= 100; shuffle++)
            {
                Assert.Equal(reference, Run(4, shuffle));
            }
        }

        public static ulong FoldScenario()
        {
            const int rows = 4099;
            var capacity = ((rows + 63) >> 6) << 6;
            var value = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            var delta = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            var floor = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            var ceiling = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            var changed = (ulong*)NativeMemory.AllocZeroed((nuint)(capacity >> 6), sizeof(ulong));
            using var inbox = new InboxBuffer(capacity);
            ulong state = 0x5EED_F01D;
            ulong digest = 0;
            try
            {
                for (var tick = 0; tick < 8; tick++)
                {
                    for (var r = 0; r < rows; r++)
                    {
                        var a = Next(ref state);
                        if (tick == 0)
                        {
                            value[r] = (int)(a % 2000) - 500;
                            floor[r] = (int)((a >> 16) % 100) - 50;
                            ceiling[r] = (r % 11 == 0) ? floor[r] - 3 : floor[r] + 100 + (int)((a >> 24) % 1500);
                        }

                        delta[r] = (r % 5 == 0) ? 0 : (int)(Next(ref state) % 400) - 200;
                        if (r % 9 == 0)
                        {
                            Inbox.Send(inbox.Pointer, (int)(a % (ulong)rows), (int)(a >> 40) % 1000);
                        }
                    }

                    Kernel.Fold(new Column<int>(value, capacity), Window.Range(0, rows), new Column<int>(delta, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed, capacity >> 6), inbox.Column);
                    digest = unchecked(digest * 0x100000001B3ul + CellDigest.Sum(1, value, 0, rows));
                    for (var w = 0; w < capacity >> 6; w++)
                    {
                        digest = unchecked((digest ^ changed[w]) * 0x100000001B3ul);
                    }
                }

                return digest;
            }
            finally
            {
                NativeMemory.Free(value);
                NativeMemory.Free(delta);
                NativeMemory.Free(floor);
                NativeMemory.Free(ceiling);
                NativeMemory.Free(changed);
            }
        }

        static ulong Next(ref ulong state)
        {
            unchecked
            {
                var z = state += 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        public static ColumnDef[] LinkColumns() => new[]
        {
            new ColumnDef(0x4D41585F4850, ColumnRole.StatOutput, 0, 0),
            new ColumnDef(0x41524D4F52, ColumnRole.StatOutput, 1, 0),
            new ColumnDef(0x4850, ColumnRole.Intrinsic, 0, 0),
            new ColumnDef(0x48504454, ColumnRole.Delta, 0, 2),
            new ColumnDef(0x4850494E, ColumnRole.Inbox, 0, 2),
            new ColumnDef(0x4C4F57, ColumnRole.Condition, 0, 0),
        };

        public static Link[] LinkTable() => new[]
        {
            new Link(1, 3, LinkMode.Delta),
            new Link(2, 3, LinkMode.Delta, LinkScope.Remote),
            new Link(2, 4, LinkMode.Delta, LinkScope.Remote),
            new Link(3, 1, LinkMode.Modifier),
            new Link(4, 5, LinkMode.Event),
            new Link(4, 0, LinkMode.Read),
            new Link(4, 2, LinkMode.Check),
        };

        [Fact]
        public void SvAsset_IsTheOneThisLegAsksFor()
        {
            var framework = typeof(Board).Assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false).Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName;
            output.WriteLine($"sv asset {framework}");
#if SV_NS21
            Assert.Equal(".NETStandard,Version=v2.1", framework);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", framework);
#endif
        }

        [Fact]
        public void FoldScenario_AndLinkTable_MatchTheirBakedPins()
        {
            var fold = FoldScenario();
            Assert.Equal(fold, FoldScenario());
            Assert.Equal(LinkFault.None, Links.Validate(LinkTable(), LinkColumns()));
            var links = Links.Hash(LinkTable(), LinkColumns());
            output.WriteLine($"fold scenario {fold:X16} link table {links:X16}");
            Assert.Equal(BakedFold, fold);
            Assert.Equal(BakedLinks, links);
        }
    }
}
