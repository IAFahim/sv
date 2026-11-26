using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sv.Columns.Entities.Tests;
using Unity.Burst;
using Unity.Core;
using Unity.Entities;

[assembly: RegisterGenericSystemType(typeof(Sv.Columns.Entities.ColumnsCardSystem<PoisonCardCell>))]
[assembly: RegisterGenericSystemType(typeof(Sv.Columns.Entities.ColumnsOutboxSystem<FireOutCell>))]
[assembly: RegisterGenericSystemType(typeof(Sv.Columns.Entities.ColumnsOutboxSystem<HealOutCell>))]

namespace Sv.Columns.Entities.Tests
{
    public struct PoisonCardCell : IComponentData, IColumnsCardCell
    {
        public int Value;

        public string Card => "poison";

        public int Level
        {
            get => Value;
            set => Value = value;
        }
    }

    public struct FireOutCell : IComponentData, IColumnsOutboxCell
    {
        public int Value;

        public string Channel => "Fire";

        public string? TargetLane => "Aim";

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public struct HealOutCell : IComponentData, IColumnsOutboxCell
    {
        public int Value;

        public string Channel => "Healing";

        public string? TargetLane => null;

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public sealed class CellScript
    {
        public const int FirstTick = 2;
        public const int Ticks = 24;

        public readonly List<Entity> Units = new List<Entity>();
        public readonly List<int> Slots = new List<int>();
        public readonly List<string> Failures = new List<string>();
        public readonly List<(int Unit, int Level)> Cards = new List<(int, int)>();
        public readonly List<(int Unit, int Amount)> Fire = new List<(int, int)>();
        public readonly List<(int Unit, int Amount)> Heal = new List<(int, int)>();
        public ColumnWorld Mirror = null!;
        public CompiledPack Pack = null!;
        public int Tick;
        public int TicksThisRender;

        public bool Done => Tick >= Ticks;

        public void Plan(int tick)
        {
            Cards.Clear();
            Fire.Clear();
            Heal.Clear();
            if (tick < FirstTick)
            {
                return;
            }

            if (tick == 3)
            {
                Cards.Add((1, 3));
            }

            if (tick == 7)
            {
                Cards.Add((4, 2));
            }

            if (tick == 11)
            {
                Cards.Add((1, 5));
            }

            for (var unit = 0; unit < Units.Count; unit++)
            {
                if ((tick + unit) % 3 == 0)
                {
                    Fire.Add((unit, 2 + unit % 4));
                }
            }

            if (tick % 4 == 1)
            {
                Heal.Add((tick % Units.Count, 5));
            }
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public partial class CellFeedSystem : SystemBase
    {
        public CellScript Script = null!;

        protected override void OnUpdate()
        {
            var script = Script;
            if (script.Done)
            {
                World.GetExistingSystemManaged<ColumnsFixedStepGroup>().Enabled = false;
                return;
            }

            script.TicksThisRender++;
            script.Plan(script.Tick);
            foreach (var (unit, level) in script.Cards)
            {
                EntityManager.SetComponentData(script.Units[unit], new PoisonCardCell { Value = level });
            }

            foreach (var (unit, amount) in script.Fire)
            {
                EntityManager.SetComponentData(script.Units[unit], new FireOutCell { Value = amount });
            }

            foreach (var (unit, amount) in script.Heal)
            {
                EntityManager.SetComponentData(script.Units[unit], new HealOutCell { Value = amount });
            }
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(ColumnsFixedStepGroup))]
    [UpdateAfter(typeof(ColumnsFixedStepSystem))]
    public partial class CellMirrorSystem : SystemBase
    {
        public CellScript Script = null!;

        protected override void OnUpdate()
        {
            var script = Script;
            if (script.Done)
            {
                return;
            }

            using (var hosts = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsHostHandle>()))
            {
                EntityManager.CompleteAllTrackedJobs();
                var host = ColumnsStepSystem.HostOf(EntityManager, hosts.GetSingletonEntity());
                host.Complete();
                var mirror = script.Mirror;
                var pack = script.Pack;
                var aim = pack.Lane("Aim");
                foreach (var (unit, amount) in script.Fire)
                {
                    var source = script.Slots[unit];
                    var aimed = mirror.Get(source, aim);
                    mirror.Writer(0).SendFrom(source, aimed <= 0 ? source : aimed - 1, pack.Channel("Fire"), amount);
                }

                foreach (var (unit, amount) in script.Heal)
                {
                    var source = script.Slots[unit];
                    mirror.Writer(0).SendFrom(source, source, pack.Channel("Healing"), amount);
                }

                foreach (var (unit, level) in script.Cards)
                {
                    mirror.Set(script.Slots[unit], pack.CardLane("poison"), level);
                }

                mirror.Step();
                if (host.World.Frame != mirror.Frame)
                {
                    script.Failures.Add($"tick {script.Tick}: the host is on frame {host.World.Frame}, the mirror on {mirror.Frame}");
                }

                if (host.World.Digest() != mirror.Digest())
                {
                    script.Failures.Add($"tick {script.Tick}: digest {host.World.Digest():X16} differs from the hand-stepped world {mirror.Digest():X16}");
                }
            }

            script.Tick++;
        }
    }

    public sealed class ColumnsClockTests
    {
        const float FixedStep = 1f / 60f;
        const int Capacity = 1024;
        const int Parts = 3;
        const string AimLane = "{ \"name\": \"Aim\", \"default\": 0 }";
        const string LastLane = "{ \"name\": \"FireTaken\", \"default\": 0 }";

        static string Arena() => File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns/Tests/Packs~/arena.json"));

        static string ArenaWithAim()
        {
            var json = Arena();
            Assert.That(json.Contains(LastLane), Is.True, "arena.json changed shape: the test adds its Aim lane after FireTaken");
            return json.Replace(LastLane, LastLane + ",\n    " + AimLane);
        }

        bool synchronousBefore;

        [SetUp]
        public void SetUp()
        {
            synchronousBefore = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        }

        [TearDown]
        public void TearDown() => BurstCompiler.Options.EnableBurstCompileSynchronously = synchronousBefore;

        static World BothClocks(params Type[] more)
        {
            var world = new World("sv columns clock");
            var systems = new List<Type> { typeof(ColumnsStepSystem), typeof(ColumnsFixedStepSystem) };
            systems.AddRange(more);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
            return world;
        }

        static Entity Config(EntityManager manager, BlobAssetReference<ColumnsPackBlob> blob, ColumnsClock clock)
        {
            var config = manager.CreateEntity(typeof(ColumnsWorldConfig));
            manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = Capacity, Writers = 1, ResolveParts = Parts, Kernel = FrameKernel.Rows });
            if (clock == ColumnsClock.FixedStep)
            {
                manager.AddComponent<ColumnsFixedStep>(config);
            }

            return config;
        }

        static int FrameOf(EntityManager manager)
        {
            using var hosts = manager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsHostHandle>());
            manager.CompleteAllTrackedJobs();
            var host = ColumnsStepSystem.HostOf(manager, hosts.GetSingletonEntity());
            host.Complete();
            return host.World.Frame;
        }

