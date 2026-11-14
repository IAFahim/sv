using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed unsafe class CardLaws
{
    const int Stat = 0;
    const int CardA = 1;
    const int CardB = 2;
    const int Base = 1000;

    static readonly LaneClass[] Classes = { LaneClass.Stat, LaneClass.Intrinsic, LaneClass.Intrinsic };

    static readonly CardTerm[] Cards =
    {
        new CardTerm(Stat, CardA, ShapeKind.Added, 500),
        new CardTerm(Stat, CardA, ShapeKind.Multi, 1000),
        new CardTerm(Stat, CardB, ShapeKind.Cap, 900),
    };

    sealed class Rig : IDisposable
    {
        public readonly World World;
        public readonly StatMachine Stats;

        public Rig(StoragePolicy policy)
        {
            World = World.Create(Classes, 1, 256, policy, Journal.DefaultRing, 1);
            Stats = new StatMachine(World, new[] { new StatDef(Stat, Base, 1) }, ReadOnlySpan<StatSource>.Empty, ReadOnlySpan<int>.Empty, Cards, Journal.DefaultRing);
            World.AddTemplate(new uint[] { Base, 0u, 0u });
            World.AddTemplate(new uint[] { Base, 1u, 0u });
        }

        public int Spawn(int template)
        {
            var slot = World.Acquire(template);
            Stats.Spawned(slot);
            return slot;
        }

        public void Card(int slot, int lane, int value)
        {
            Assert.True(World.Write(slot, lane, value));
            Stats.CardRowsChanged(Slot.Chunk(slot), Stats.CardOf(lane), Slot.Bit(slot));
        }

        public void EndFrame()
        {
            World.EndFrame();
            Stats.MarkEpoch(World.Epoch);
        }

        public int Value(int slot) => World.Read(slot, Stat);

        public void Dispose()
        {
            Stats.Dispose();
            World.Dispose();
        }
    }

    static int Reference(params (ShapeKind Kind, int Value)[] terms) =>
        ReferenceLaw.StatFold(Base, terms.Select(term => new ShapeEntry(term.Kind, term.Value)).ToList());

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void A_held_card_folds_into_the_stat_on_the_next_recalculate_and_a_lost_card_restores_it(StoragePolicy policy)
    {
        using var rig = new Rig(policy);
        var slot = rig.Spawn(0);
        rig.Stats.Recalculate();
        Assert.Equal(Base, rig.Value(slot));

        rig.Card(slot, CardA, 1);
        Assert.Equal(Base, rig.Value(slot));
        rig.Stats.Recalculate();
        Assert.Equal(Reference((ShapeKind.Added, 500), (ShapeKind.Multi, 1000)), rig.Value(slot));

        rig.Card(slot, CardB, 3);
        rig.Stats.Recalculate();
        Assert.Equal(Reference((ShapeKind.Added, 500), (ShapeKind.Multi, 1000), (ShapeKind.Cap, 900)), rig.Value(slot));

        rig.Card(slot, CardA, 0);
        rig.Stats.Recalculate();
        Assert.Equal(Reference((ShapeKind.Cap, 900)), rig.Value(slot));

        rig.Card(slot, CardB, -4);
        rig.Stats.Recalculate();
        Assert.Equal(Base, rig.Value(slot));
        Assert.Equal(1, rig.Stats.ChangedPairCount);
    }

    [Fact]
    public void A_card_change_that_does_not_cross_zero_changes_no_stat()
    {
        using var rig = new Rig(StoragePolicy.Shared);
        var slot = rig.Spawn(1);
        rig.Stats.Recalculate();
        var held = rig.Value(slot);
        rig.Card(slot, CardA, 7);
        rig.Stats.Recalculate();
        Assert.Equal(held, rig.Value(slot));
        Assert.Equal(0, rig.Stats.ChangedPairCount);
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void Spawning_from_a_template_that_holds_a_card_folds_it_on_the_first_recalculate(StoragePolicy policy)
    {
        using var rig = new Rig(policy);
        var holder = rig.Spawn(1);
        var plain = rig.Spawn(0);
        Assert.Equal(Base, rig.Value(holder));
        rig.Stats.Recalculate();
        Assert.Equal(Reference((ShapeKind.Added, 500), (ShapeKind.Multi, 1000)), rig.Value(holder));
        Assert.Equal(Base, rig.Value(plain));
        Assert.Equal(1, rig.Stats.ChangedPairCount);
        Assert.Equal(holder, StatMachine.SlotOfPair(rig.Stats.ChangedPairs[0]));
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void Rewind_restores_a_card_modified_stat_and_its_pending_recompute_bit_identically(StoragePolicy policy)
    {
        using var rig = new Rig(policy);
        var slot = rig.Spawn(0);
        var frames = new (int Lane, int Value)?[] { (CardA, 1), null, (CardA, 0), null };
        var digests = new List<ulong>();
        var epochs = new List<uint>();
        for (var frame = 0; frame < frames.Length; frame++)
        {
            Run(rig, slot, frames[frame]);
            epochs.Add(rig.World.Epoch);
            digests.Add(rig.World.Digest());
        }

        Assert.Equal(Base, rig.Value(slot));
        for (var from = epochs.Count - 2; from >= 0; from--)
        {
            Assert.True(rig.World.Rewind(epochs[from]));
            Assert.True(rig.Stats.Rewind(epochs[from]));
            Assert.Equal(digests[from], rig.World.Digest());
            for (var frame = from + 1; frame < frames.Length; frame++)
            {
                Run(rig, slot, frames[frame]);
                Assert.True(digests[frame] == rig.World.Digest(), $"rewind to frame {from}, replayed frame {frame}");
            }
        }
    }

    static void Run(Rig rig, int slot, (int Lane, int Value)? card)
    {
        rig.Stats.Recalculate();
        if (card is { } change)
        {
            rig.Card(slot, change.Lane, change.Value);
        }

        rig.EndFrame();
    }
}
