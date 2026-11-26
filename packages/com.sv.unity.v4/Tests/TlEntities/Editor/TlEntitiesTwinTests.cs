using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.NorthStar3;
using Sv.Kernels.NorthStar3.Glue;
using Sv.Unity.Timelines;
using Tl.Generated;
using Tl.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor.PackageManager;
using EcsWorld = Unity.Entities.World;

namespace Sv.Unity.TlEntitiesTests
{
    public sealed class JournalFrame
    {
        public readonly List<int[]> Spawns = new List<int[]>();
        public readonly List<string[]> DirectSends = new List<string[]>();
        public readonly List<string> TimelineSends = new List<string>();
        public long Number = -1;
        public ulong Digest;
    }

    public sealed class Replay
    {
        public readonly List<JournalFrame> Frames = new List<JournalFrame>();
        public readonly List<string> Failures = new List<string>();
        public readonly List<int> Spawned = new List<int>();
        public readonly Dictionary<int, Entity> PlayerOf = new Dictionary<int, Entity>();
        public ISvWorld Sv = null!;
        public int PoisonStartFrame;
        public int StatusSpawn;
        public ushort Poison;
        public int Next;
        public int TicksThisRender;
        public int TimelineCells;
        public ulong Final;

        public bool Done => Next >= Frames.Count;

        public JournalFrame Current => Frames[Next];
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public partial class JournalFeedSystem : SystemBase
    {
        public Replay Replay = null!;

        protected override void OnUpdate()
        {
            var replay = Replay;
            if (replay.Done)
            {
                World.GetExistingSystemManaged<SvFixedStepGroup>().Enabled = false;
                return;
            }

            replay.TicksThisRender++;
            var frame = replay.Current;
            if (replay.Sv.Frame != frame.Number)
            {
                replay.Failures.Add($"frame {frame.Number}: the sv world is on frame {replay.Sv.Frame}");
            }

            foreach (var spawn in frame.Spawns)
            {
                var slot = replay.Sv.Spawn(spawn[0]);
                replay.Spawned.Add(slot);
                if (slot != spawn[1])
                {
                    replay.Failures.Add($"frame {frame.Number}: spawn got slot {slot}, the journal says {spawn[1]}");
                }

                replay.PlayerOf[slot] = TlEntitiesTwinTests.Player(EntityManager, slot);
            }

            foreach (var send in frame.DirectSends)
            {
                if (!replay.Sv.Send(TlEntitiesTwinTests.Int(send[1]), TlEntitiesTwinTests.Int(send[2]), TlEntitiesTwinTests.Int(send[3]), TlEntitiesTwinTests.Int(send[4]), TlEntitiesTwinTests.Int(send[5]), TlEntitiesTwinTests.Int(send[6]), Origin.Direct))
                {
                    replay.Failures.Add($"frame {frame.Number}: direct send refused");
                }
            }

            if (frame.Number == replay.PoisonStartFrame)
            {
                var status = replay.PlayerOf[replay.Spawned[replay.StatusSpawn]];
                global::Tl.TlRequests.Start(EntityManager.GetBuffer<TimelineRequest>(status), replay.Poison);
            }
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(SvSendGroup), OrderFirst = true)]
    public partial class OutboxCellRecordSystem : SystemBase
    {
        public Replay Replay = null!;

        protected override void OnUpdate()
        {
            var replay = Replay;
            if (replay.Done)
            {
                return;
            }

            EntityManager.CompleteAllTrackedJobs();
            var wrote = new List<string>();
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SvSlot>(), ComponentType.ReadOnly<PoisonOut>(), ComponentType.ReadOnly<BleedingOut>()))
            {
                var slots = query.ToComponentDataArray<SvSlot>(Allocator.Temp);
                var poison = query.ToComponentDataArray<PoisonOut>(Allocator.Temp);
                var bleeding = query.ToComponentDataArray<BleedingOut>(Allocator.Temp);
                for (var row = 0; row < slots.Length; row++)
                {
                    if (poison[row].Value != 0)
                    {
                        wrote.Add(TlEntitiesTwinTests.SourceChannelAmount(slots[row].Value, (int)Channel.Poison, poison[row].Value));
                    }

                    if (bleeding[row].Value != 0)
                    {
                        wrote.Add(TlEntitiesTwinTests.SourceChannelAmount(slots[row].Value, (int)Channel.Bleeding, bleeding[row].Value));
                    }
                }
            }

