namespace Sv.Laws.Tests;

public sealed unsafe class StatMachineLaws
{
    const int Reach = 0;
    const int Power = 1;
    const int Strength = 2;
    const int StrengthBase = 10;
    const int PowerBase = 50;
    const int ReachBase = 2;
    const int PowerScale = 10;
    const int PowerPerStrength = 15000;
    const int CurveMin = 100;
    const int CurveStep = 40;

    static readonly LaneClass[] Classes = { LaneClass.Stat, LaneClass.Stat, LaneClass.Stat };

    static readonly int[] Curve = { 0, 1, 3, 6, 10 };

    static readonly StatDef[] Stats =
    {
        new StatDef(Reach, ReachBase, 1),
        new StatDef(Power, PowerBase, PowerScale),
        new StatDef(Strength, StrengthBase, 1),
    };

    static readonly StatSource[] Sources =
    {
        StatSource.Curve(Reach, Power, 0, 5, CurveMin, CurveStep),
        StatSource.Linear(Power, Strength, PowerPerStrength),
    };

    sealed class Rig : IDisposable
    {
        public readonly World World;
        public readonly StatMachine Stats;

        public Rig(StoragePolicy policy)
        {
            World = World.Create(Classes, 1, 256, policy, Journal.DefaultRing, 1);
            Stats = new StatMachine(World, StatMachineLaws.Stats, Sources, Curve, ReadOnlySpan<CardTerm>.Empty, Journal.DefaultRing);
            var values = new int[Classes.Length];
            Stats.FoldTemplate(0, values);
            World.AddTemplate(values.Select(value => (uint)value).ToArray());
        }

        public int Spawn() => World.Acquire(0);

        public int Read(int slot, int lane) => World.Read(slot, lane);

        public void EndFrame()
        {
            World.EndFrame();
            Stats.MarkEpoch(World.Epoch);
        }

        public void Dispose()
        {
            Stats.Dispose();
            World.Dispose();
        }
    }

    static int Fold(int start, params (ShapeKind Kind, int Value)[] terms)
    {
        var buffer = stackalloc ShapeTerm[Math.Max(1, terms.Length)];
        for (var at = 0; at < terms.Length; at++)
        {
            buffer[at] = new ShapeTerm(terms[at].Kind, terms[at].Value);
        }

        StatLaw.Canonicalize(buffer, terms.Length);
        return StatLaw.Fold(start, buffer, terms.Length);
    }

    static int ExpectedPower(int strength) => Fold(PowerBase, (ShapeKind.Added, StatLaw.Derived(strength, PowerPerStrength, 1, PowerScale)));

    static int ExpectedReach(int power) => Fold(ReachBase, (ShapeKind.Added, Curve[StatLaw.CurveBucket(power, CurveMin, CurveStep, Curve.Length)]));

    static (ShapeKind Kind, int Value) RandomTerm(Gen gen)
    {
        var kind = (ShapeKind)gen.Below(4);
        var value = kind switch
        {
            ShapeKind.Added => gen.Between(-20, 60),
            ShapeKind.Additive => gen.Between(-5000, 20000),
            ShapeKind.Multi => gen.Between(-5000, 20000),
            _ => gen.Between(0, 80),
        };
        return (kind, value);
    }

