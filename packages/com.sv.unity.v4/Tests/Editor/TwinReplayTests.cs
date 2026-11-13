using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Sv.Host;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor.PackageManager;

namespace Sv.Unity.Tests
{
    public sealed class TwinReplayTests
    {
        static string TwinFolder =>
            Path.Combine(PackageInfo.FindForAssembly(typeof(TwinReplayTests).Assembly)!.resolvedPath, "Tests", "Editor", "Twin");

        public static IEnumerable<TestCaseData> Journals()
        {
            foreach (var file in Directory.GetFiles(TwinFolder, "*.twin.txt", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(TwinFolder, file).Replace('\\', '/');
                foreach (var jobWorkers in new[] { 1, 11 })
                {
                    yield return new TestCaseData(jobWorkers, relative).SetName("Replay(" + relative + ", workers " + jobWorkers + ")");
                }
            }
        }

        static int workers;

        [SetUp]
        public void SetUp()
        {
            SvWorlds.DisposeAll();
            workers = JobsUtility.JobWorkerCount;
        }

        [TearDown]
        public void TearDown()
        {
            JobsUtility.JobWorkerCount = workers;
            SvWorlds.DisposeAll();
        }

        static ulong Hex(string text) => ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        static ISvWorld Create(string pack, HostOptions options) => pack switch
        {
            "reference" => Sv.Kernels.Reference.PackWorld.Create(options),
            "lego" => Sv.Kernels.Lego.PackWorld.Create(options),
            _ => throw new ArgumentException("unknown pack " + pack),
        };

        static ulong PackHash(string pack) => pack == "reference" ? Sv.Kernels.Reference.Layout.PackHash : Sv.Kernels.Lego.Layout.PackHash;

        [TestCaseSource(nameof(Journals))]
        public void Replay_reaches_the_twin_digest_every_frame(int jobWorkers, string journal) => Play(journal, jobWorkers, null);

        [Test]
        public void Every_phase_runs_as_a_Burst_job_somewhere_in_the_scenarios()
        {
            var burst = new bool[FramePhases.Count];
            foreach (var case_ in Journals())
            {
                Play((string)case_.Arguments[1], 1, burst);
            }

            for (var phase = 0; phase < burst.Length; phase++)
            {
                Assert.That(burst[phase], Is.True, $"no scenario drove {(FramePhase)phase} as a Burst job");
            }
        }

        static void Play(string journal, int jobWorkers, bool[]? burstSeen)
        {
            JobsUtility.JobWorkerCount = Math.Min(jobWorkers, JobsUtility.JobWorkerMaximumCount);
            var lines = File.ReadAllLines(Path.Combine(TwinFolder, journal));
            Assert.That(lines[0], Is.EqualTo("twin 1"));
            var head = lines[1].Split(' ');
            Assert.That(head[0], Is.EqualTo("pack"));
            var pack = head[1];
            Assert.That(Hex(head[2]), Is.EqualTo(PackHash(pack)), "the journal was recorded against another pack layout, rerun SV_UPDATE_TWIN=1 dotnet test tests/Sv.Twin.Fixtures");
            var capacity = Int(lines[3].Split(' ')[1]);
            using var world = Create(pack, new HostOptions { Workers = jobWorkers, Parts = 64, EntityCapacity = capacity });
            var frame = -1;
            for (var at = 5; at < lines.Length; at++)
            {
                var part = lines[at].Split(' ');
                switch (part[0])
                {
                    case "frame":
                        frame = Int(part[1]);
                        Assert.That(world.Frame, Is.EqualTo(frame));
                        break;
                    case "spawn":
                        Assert.That(world.Spawn(Int(part[1])), Is.EqualTo(Int(part[2])), $"frame {frame}: spawn got another slot");
                        break;
                    case "kill":
                        Assert.That(world.Kill(Int(part[1])), Is.True, $"frame {frame}: kill refused");
                        break;
                    case "add":
                        world.Modify(StatOp.Add(Int(part[1]), Int(part[2]), (ShapeKind)Int(part[3]), Int(part[4]), Int(part[5]), long.MaxValue, (StatLayer)Int(part[6])));
                        break;
                    case "remove":
                        world.Modify(StatOp.RemoveSource(Int(part[1]), Int(part[2])));
                        break;
                    case "send":
                        Assert.That(world.Send(Int(part[1]), Int(part[2]), Int(part[3]), Int(part[4]), Int(part[5]), Int(part[6]), (Origin)Int(part[7])), Is.True, $"frame {frame}: send refused");
                        break;
                    case "digest":
                        world.Step();
                        Assert.That(world.LastDigest, Is.EqualTo(Hex(part[1])), $"frame {frame}: digest differs from the .NET host");
                        break;
                    case "final":
                        Assert.That(world.Digest(), Is.EqualTo(Hex(part[1])), "final digest differs from the .NET host");
                        break;
                    default:
                        Assert.Fail("unknown journal line: " + lines[at]);
                        break;
                }
            }

            for (var phase = 0; phase < FramePhases.Count; phase++)
            {
                var probe = world.ProbeOf((FramePhase)phase);
                Assert.That(probe, Is.Not.EqualTo(SvProbe.Managed), $"{(FramePhase)phase} ran as managed code instead of Burst");
                if (burstSeen != null && probe == SvProbe.Burst)
                {
                    burstSeen[phase] = true;
                }
            }

            Assert.That(SvProbe.ManagedRuns, Is.Zero, "a phase ran as managed code instead of Burst");
        }
    }
}
