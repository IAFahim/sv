using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.Reference;
using Sv.Unity;

namespace Sv.Unity.Tests
{
    public sealed class BurstSmokeTests
    {
        [SetUp]
        public void SetUp() => SvWorlds.DisposeAll();

        [TearDown]
        public void TearDown() => SvWorlds.DisposeAll();

        static HostOptions Options(int workers) => new HostOptions { Workers = workers, Parts = 64, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 };

        [Test]
        public void PhasesRunUnderBurstAndNeverFallBackToManaged([Values(1, 4)] int workers)
        {
            using var world = PackWorld.Create(Options(workers));
            var target = world.Spawn(0);
            var source = world.Spawn(1);
            Assert.That(target, Is.GreaterThanOrEqualTo(0));
            Assert.That(source, Is.GreaterThanOrEqualTo(0));

            for (var frame = 0; frame < 16; frame++)
            {
                world.Send(target, source, (int)Kind.Sniper, (int)Channel.Physical, 3, 1, Origin.Direct);
                world.Step();
            }

            Assert.That(world.Frame, Is.EqualTo(16));
            Assert.That(world.Digest(), Is.Not.Zero);
            var seen = 0;
            for (var phase = 0; phase < FramePhases.Count; phase++)
            {
                var probe = world.ProbeOf((FramePhase)phase);
                Assert.That(probe, Is.Not.EqualTo(SvProbe.Managed), $"{(FramePhase)phase} ran as managed code, Burst did not compile it");
                if (probe == SvProbe.Burst)
                {
                    seen++;
                }
            }

            Assert.That(seen, Is.GreaterThan(0), "no phase ran as a Burst job");
            Assert.That(SvProbe.ManagedRuns, Is.Zero);
        }

        [Test]
        public unsafe void ProbeReadsManagedWhenCalledFromManagedCode()
        {
            var probe = 0;
            SvProbe.Mark(&probe);
            Assert.That(probe, Is.EqualTo(SvProbe.Managed), "Mark must read Managed outside Burst, otherwise the Burst assertion proves nothing");
            Assert.That(SvProbe.ManagedRuns, Is.GreaterThan(0));
        }
    }
}