    [Fact]
    public void A_template_folds_its_derived_stats_in_dependency_order_at_creation()
    {
        using var rig = new Rig(StoragePolicy.Shared);
        var slot = rig.Spawn();
        Assert.Equal(StrengthBase, rig.Read(slot, Strength));
        Assert.Equal(ExpectedPower(StrengthBase), rig.Read(slot, Power));
        Assert.Equal(ExpectedReach(ExpectedPower(StrengthBase)), rig.Read(slot, Reach));
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void A_linear_derived_stat_equals_its_base_plus_the_rescaled_source_after_every_source_change(ulong seed)
    {
        var gen = new Gen(seed);
        using var rig = new Rig(StoragePolicy.Shared);
        var slot = rig.Spawn();
        var terms = new List<(ShapeKind, int)>();
        for (var step = 0; step < 40; step++)
        {
            var term = RandomTerm(gen);
            terms.Add(term);
            rig.Stats.Add(slot, Strength, term.Kind, term.Value, step);
            rig.Stats.Recalculate();
            var strength = Fold(StrengthBase, terms.ToArray());
            Assert.Equal(strength, rig.Read(slot, Strength));
            Assert.True(ExpectedPower(strength) == rig.Read(slot, Power), $"{gen} step {step}: strength {strength}, power {rig.Read(slot, Power)}, expected {ExpectedPower(strength)}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void A_curve_derived_stat_follows_its_source_through_a_chain_in_one_recalculate(ulong seed)
    {
        var gen = new Gen(seed);
        using var rig = new Rig(StoragePolicy.Dense);
        var slot = rig.Spawn();
        for (var step = 0; step < 40; step++)
        {
            rig.Stats.RemoveSource(slot, 1);
            rig.Stats.Add(slot, Strength, ShapeKind.Added, gen.Between(-30, 80), 1);
            rig.Stats.Recalculate();
            var power = rig.Read(slot, Power);
            Assert.Equal(ExpectedPower(rig.Read(slot, Strength)), power);
            Assert.True(ExpectedReach(power) == rig.Read(slot, Reach), $"{gen} step {step}: power {power}, reach {rig.Read(slot, Reach)}, expected {ExpectedReach(power)}");
        }
    }

    [Fact]
    public void A_modifier_on_a_derived_stat_stacks_on_top_of_the_derived_term()
    {
        using var rig = new Rig(StoragePolicy.Shared);
        var slot = rig.Spawn();
        rig.Stats.Add(slot, Power, ShapeKind.Multi, 5000, 3);
        rig.Stats.Recalculate();
        var derived = StatLaw.Derived(StrengthBase, PowerPerStrength, 1, PowerScale);
        Assert.Equal(Fold(PowerBase, (ShapeKind.Added, derived), (ShapeKind.Multi, 5000)), rig.Read(slot, Power));
        Assert.Equal(ExpectedReach(rig.Read(slot, Power)), rig.Read(slot, Reach));
    }

    [Fact]
    public void Derived_stats_that_form_a_cycle_are_refused()
    {
        using var world = World.Create(Classes, 1, 256, StoragePolicy.Shared, Journal.DefaultRing, 1);
        var cycle = new[] { StatSource.Linear(Power, Strength, 10000), StatSource.Linear(Strength, Power, 10000) };
        Assert.Throws<ArgumentException>(() => new StatMachine(world, Stats, cycle, ReadOnlySpan<int>.Empty, ReadOnlySpan<CardTerm>.Empty, 4));
    }

    [Fact]
    public void An_expiring_modifier_leaves_at_its_tick_and_the_stat_returns()
    {
        using var rig = new Rig(StoragePolicy.Shared);
        var slot = rig.Spawn();
        rig.Stats.Step(0, new[] { StatOp.Add(slot, Strength, ShapeKind.Added, 20, 7, 3) });
        Assert.Equal(StrengthBase + 20, rig.Read(slot, Strength));
        Assert.Equal(1, rig.Stats.PendingExpiries);
        rig.Stats.Step(2, Span<StatOp>.Empty);
        Assert.Equal(StrengthBase + 20, rig.Read(slot, Strength));
        rig.Stats.Step(3, Span<StatOp>.Empty);
        Assert.Equal(StrengthBase, rig.Read(slot, Strength));
        Assert.Equal(ExpectedPower(StrengthBase), rig.Read(slot, Power));
        Assert.Equal(0, rig.Stats.ModifierCount);
        Assert.Equal(0, rig.Stats.PendingExpiries);
    }

    static StatOp[][] RandomOps(Gen gen, int[] slots, int frames)
    {
        var plan = new StatOp[frames][];
        for (var frame = 0; frame < frames; frame++)
        {
            var ops = new List<StatOp>();
            var count = gen.Below(5);
            for (var at = 0; at < count; at++)
            {
                var slot = slots[gen.Below(slots.Length)];
                var source = gen.Below(4);
                if (gen.Chance(25))
                {
                    ops.Add(StatOp.RemoveSource(slot, source));
                    continue;
                }

                var term = RandomTerm(gen);
                var expiry = gen.Chance(40) ? frame + 1 + gen.Below(6) : long.MaxValue;
                ops.Add(StatOp.Add(slot, gen.Chance(70) ? Strength : Power, term.Kind, term.Value, source, expiry));
            }

            plan[frame] = ops.ToArray();
        }

        return plan;
    }

    sealed record Snapshot(uint Epoch, ulong Lanes, int Modifiers, int Expiries, ulong ModifierDigest, long NextId);

    static Snapshot Take(Rig rig) => new(rig.World.Epoch, rig.World.Digest(), rig.Stats.ModifierCount, rig.Stats.PendingExpiries, rig.Stats.Digest, rig.Stats.NextId);

    [Theory]
    [InlineData(StoragePolicy.Shared, 5ul)]
    [InlineData(StoragePolicy.Dense, 6ul)]
    [InlineData(StoragePolicy.Shared, 4242ul)]
    public void Rewind_undoes_stat_ops_and_expiries_to_the_exact_modifier_set_and_replay_reproduces_every_frame(StoragePolicy policy, ulong seed)
    {
        const int frames = 24;
        var gen = new Gen(seed);
        using var rig = new Rig(policy);
        var slots = new[] { rig.Spawn(), rig.Spawn(), rig.Spawn() };
        var plan = RandomOps(gen, slots, frames);
        var straight = new List<Snapshot>();
        for (var frame = 0; frame < frames; frame++)
        {
            rig.Stats.Step(frame, plan[frame]);
            rig.EndFrame();
            straight.Add(Take(rig));
        }

        Assert.True(straight.Max(snapshot => snapshot.Modifiers) > 0);
        for (var from = frames - 2; from >= 0; from -= 3)
        {
            Assert.True(rig.World.Rewind(straight[from].Epoch));
            Assert.True(rig.Stats.Rewind(straight[from].Epoch));
            Assert.Equal(straight[from], Take(rig));
            for (var frame = from + 1; frame < frames; frame++)
            {
                rig.Stats.Step(frame, plan[frame]);
                rig.EndFrame();
                Assert.True(straight[frame] == Take(rig), $"{gen} rewind to frame {from}, replayed frame {frame}: {Take(rig)}, straight {straight[frame]}");
            }
        }
    }
}
