using System.Collections.Generic;
using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace ScenarioTests
{
    public sealed unsafe class RewindTests
    {
        const int Frames = 20;
        const int Physical = 0;
        const int Poison = 1;
        const int Bleeding = 2;
        const int Healing = 3;
        const int Sniper = 1;
        const int Rifle = 2;
        readonly ITestOutputHelper output;

        public RewindTests(ITestOutputHelper output) => this.output = output;

        sealed class Cast
        {
            public int Player;
            public int Sniper;
            public int Rifle;
            public int Medic;
        }

        static FrameLoop<ReferenceKernels> NewHost(StoragePolicy policy) => new FrameLoop<ReferenceKernels>(HostPack.From(Packs.Reference), Packs.Options(policy));

        static Cast SpawnCast(FrameLoop<ReferenceKernels> host)
        {
            var templates = Packs.Reference.Schema.Templates;
            int Of(string name)
            {
                for (var t = 0; t < templates.Length; t++)
                {
                    if (templates[t].Name == name)
                    {
                        return t;
                    }
                }

                return -1;
            }

            return new Cast
            {
                Player = host.Spawn(Of("worked_example_player")),
                Sniper = host.Spawn(Of("sniper")),
                Rifle = host.Spawn(Of("rifleman")),
                Medic = host.Spawn(Of("rifleman")),
            };
        }

        static void Feed(FrameLoop<ReferenceKernels> host, Cast cast, long frame)
        {
            host.Send(cast.Player, cast.Rifle, Rifle, Physical, (int)(frame % 5 + 1) * 3);
            if (frame % 3 == 0)
            {
                host.Send(cast.Rifle, cast.Sniper, Sniper, Physical, 7);
                host.Send(cast.Player, cast.Sniper, Sniper, Poison, 4);
            }

            if (frame % 2 == 0)
            {
                host.Send(cast.Player, cast.Medic, Rifle, Healing, 4);
                host.Send(cast.Medic, cast.Player, 0, Bleeding, 2);
            }
        }

        static List<ulong> Straight(FrameLoop<ReferenceKernels> host, Cast cast)
        {
            var digests = new List<ulong>();
            while (host.Frame < Frames)
            {
                Feed(host, cast, host.Frame);
                host.Step();
                digests.Add(host.Digest());
            }

            return digests;
        }

        [Theory]
        [InlineData(StoragePolicy.Shared, 7L)]
        [InlineData(StoragePolicy.Dense, 7L)]
        [InlineData(StoragePolicy.Shared, 3L)]
        public void Rewind_to_an_earlier_epoch_and_replay_gives_every_digest_of_the_straight_run(StoragePolicy policy, long rewindFrame)
        {
            using var host = NewHost(policy);
            var cast = SpawnCast(host);
            var straight = Straight(host, cast);
            var epoch = host.World.EpochAtOrBefore(rewindFrame);
            Assert.True(host.Rewind(epoch));
            var begin = host.Frame;
            output.WriteLine("rewound to epoch " + epoch + " at frame " + begin + ", straight final " + ScenarioRunner.Hex(straight[Frames - 1]));
            Assert.InRange(begin, 1L, rewindFrame);
            Assert.Equal(ScenarioRunner.Hex(straight[(int)begin - 1]), ScenarioRunner.Hex(host.Digest()));
            while (host.Frame < Frames)
            {
                var frame = host.Frame;
                Feed(host, cast, frame);
                host.Step();
                Assert.Equal(ScenarioRunner.Hex(straight[(int)frame]), ScenarioRunner.Hex(host.Digest()));
            }
        }

        [Fact]
        public void A_late_input_resolved_by_rewind_equals_the_run_that_had_it_on_time()
        {
            const long late = 6;
            ulong onTime;
            using (var host = NewHost(StoragePolicy.Shared))
            {
                var cast = SpawnCast(host);
                while (host.Frame < Frames)
                {
                    Feed(host, cast, host.Frame);
                    if (host.Frame == late)
                    {
                        host.Send(cast.Player, cast.Medic, Rifle, Physical, 30);
                    }

                    host.Step();
                }

                onTime = host.Digest();
            }

            using (var host = NewHost(StoragePolicy.Shared))
            {
                var cast = SpawnCast(host);
                Straight(host, cast);
                var record = new LoggedRecord(EffectKey.Pack(cast.Player, cast.Medic, Rifle, Physical, Origin.Direct, 0), 30, 1);
                Assert.Equal(late, host.Late(late, &record, 1));
                Assert.True(host.Resolve());
                output.WriteLine("on time " + ScenarioRunner.Hex(onTime) + " resolved " + ScenarioRunner.Hex(host.Digest()) + " replay faults " + host.ReplayFaults);
                Assert.Equal(Frames, host.Frame);
                Assert.Equal(0L, host.ReplayFaults);
                Assert.Equal(ScenarioRunner.Hex(onTime), ScenarioRunner.Hex(host.Digest()));
            }
        }
    }
}