            var expected = new List<string>(replay.Current.TimelineSends);
            wrote.Sort(StringComparer.Ordinal);
            expected.Sort(StringComparer.Ordinal);
            if (string.Join(", ", wrote) != string.Join(", ", expected))
            {
                replay.Failures.Add($"frame {replay.Current.Number}: tl wrote outbox cells [{string.Join(", ", wrote)}], the .NET host collected [{string.Join(", ", expected)}]");
            }

            replay.TimelineCells += wrote.Count;
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(SvEventGroup), OrderLast = true)]
    public partial class DigestRecordSystem : SystemBase
    {
        public Replay Replay = null!;

        protected override void OnUpdate()
        {
            var replay = Replay;
            if (replay.Done)
            {
                return;
            }

            var frame = replay.Current;
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SvFrame>(), ComponentType.ReadOnly<SvFixedStep>()))
            {
                var stepped = query.GetSingleton<SvFrame>();
                if (stepped.Frame != frame.Number + 1)
                {
                    replay.Failures.Add($"frame {frame.Number}: the published frame is {stepped.Frame}");
                }

                if (stepped.LastDigest != frame.Digest)
                {
                    replay.Failures.Add($"frame {frame.Number}: digest {stepped.LastDigest:X16} differs from the .NET host {frame.Digest:X16}");
                }
            }

            replay.Next++;
        }
    }

    public sealed unsafe class TlEntitiesTwinTests
    {
        const int StatusEntityIsTheSecondSpawn = 1;
        const int NoPoisonStartFromTheTest = -1;
        const int PoisonStartsOnFrameOne = 1;
        const float FixedStep = 1f / 60f;

        static string PackageFolder => PackageInfo.FindForAssembly(typeof(TlEntitiesTwinTests).Assembly)!.resolvedPath;

        static string JournalPath(string journal) => Path.Combine(PackageFolder, "Tests", "Editor", "Twin", "northstar3", journal + ".twin.txt");

        static byte[] BakedLibrary() => File.ReadAllBytes(Path.Combine(PackageFolder, "Tests", "Tl", "Glue", "game.bytes"));

        static TestCaseData Journal(string journal, int poisonStartFrame, int renderFps, int jobWorkers) =>
            new TestCaseData(journal, poisonStartFrame, renderFps, jobWorkers).SetName("TlEntities(" + journal + ", " + renderFps + " fps, workers " + jobWorkers + ")");

        public static IEnumerable<TestCaseData> Journals()
        {
            foreach (var renderFps in new[] { 30, 60, 144 })
            {
                foreach (var jobWorkers in new[] { 1, 11 })
                {
                    yield return Journal("poison_status_on_player", PoisonStartsOnFrameOne, renderFps, jobWorkers);
                    yield return Journal("poison_status_on_warded_player", PoisonStartsOnFrameOne, renderFps, jobWorkers);
                    yield return Journal("rifle_status_on_warded_player", PoisonStartsOnFrameOne, renderFps, jobWorkers);
                    yield return Journal("low_health_requests_poison", NoPoisonStartFromTheTest, renderFps, jobWorkers);
                    yield return Journal("low_health_then_healed", NoPoisonStartFromTheTest, renderFps, jobWorkers);
                }
            }
        }

        int jobWorkersBefore;
        bool synchronousBefore;

        [SetUp]
        public void SetUp()
        {
            SvWorlds.DisposeAll();
            jobWorkersBefore = JobsUtility.JobWorkerCount;
            synchronousBefore = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        }

        [TearDown]
        public void TearDown()
        {
            BurstCompiler.Options.EnableBurstCompileSynchronously = synchronousBefore;
            JobsUtility.JobWorkerCount = jobWorkersBefore;
            SvWorlds.DisposeAll();
        }

        static ulong Hex(string text) => ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        public static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        public static string SourceChannelAmount(int source, int channel, int amount) => source + " " + channel + " " + amount;

        public static Entity Player(EntityManager manager, int slot)
        {
            var types = new List<ComponentType>
            {
                typeof(SvSlot),
                typeof(Playback),
                typeof(TimelineRequest),
                typeof(TimelinePlaying),
                typeof(PoisonOut),
                typeof(BleedingOut),
            };
            types.AddRange(PoisonFeedKind.Types);
            types.AddRange(BleedingFeedKind.Types);
            var entity = manager.CreateEntity(types.ToArray());
            manager.SetComponentData(entity, new SvSlot { Value = slot });
            manager.SetComponentEnabled<TimelinePlaying>(entity, false);
            PoisonFeedKind.Init(manager, entity);
            BleedingFeedKind.Init(manager, entity);
            return entity;
        }

        static Replay Read(string journal)
        {
            var lines = File.ReadAllLines(JournalPath(journal));
            Assert.That(lines[0], Is.EqualTo("twin 1"));
            Assert.That(Hex(lines[1].Split(' ')[2]), Is.EqualTo(Layout.PackHash), "the journal was recorded against another pack layout, rerun SV_UPDATE_TWIN=1 dotnet test tests/Sv.NorthStar3");
            var replay = new Replay();
            var pending = new JournalFrame();
            for (var at = 5; at < lines.Length; at++)
            {
                var part = lines[at].Split(' ');
                switch (part[0])
                {
                    case "spawn":
                        pending.Spawns.Add(new[] { Int(part[1]), Int(part[2]) });
                        break;
                    case "send" when (Origin)Int(part[7]) == Origin.Direct:
                        pending.DirectSends.Add(part);
                        break;
                    case "send":
                        pending.TimelineSends.Add(SourceChannelAmount(Int(part[2]), Int(part[4]), Int(part[5])));
                        break;
                    case "frame":
                        pending.Number = Int(part[1]);
                        break;
                    case "digest":
                        pending.Digest = Hex(part[1]);
                        replay.Frames.Add(pending);
                        pending = new JournalFrame();
                        break;
                    case "final":
                        replay.Final = Hex(part[1]);
                        break;
                    default:
                        Assert.Fail("unknown journal line: " + lines[at]);
                        break;
                }
            }

            Assert.That(pending.Spawns.Count + pending.DirectSends.Count + pending.TimelineSends.Count, Is.Zero, "the journal ends with lines after its last digest");
            return replay;
        }

        static EcsWorld FixedStepWorld()
        {
            var world = new EcsWorld("sv-tl-entities");
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type>
            {
                typeof(TimelineRequestSystem),
                typeof(TimelineAdvanceSystem),
                typeof(TimelineKindTickSystem),
                typeof(TlKindSystem<TlAuto_Sv_NorthStar3_Entities_Jobs>),
                typeof(SvOpenFrameSystem),
                typeof(SvOutboxSystem<PoisonOut>),
                typeof(SvOutboxSystem<BleedingOut>),
                typeof(SvFixedStepSystem),
                typeof(SvTimelineRequestSystem),
                typeof(JournalFeedSystem),
                typeof(OutboxCellRecordSystem),
                typeof(DigestRecordSystem),
            });
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
            return world;
        }

        [TestCaseSource(nameof(Journals))]
        public void Sv_steps_in_the_fixed_step_group_after_tl_and_every_frame_digest_equals_the_dotnet_host(string journal, int poisonStartFrame, int renderFps, int jobWorkers)
        {
            JobsUtility.JobWorkerCount = Math.Min(jobWorkers, JobsUtility.JobWorkerMaximumCount);
            var replay = Read(journal);
            var capacity = Int(File.ReadAllLines(JournalPath(journal))[3].Split(' ')[1]);
            using var sv = PackWorld.Create(new HostOptions { Workers = jobWorkers, Parts = 64, EntityCapacity = capacity });
            using var library = TlLibrary.FromBytes(BakedLibrary());
            var world = FixedStepWorld();
            try
            {
                var manager = world.EntityManager;
                library.Publish(manager);
                var handle = SvEntities.Publish(manager, sv, SvClock.FixedStep);
                TimelineRules.AddTo(SvTimelines.Rules(manager, handle), library.Find);
                replay.Sv = sv;
                replay.PoisonStartFrame = poisonStartFrame;
                replay.StatusSpawn = StatusEntityIsTheSecondSpawn;
                replay.Poison = library.Find("poison");
                Assert.That(replay.Poison, Is.Not.Zero, "the baked library has no poison timeline");
                world.GetExistingSystemManaged<JournalFeedSystem>().Replay = replay;
                world.GetExistingSystemManaged<OutboxCellRecordSystem>().Replay = replay;
                world.GetExistingSystemManaged<DigestRecordSystem>().Replay = replay;

                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                var renderDelta = 1.0 / renderFps;
                var elapsed = 0.0;
                var mostTicks = 0;
                var rendersWithoutATick = 0;
                var renders = 0;
                for (; !replay.Done && renders < replay.Frames.Count * 8 + 8; renders++)
                {
                    replay.TicksThisRender = 0;
                    world.SetTime(new TimeData(elapsed, (float)renderDelta));
                    simulation.Update();
                    elapsed += renderDelta;
                    mostTicks = Math.Max(mostTicks, replay.TicksThisRender);
                    rendersWithoutATick += replay.TicksThisRender == 0 ? 1 : 0;
                }

                manager.CompleteAllTrackedJobs();
                Assert.That(replay.Failures, Is.Empty);
                Assert.That(replay.Done, Is.True, $"{renders} render frames at {renderFps} fps stepped only {replay.Next} of {replay.Frames.Count} journal frames");
                Assert.That(sv.Digest(), Is.EqualTo(replay.Final), "final digest differs from the .NET host");
                Assert.That(sv.Frame, Is.EqualTo(replay.Frames.Count), "sv stepped more fixed ticks than the journal has frames");
                if (renderFps < 60)
                {
                    Assert.That(mostTicks, Is.GreaterThan(1), $"at {renderFps} fps no render frame ran more than one fixed step");
                }

                if (renderFps > 60)
                {
                    Assert.That(rendersWithoutATick, Is.GreaterThan(0), $"at {renderFps} fps every render frame ran a fixed step");
                }

                Assert.That(replay.TimelineCells, Is.GreaterThan(0), "no tl timeline wrote an outbox cell");
                var unmanaged = world.Unmanaged;
                var poisonOutbox = unmanaged.GetUnsafeSystemRef<SvOutboxSystem<PoisonOut>>(world.GetExistingSystem<SvOutboxSystem<PoisonOut>>());
                var bleedingOutbox = unmanaged.GetUnsafeSystemRef<SvOutboxSystem<BleedingOut>>(world.GetExistingSystem<SvOutboxSystem<BleedingOut>>());
                var requests = unmanaged.GetUnsafeSystemRef<SvTimelineRequestSystem>(world.GetExistingSystem<SvTimelineRequestSystem>());
                Assert.That(poisonOutbox.Sent + bleedingOutbox.Sent, Is.EqualTo(replay.TimelineCells), "the outbox systems sent another number of records than tl wrote cells");
                Assert.That(poisonOutbox.Refused + bleedingOutbox.Refused, Is.Zero, "sv refused a timeline record");
                Assert.That(poisonOutbox.Probe, Is.EqualTo(SvProbe.Burst), "the poison outbox system ran as managed code instead of Burst");
                Assert.That(bleedingOutbox.Probe, Is.EqualTo(SvProbe.Burst), "the bleeding outbox system ran as managed code instead of Burst");
                if (poisonStartFrame == NoPoisonStartFromTheTest)
                {
                    Assert.That(requests.Started, Is.GreaterThan(0), "no sv event started a tl timeline");
                    Assert.That(requests.Probe, Is.EqualTo(SvProbe.Burst), "the timeline request system ran as managed code instead of Burst");
                }

                Assert.That(requests.Unmatched, Is.Zero, "an sv event named a slot with no tl player");
                for (var phase = 0; phase < FramePhases.Count; phase++)
                {
                    Assert.That(sv.ProbeOf((FramePhase)phase), Is.Not.EqualTo(SvProbe.Managed), $"{(FramePhase)phase} ran as managed code instead of Burst");
                }

                Assert.That(SvProbe.ManagedRuns, Is.Zero, "a phase ran as managed code instead of Burst");
            }
            finally
            {
                world.EntityManager.CompleteAllTrackedJobs();
                world.Dispose();
            }
        }

        [Test]
        public void A_published_tl_library_moves_a_render_frame_sv_world_onto_the_fixed_step_clock()
        {
            using var sv = PackWorld.Create(new HostOptions { Workers = 1, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 });
            using var library = TlLibrary.FromBytes(BakedLibrary());
            var world = new EcsWorld("sv-tl-clock");
            try
            {
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type> { typeof(SvTimelineClockSystem), typeof(SvStepSystem), typeof(SvOpenFrameSystem), typeof(SvFixedStepSystem) });
                var manager = world.EntityManager;
                var handle = SvEntities.Publish(manager, sv);
                Assert.That(manager.HasComponent<SvFixedStep>(handle), Is.False, "per-frame stepping is the default");
                world.GetExistingSystemManaged<InitializationSystemGroup>().Update();
                Assert.That(manager.HasComponent<SvFixedStep>(handle), Is.False, "the clock moved with no tl library published");
                library.Publish(manager);
                world.GetExistingSystemManaged<InitializationSystemGroup>().Update();
                Assert.That(manager.HasComponent<SvFixedStep>(handle), Is.True, "a published tl library left sv on the render-frame clock");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
