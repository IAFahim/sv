using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.NorthStar3;
using Sv.Kernels.NorthStar3.Glue;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor.PackageManager;

namespace Sv.Unity.TlTests
{
    public sealed unsafe class TlTimelineTwinTests
    {
        const int TimelineRows = 1024;
        const int StatusEntityIsTheSecondSpawn = 1;
        const int NoPoisonStartFromTheTest = -1;
        const int PoisonStartsOnFrameOne = 1;

        static string PackageFolder => PackageInfo.FindForAssembly(typeof(TlTimelineTwinTests).Assembly)!.resolvedPath;

        static string JournalPath(string journal) => Path.Combine(PackageFolder, "Tests", "Editor", "Twin", "northstar3", journal + ".twin.txt");

        static byte[] BakedLibrary() => File.ReadAllBytes(Path.Combine(PackageFolder, "Tests", "Tl", "Glue", "game.bytes"));

        static TestCaseData Journal(string journal, int poisonStartFrame, int jobWorkers) =>
            new TestCaseData(journal, poisonStartFrame, jobWorkers).SetName("Tl(" + journal + ", workers " + jobWorkers + ")");

        public static IEnumerable<TestCaseData> Journals()
        {
            foreach (var jobWorkers in new[] { 1, 11 })
            {
                yield return Journal("poison_status_on_player", PoisonStartsOnFrameOne, jobWorkers);
                yield return Journal("poison_status_on_warded_player", PoisonStartsOnFrameOne, jobWorkers);
                yield return Journal("rifle_status_on_warded_player", PoisonStartsOnFrameOne, jobWorkers);
                yield return Journal("low_health_requests_poison", NoPoisonStartFromTheTest, jobWorkers);
                yield return Journal("low_health_then_healed", NoPoisonStartFromTheTest, jobWorkers);
            }
        }

        int jobWorkersBefore;

        [SetUp]
        public void SetUp()
        {
            SvWorlds.DisposeAll();
            jobWorkersBefore = JobsUtility.JobWorkerCount;
        }

        [TearDown]
        public void TearDown()
        {
            JobsUtility.JobWorkerCount = jobWorkersBefore;
            SvWorlds.DisposeAll();
        }

        static ulong Hex(string text) => ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        static string SourceChannelAmount(int source, int channel, int amount) => source + " " + channel + " " + amount;

        static void ListWhatTlWrote(Span<int> outboxCells, Channel channel, List<string> tlWroteThisFrame)
        {
            for (var slot = 0; slot < outboxCells.Length; slot++)
            {
                if (outboxCells[slot] != 0)
                {
                    tlWroteThisFrame.Add(SourceChannelAmount(slot, (int)channel, outboxCells[slot]));
                }
            }
        }

        [TestCaseSource(nameof(Journals))]
        public void A_tl_timeline_feeds_the_outbox_and_every_frame_digest_equals_the_dotnet_host(string journal, int poisonStartFrame, int jobWorkers)
        {
            JobsUtility.JobWorkerCount = Math.Min(jobWorkers, JobsUtility.JobWorkerMaximumCount);
            var lines = File.ReadAllLines(JournalPath(journal));
            Assert.That(lines[0], Is.EqualTo("twin 1"));
            Assert.That(Hex(lines[1].Split(' ')[2]), Is.EqualTo(Layout.PackHash), "the journal was recorded against another pack layout, rerun SV_UPDATE_TWIN=1 dotnet test tests/Sv.NorthStar3");
            var capacity = Int(lines[3].Split(' ')[1]);
            using var sv = PackWorld.Create(new HostOptions { Workers = jobWorkers, Parts = 64, EntityCapacity = capacity });
            using var tl = new NorthStar3Timelines(BakedLibrary(), TimelineRows, jobWorkers);
            var poison = tl.Find("poison");
            Assert.That(poison, Is.Not.Zero, "the baked library has no poison timeline");
            var spawned = new List<int>();
            var tlWroteThisFrame = new List<string>();
            var dotnetCollectedThisFrame = new List<string>();
            var timelineRecordsCollected = 0;
            var frame = -1;
            for (var at = 5; at < lines.Length; at++)
            {
                var part = lines[at].Split(' ');
                switch (part[0])
                {
                    case "spawn":
                        spawned.Add(sv.Spawn(Int(part[1])));
                        Assert.That(spawned[spawned.Count - 1], Is.EqualTo(Int(part[2])), $"frame {frame}: spawn got another slot");
                        break;
                    case "send" when (Origin)Int(part[7]) == Origin.Direct:
                        Assert.That(sv.Send(Int(part[1]), Int(part[2]), Int(part[3]), Int(part[4]), Int(part[5]), Int(part[6]), Origin.Direct), Is.True, $"frame {frame}: send refused");
                        break;
                    case "send":
                        dotnetCollectedThisFrame.Add(SourceChannelAmount(Int(part[2]), Int(part[4]), Int(part[5])));
                        break;
                    case "frame":
                        frame = Int(part[1]);
                        Assert.That(sv.Frame, Is.EqualTo(frame));
                        if (frame == poisonStartFrame)
                        {
                            tl.Start(spawned[StatusEntityIsTheSecondSpawn], poison);
                        }

                        tl.TickAndFillOutboxes();
                        ListWhatTlWrote(tl.PoisonCells, Channel.Poison, tlWroteThisFrame);
                        ListWhatTlWrote(tl.BleedingCells, Channel.Bleeding, tlWroteThisFrame);
                        var collected = Outbox.Send(sv.Loop, tl.PoisonCells, (int)Channel.Poison, (int)Intrinsic.Kind, (int)Intrinsic.Target)
                            + Outbox.Send(sv.Loop, tl.BleedingCells, (int)Channel.Bleeding, (int)Intrinsic.Kind, (int)Intrinsic.Target);
                        Assert.That(collected, Is.EqualTo(tlWroteThisFrame.Count), $"frame {frame}: Collect refused a timeline record");
                        timelineRecordsCollected += collected;
                        break;
                    case "digest":
                        Assert.That(tlWroteThisFrame, Is.EqualTo(dotnetCollectedThisFrame), $"frame {frame}: tl in Unity wrote other outbox cells than the .NET host collected");
                        tlWroteThisFrame.Clear();
                        dotnetCollectedThisFrame.Clear();
                        sv.Step();
                        tl.StartAndStopFromSvEvents(sv.Loop.Events, sv.Loop.EventCount);
                        Assert.That(sv.LastDigest, Is.EqualTo(Hex(part[1])), $"frame {frame}: digest differs from the .NET host");
                        break;
                    case "final":
                        Assert.That(sv.Digest(), Is.EqualTo(Hex(part[1])), "final digest differs from the .NET host");
                        break;
                    default:
                        Assert.Fail("unknown journal line: " + lines[at]);
                        break;
                }
            }

            Assert.That(timelineRecordsCollected, Is.GreaterThan(0), "no tl timeline wrote an outbox cell");
            for (var phase = 0; phase < FramePhases.Count; phase++)
            {
                Assert.That(sv.ProbeOf((FramePhase)phase), Is.Not.EqualTo(SvProbe.Managed), $"{(FramePhase)phase} ran as managed code instead of Burst");
            }

            Assert.That(SvProbe.ManagedRuns, Is.Zero, "a phase ran as managed code instead of Burst");
        }
    }
}