        static void Render(World world, int renderFps, int renders)
        {
            var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
            for (var at = 0; at < renders; at++)
            {
                world.SetTime(new TimeData(at / (double)renderFps, 1f / renderFps));
                simulation.Update();
            }
        }

        [Test]
        public void ARenderFrameConfigStepsOncePerRenderedFrame()
        {
            var blob = ColumnsPacks.Create(Arena());
            var world = BothClocks();
            try
            {
                Config(world.EntityManager, blob, ColumnsClock.RenderFrame);
                Render(world, 30, 12);
                Assert.AreEqual(12, FrameOf(world.EntityManager), "per-frame stepping ran another number of frames than render frames");
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void AFixedStepConfigStepsOncePerFixedTickAndNeverOnTheRenderedFrame()
        {
            var blob = ColumnsPacks.Create(Arena());
            var world = BothClocks();
            try
            {
                Config(world.EntityManager, blob, ColumnsClock.FixedStep);
                Render(world, 30, 12);
                Assert.That(FrameOf(world.EntityManager), Is.InRange(22, 23), "fixed stepping at 30 fps did not run two fixed ticks per render frame");
                using var hosts = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsHostHandle>());
                Assert.AreEqual(1, hosts.CalculateEntityCount(), "both clocks made a host");
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void LaneSetsApplyBetweenFramesAndTheLargerValueWinsATie()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var host = new ColumnsBurstHost(pack, 256, 1, 1);
            var world = host.World;
            var unit = world.Spawn(pack.Template("dummy"));
            var gone = world.Spawn(pack.Template("dummy"));
            world.Despawn(gone);
            var poison = pack.CardLane("poison");
            var sets = host.Sets;
            sets.Set(unit, poison, 2);
            sets.Set(unit, poison, 5);
            sets.Set(unit, poison, 3);
            sets.Set(gone, poison, 9);
            sets.Set(unit, world.View.Lanes - 1, 1);
            Assert.AreEqual(5, host.PendingSets);
            host.Step();
            Assert.AreEqual(0, host.PendingSets);
            Assert.AreEqual(2L, host.RefusedSets, "a set on a dead slot and a set on an engine lane were applied");
            using var classic = new ColumnWorld(pack, 256, 1, 1);
            var twin = classic.Spawn(pack.Template("dummy"));
            classic.Despawn(classic.Spawn(pack.Template("dummy")));
            classic.Set(twin, poison, 5);
            classic.Step();
            Assert.AreEqual(classic.Digest().ToString("X16"), world.Digest().ToString("X16"));
        }

        static IEnumerable<TestCaseData> RenderRates()
        {
            foreach (var renderFps in new[] { 30, 60, 144 })
            {
                yield return new TestCaseData(renderFps).SetName("Cards and outbox on the fixed clock at " + renderFps + " fps");
            }
        }

        [TestCaseSource(nameof(RenderRates))]
        public void CardsAndOutboxCellsOnTheFixedClockGiveTheHandSteppedDigestEveryTick(int renderFps)
        {
            var json = ArenaWithAim();
            var blob = ColumnsPacks.Create(json);
            using var pack = PackJson.Read(json).Compile();
            using var mirror = new ColumnWorld(pack, Capacity, 1, Parts);
            var world = BothClocks(typeof(ColumnsCardSystem<PoisonCardCell>), typeof(ColumnsOutboxSystem<FireOutCell>), typeof(ColumnsOutboxSystem<HealOutCell>), typeof(CellFeedSystem), typeof(CellMirrorSystem));
            try
            {
                var manager = world.EntityManager;
                Config(manager, blob, ColumnsClock.FixedStep);
                var script = new CellScript { Mirror = mirror, Pack = pack };
                var templates = new[] { "dummy", "shielded", "converter", "regen", "dummy", "sniper", "phoenix", "dummy", "shielded", "burning" };
                var aim = pack.Lane("Aim");
                foreach (var template in templates)
                {
                    script.Slots.Add(mirror.Spawn(pack.Template(template)));
                }

                for (var unit = 0; unit < templates.Length; unit++)
                {
                    var entity = manager.CreateEntity(typeof(ColumnsTemplate), typeof(PoisonCardCell), typeof(FireOutCell), typeof(HealOutCell));
                    manager.SetComponentData(entity, new ColumnsTemplate { Value = pack.Template(templates[unit]) });
                    if (unit % 2 == 0)
                    {
                        var aimed = script.Slots[(unit + 3) % templates.Length] + 1;
                        manager.AddBuffer<ColumnsLaneValue>(entity).Add(new ColumnsLaneValue { Lane = aim, Value = aimed });
                        mirror.Set(script.Slots[unit], aim, aimed);
                    }

                    script.Units.Add(entity);
                }

                world.GetExistingSystemManaged<CellFeedSystem>().Script = script;
                world.GetExistingSystemManaged<CellMirrorSystem>().Script = script;
                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                var renderDelta = 1.0 / renderFps;
                var elapsed = 0.0;
                var mostTicks = 0;
                var rendersWithoutATick = 0;
                var renders = 0;
                for (; !script.Done && renders < CellScript.Ticks * 8; renders++)
                {
                    script.TicksThisRender = 0;
                    world.SetTime(new TimeData(elapsed, (float)renderDelta));
                    simulation.Update();
                    elapsed += renderDelta;
                    mostTicks = Math.Max(mostTicks, script.TicksThisRender);
                    rendersWithoutATick += script.TicksThisRender == 0 ? 1 : 0;
                }

                manager.CompleteAllTrackedJobs();
                Assert.That(script.Failures, Is.Empty);
                Assert.IsTrue(script.Done, $"{renders} render frames at {renderFps} fps ran only {script.Tick} of {CellScript.Ticks} fixed ticks");
                for (var unit = 0; unit < templates.Length; unit++)
                {
                    Assert.AreEqual(script.Slots[unit], manager.GetComponentData<ColumnsSlot>(script.Units[unit]).Value, "the step system gave unit " + unit + " another slot than the hand-stepped world");
                }

                if (renderFps < 60)
                {
                    Assert.Greater(mostTicks, 1, $"at {renderFps} fps no render frame ran more than one fixed step");
                }

                if (renderFps > 60)
                {
                    Assert.Greater(rendersWithoutATick, 0, $"at {renderFps} fps every render frame ran a fixed step");
                }

                var unmanaged = world.Unmanaged;
                var cards = unmanaged.GetUnsafeSystemRef<ColumnsCardSystem<PoisonCardCell>>(world.GetExistingSystem<ColumnsCardSystem<PoisonCardCell>>());
                var fire = unmanaged.GetUnsafeSystemRef<ColumnsOutboxSystem<FireOutCell>>(world.GetExistingSystem<ColumnsOutboxSystem<FireOutCell>>());
                var heal = unmanaged.GetUnsafeSystemRef<ColumnsOutboxSystem<HealOutCell>>(world.GetExistingSystem<ColumnsOutboxSystem<HealOutCell>>());
                Assert.AreEqual(3L, cards.Set, "the card system set another number of card lanes than the script wrote");
                Assert.Greater(fire.Sent, 0L);
                Assert.Greater(heal.Sent, 0L);
                if (BurstCompiler.IsEnabled)
                {
                    Assert.AreEqual(1, cards.Probe, "the card system ran managed");
                    Assert.AreEqual(1, fire.Probe, "the fire outbox system ran managed");
                    Assert.AreEqual(1, heal.Probe, "the heal outbox system ran managed");
                }
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }
    }
}
