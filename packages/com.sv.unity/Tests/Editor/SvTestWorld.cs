using System;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Unity.Tests
{
    public struct TestHp : IComponentData { public int Value; }
    public struct TestHpDelta : IComponentData { public int Value; }
    public struct TestMaxHp : IComponentData { public float Value; }
    public struct TestMana : IComponentData { public int Value; }
    public struct TestManaDelta : IComponentData { public int Value; }
    public struct TestLow : IComponentData, IEnableableComponent { }

    public sealed class SvTestWorld : IDisposable
    {
        public readonly World World;
        public readonly Board Board;
        public readonly SvHost Host;
        public readonly Schema Schema;
        public readonly Entity[] Entities;
        public readonly int[] Slots;
        public readonly StatId MaxHp;
        public readonly IntrinsicId Hp;
        public readonly IntrinsicId Mana;
        public const int ManaCeiling = 50;

        public SvTestWorld(int rows, int capacity, int eventCapacity = 65536, bool reverseCreation = false)
        {
            Schema = SchemaBuilder.Create().Stat("max_hp", 100f).Intrinsic("hp").Intrinsic("mana").Build();
            MaxHp = Schema.Stat("max_hp");
            Hp = Schema.Intrinsic("hp");
            Mana = Schema.Intrinsic("mana");
            World = new World("sv-test");
            World.GetOrCreateSystemManaged<SimulationSystemGroup>();
            Board = Board.Create(Schema, capacity);
            var links = new SvLinkRegistry();
            links.Stat<TestMaxHp>(MaxHp);
            var hp = links.Intrinsic<TestHp, TestHpDelta>(Hp, SvBound.Of(0), SvBound.OfStat<TestMaxHp>(), true);
            links.Condition<TestLow>(hp, 30, 100, 40, 100, eventCapacity);
            links.Intrinsic<TestMana, TestManaDelta>(Mana, SvBound.Of(0), SvBound.Of(ManaCeiling));
            links.Link<TestHpDelta>(1, LinkMode.Delta);
            links.Link<TestHpDelta>(2, LinkMode.Delta, LinkScope.Remote);
            links.Link<TestManaDelta>(3, LinkMode.Delta);
            links.Link<TestMaxHp>(1, LinkMode.Read);
            Host = SvHost.Install(World, Board, links);
            var manager = World.EntityManager;
            var archetypeA = manager.CreateArchetype(typeof(SvSlot), typeof(TestHp), typeof(TestHpDelta), typeof(TestMaxHp), typeof(TestMana), typeof(TestManaDelta), typeof(TestLow));
            Entities = new Entity[rows];
            Slots = new int[rows];
            for (var n = 0; n < rows; n++)
            {
                var i = reverseCreation ? rows - 1 - n : n;
                var e = manager.CreateEntity(archetypeA);
                Entities[i] = e;
            }

            for (var i = 0; i < rows; i++)
            {
                var e = Entities[i];
                manager.SetComponentData(e, new TestHp { Value = 50 + i % 40 });
                manager.SetComponentData(e, new TestMaxHp { Value = 100f });
                manager.SetComponentData(e, new TestMana { Value = 10 });
                manager.SetComponentEnabled<TestLow>(e, false);
            }

            for (var i = 0; i < rows; i++)
            {
                Slots[i] = Host.Bind(Entities[i]);
            }
        }

        public void Frame() => World.GetExistingSystemManaged<SimulationSystemGroup>().Update();

        public int HpOf(int i) => World.EntityManager.GetComponentData<TestHp>(Entities[i]).Value;

        public int ManaOf(int i) => World.EntityManager.GetComponentData<TestMana>(Entities[i]).Value;

        public float MaxHpOf(int i) => World.EntityManager.GetComponentData<TestMaxHp>(Entities[i]).Value;

        public void AddHpDelta(int i, int amount)
        {
            var d = World.EntityManager.GetComponentData<TestHpDelta>(Entities[i]);
            d.Value += amount;
            World.EntityManager.SetComponentData(Entities[i], d);
        }

        public void AddManaDelta(int i, int amount)
        {
            var d = World.EntityManager.GetComponentData<TestManaDelta>(Entities[i]);
            d.Value += amount;
            World.EntityManager.SetComponentData(Entities[i], d);
        }

        public void Dispose()
        {
            Host.Dispose();
            World.Dispose();
            Board.Dispose();
        }
    }
}
