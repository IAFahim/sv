using Sv;
using Sv.Host;
using Sv.Kernels.NorthStar3;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Sv.NorthStar3
{
    public sealed class NorthStar3Tests
    {
        const int Frames = 9;
        const int Amount = 3;
        const int Ticks = 4;
        readonly ITestOutputHelper output;

        public NorthStar3Tests(ITestOutputHelper output) => this.output = output;

        static PackPlan Plan
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "tests", "Sv.Kernels.NorthStar3", "pack")))
                {
                    folder = folder.Parent;
                }

                var compiled = PackCompiler.Compile(Sv.Pack.PackFolder.Read(Path.Combine(folder!.FullName, "tests", "Sv.Kernels.NorthStar3", "pack")));
                return compiled.Value ?? throw new InvalidOperationException(string.Join("\n", compiled.Diagnostics));
            }
        }

        sealed record Run(int Victim, int Status, List<Cause> Causes, List<ulong> Digests, List<Tick> Ticks, int Health);

        static Run Play(string victim, string status, int workers)
        {
            using var rig = new Rig(Plan, workers);
            var target = rig.Spawn(victim);
            var source = rig.Spawn(status);
            var causes = new List<Cause> { new Cause(0, Rig.Aim, source, target + 1), new Cause(1, Rig.Start, source, Rig.PoisonTimeline) };
            var digests = new List<ulong>();
            for (var frame = 0L; frame < Frames; frame++)
            {
                foreach (var cause in causes)
                {
                    if (cause.Frame == frame)
                    {
                        rig.Apply(cause);
                    }
                }

                digests.Add(rig.Step());
            }

            return new Run(target, source, causes, digests, rig.Ticks, rig.Health(target));
        }

        [Fact]
        public void The_committed_glue_equals_the_emitter_output()
        {
            var emitted = Sv.Rules.Emit.TlGlueEmitter.Emit(Plan, "Sv.Kernels.NorthStar3");
            var committed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Glue.g.cs.txt"));
            Assert.Equal(committed.Replace("\r\n", "\n"), emitted);
        }

        [Fact]
        public void A_poison_status_entity_sends_its_ticks_to_the_victim_as_timeline_records()
        {
            var run = Play("player", "poison_status", 1);
            output.WriteLine(string.Join(" ", run.Ticks));
            Assert.Equal(Ticks, run.Ticks.Count);
            foreach (var tick in run.Ticks)
            {
                Assert.Equal(run.Victim, tick.Target);
                Assert.Equal(run.Status, tick.Source);
                Assert.Equal((int)Kind.Sniper, tick.Kind);
                Assert.Equal((int)Channel.Poison, tick.Channel);
                Assert.Equal(Amount, tick.Amount);
            }

            Assert.Equal(100 - Amount * Ticks, run.Health);
        }

        [Fact]
        public void Immune_to_snipers_drops_the_poison_ticks_of_a_sniper_status()
        {
            var run = Play("warded_player", "poison_status", 1);
            Assert.Equal(Ticks, run.Ticks.Count);
            Assert.Equal(100, run.Health);
        }

        [Fact]
        public void The_sniper_ward_does_not_drop_the_ticks_of_a_rifle_status()
        {
            var run = Play("warded_player", "rifle_status", 1);
            Assert.Equal(Ticks, run.Ticks.Count);
            Assert.All(run.Ticks, tick => Assert.Equal((int)Kind.Rifle, tick.Kind));
            Assert.Equal(100 - Amount * Ticks, run.Health);
        }

        [Fact]
        public void Collected_ticks_equal_the_same_records_sent_directly()
        {
            var run = Play("player", "poison_status", 1);
            using var host = new FrameLoop<HostKernels>(HostPack.From(Plan), new HostOptions { Workers = 1, Parts = 64 });
            var template = Plan.Schema.Templates;
            int Index(string name) => Array.FindIndex(template, t => t.Name == name);
            host.Spawn(Index("player"));
            host.Spawn(Index("poison_status"));
            var digests = new List<ulong>();
            for (var frame = 0L; frame < Frames; frame++)
            {
                if (frame == 0)
                {
                    host.Send(run.Status, EffectKey.NoSource, 0, (int)Channel.Aim, run.Victim + 1, 1, Origin.Direct);
                }

                foreach (var tick in run.Ticks)
                {
                    if (tick.Frame == frame)
                    {
                        host.Send(tick.Target, tick.Source, tick.Kind, tick.Channel, tick.Amount, 1, Origin.Timeline);
                    }
                }

                host.Step();
                digests.Add(host.LastDigest);
            }

            Assert.Equal(run.Digests, digests);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(11)]
        public void Every_worker_count_gives_one_digest_per_frame(int workers)
        {
            Assert.Equal(Play("warded_player", "poison_status", 1).Digests, Play("warded_player", "poison_status", workers).Digests);
            Assert.Equal(Play("player", "poison_status", 1).Digests, Play("player", "poison_status", workers).Digests);
        }

        [Fact]
        public void A_fresh_world_fed_only_the_causes_replays_every_digest()
        {
            var live = Play("player", "poison_status", 1);
            using var rig = new Rig(Plan, 1);
            rig.Spawn("player");
            rig.Spawn("poison_status");
            var replay = new List<ulong>();
            for (var frame = 0L; frame < Frames; frame++)
            {
                foreach (var cause in live.Causes)
                {
                    if (cause.Frame == frame)
                    {
                        rig.Apply(cause);
                    }
                }

                replay.Add(rig.Step());
            }

            Assert.Equal(live.Digests, replay);
            Assert.Equal(live.Ticks, rig.Ticks);
        }

        static Run Hurt(int damage, int workers)
        {
            using var rig = new Rig(Plan, workers);
            var victim = rig.Spawn("player");
            var causes = new List<Cause> { new Cause(0, Rig.Hit, victim, damage) };
            var digests = new List<ulong>();
            for (var frame = 0L; frame < Frames; frame++)
            {
                foreach (var cause in causes)
                {
                    if (cause.Frame == frame)
                    {
                        rig.Apply(cause);
                    }
                }

                digests.Add(rig.Step());
            }

            return new Run(victim, victim, causes, digests, rig.Ticks, rig.Health(victim));
        }

        [Fact]
        public void An_event_that_starts_a_timeline_requests_it_on_the_entity_that_went_low()
        {
            var run = Hurt(75, 1);
            output.WriteLine(string.Join(" ", run.Ticks));
            Assert.Equal(Ticks, run.Ticks.Count);
            Assert.All(run.Ticks, tick =>
            {
                Assert.Equal(run.Victim, tick.Target);
                Assert.Equal(run.Victim, tick.Source);
                Assert.Equal((int)Channel.Poison, tick.Channel);
            });
            Assert.Equal(100 - 75 - Amount * Ticks, run.Health);
        }

        [Fact]
        public void A_healthy_entity_requests_nothing()
        {
            var run = Hurt(10, 1);
            Assert.Empty(run.Ticks);
            Assert.Equal(90, run.Health);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(11)]
        public void Requests_give_one_digest_per_frame_at_every_worker_count(int workers) =>
            Assert.Equal(Hurt(75, 1).Digests, Hurt(75, workers).Digests);

        [Fact]
        public void Requests_replay_from_the_causes_alone()
        {
            var live = Hurt(75, 1);
            using var rig = new Rig(Plan, 1);
            rig.Spawn("player");
            var replay = new List<ulong>();
            for (var frame = 0L; frame < Frames; frame++)
            {
                foreach (var cause in live.Causes.Where(c => c.Frame == frame))
                {
                    rig.Apply(cause);
                }

                replay.Add(rig.Step());
            }

            Assert.Equal(live.Digests, replay);
            Assert.Equal(live.Ticks, rig.Ticks);
        }

        [Fact]
        public void Starts_round_trips_through_the_canonical_writer()
        {
            var plan = Plan;
            var low = plan.Schema.FindByName(ValueClass.Event, "LowHealth")!;
            Assert.Equal("poison", low.Starts);
            var written = PackWriter.Write(plan.Pack);
            Assert.Contains("\"starts\":\"poison\"", written.Values);
            var again = PackCompiler.Compile(written);
            Assert.NotNull(again.Value);
            Assert.Equal(plan.Hash, again.Value!.Hash);
        }

        [Fact]
        public void Starts_on_a_value_that_is_not_an_event_is_an_error()
        {
            var source = Sv.Pack.PackFolder.Read(Path.Combine(FindRoot(), "tests", "Sv.Kernels.NorthStar3", "pack"));
            var broken = new PackSource(source.Values.Replace("\"name\":\"Shield\",\"class\":\"intrinsic\"", "\"name\":\"Shield\",\"class\":\"intrinsic\",\"starts\":\"poison\""), source.Rules, source.Cards, source.Templates);
            var result = PackCompiler.Compile(broken);
            Assert.Null(result.Value);
            Assert.Contains(result.Diagnostics, d => d.Message.Contains("only for events"));
        }

        static string FindRoot()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "tests", "Sv.Kernels.NorthStar3", "pack")))
            {
                folder = folder.Parent;
            }

            return folder!.FullName;
        }
    }
}
