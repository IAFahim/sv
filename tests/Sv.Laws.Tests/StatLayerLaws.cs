using System.Numerics;
using Sv.Rules;
using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed unsafe class StatLayerLaws
{
    static int Clamp32(BigInteger value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

    static int ContractPass(int start, IEnumerable<ShapeTerm> terms)
    {
        var ordered = terms.OrderBy(term => term.Kind).ThenBy(term => term.Value).ToList();
        var value = Clamp32((BigInteger)start + ordered.Where(term => term.Kind == ShapeKind.Added).Aggregate(BigInteger.Zero, (sum, term) => sum + term.Value));
        var factor = Clamp32(10000 + ordered.Where(term => term.Kind == ShapeKind.Additive).Aggregate(BigInteger.Zero, (sum, term) => sum + term.Value));
        value = Clamp32(BigInteger.Divide((BigInteger)value * factor, 10000));
        foreach (var multi in ordered.Where(term => term.Kind == ShapeKind.Multi))
        {
            value = Clamp32(BigInteger.Divide((BigInteger)value * Clamp32((BigInteger)10000 + multi.Value), 10000));
        }

        foreach (var cap in ordered.Where(term => term.Kind == ShapeKind.Cap))
        {
            value = Math.Min(value, cap.Value);
        }

        return value;
    }

    static int ContractLayeredFold(int start, IEnumerable<ShapeTerm> core, IEnumerable<ShapeTerm> external) =>
        ContractPass(ContractPass(start, core), external);

    static ShapeTerm[] RandomTerms(Gen gen, int maxLength)
    {
        var terms = new ShapeTerm[gen.Below(maxLength)];
        for (var at = 0; at < terms.Length; at++)
        {
            terms[at] = new ShapeTerm((ShapeKind)gen.Below(4), gen.EdgeOrAny());
        }

        return terms;
    }

    static int LayeredFold(int start, ShapeTerm[] core, ShapeTerm[] external)
    {
        var coreCopy = (ShapeTerm[])core.Clone();
        var externalCopy = (ShapeTerm[])external.Clone();
        fixed (ShapeTerm* corePtr = coreCopy)
        fixed (ShapeTerm* externalPtr = externalCopy)
        {
            StatLaw.Canonicalize(corePtr, coreCopy.Length);
            StatLaw.Canonicalize(externalPtr, externalCopy.Length);
            return StatLaw.Fold(start, corePtr, coreCopy.Length, externalPtr, externalCopy.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void Scalar_layered_fold_matches_big_integer_oracle_and_reference_on_edge_values(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 2000; trial++)
        {
            var start = gen.EdgeOrAny();
            var core = RandomTerms(gen, 9);
            var external = RandomTerms(gen, 9);
            var folded = LayeredFold(start, core, external);
            var oracle = ContractLayeredFold(start, core, external);
            var refCore = core.Select(term => new ShapeEntry(term.Kind, term.Value)).ToList();
            var refExternal = external.Select(term => new ShapeEntry(term.Kind, term.Value)).ToList();
            var reference = ReferenceLaw.StatFold(start, refCore, refExternal);
            Assert.True(oracle == folded, $"{gen} trial {trial}: oracle {oracle}, folded {folded}");
            Assert.True(reference == folded, $"{gen} trial {trial}: reference {reference}, folded {folded}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void Layered_fold_does_not_depend_on_term_arrival_order(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 1000; trial++)
        {
            var start = gen.EdgeOrAny();
            var core = RandomTerms(gen, 9);
            var external = RandomTerms(gen, 9);
            var expected = LayeredFold(start, core, external);
            gen.Shuffle(core);
            gen.Shuffle(external);
            Assert.Equal(expected, LayeredFold(start, core, external));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void Simd_layered_fold_matches_scalar_fold_on_all_64_rows(ulong seed)
    {
        var gen = new Gen(seed);
        var starts = new int[Lanes.Rows];
        var outputs = new int[Lanes.Rows];
        for (var trial = 0; trial < 250; trial++)
        {
            for (var row = 0; row < Lanes.Rows; row++)
            {
                starts[row] = gen.EdgeOrAny();
            }

            var core = RandomTerms(gen, 9);
            var external = RandomTerms(gen, 9);
            fixed (int* startPtr = starts)
            fixed (int* outputPtr = outputs)
            fixed (ShapeTerm* corePtr = core)
            fixed (ShapeTerm* externalPtr = external)
            {
                StatLaw.Canonicalize(corePtr, core.Length);
                StatLaw.Canonicalize(externalPtr, external.Length);
                StatLaw.Fold(startPtr, corePtr, core.Length, externalPtr, external.Length, outputPtr);
                for (var row = 0; row < Lanes.Rows; row++)
                {
                    Assert.Equal(StatLaw.Fold(starts[row], corePtr, core.Length, externalPtr, external.Length), outputs[row]);
                }
            }
        }
    }

    [Fact]
    public void StatOps_compare_separates_core_and_external_deterministically()
    {
        Assert.Equal(24, sizeof(StatOp));
        var ops = new[]
        {
            StatOp.Add(0, 1, ShapeKind.Multi, 1000, 1, long.MaxValue, StatLayer.External),
            StatOp.Add(0, 1, ShapeKind.Added, 10, 1, long.MaxValue, StatLayer.Core),
            StatOp.Add(0, 1, ShapeKind.Added, 10, 1, long.MaxValue, StatLayer.External),
            StatOp.Add(0, 1, ShapeKind.Multi, 1000, 1, long.MaxValue, StatLayer.Core),
        };
        var a = (StatOp[])ops.Clone();
        var b = (StatOp[])ops.Clone();
        var scratch = new StatOp[ops.Length];
        new Gen(13ul).Shuffle(b);
        StatOps.Canonicalize(a, scratch);
        StatOps.Canonicalize(b, scratch);
        Assert.Equal(StatLayer.Core, a[0].Layer);
        Assert.Equal(StatLayer.Core, a[1].Layer);
        Assert.Equal(StatLayer.External, a[2].Layer);
        Assert.Equal(StatLayer.External, a[3].Layer);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(0, StatOps.Compare(a[i], b[i]));
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void StatMachine_stacks_core_cards_derived_stats_and_external_ops_matching_designer_209(StoragePolicy policy)
    {
        const int strength = 0;
        const int halfStrength = 1;
        const int reach = 2;
        const int coreCard = 3;
        const int extCard = 4;
        var classes = new[] { LaneClass.Stat, LaneClass.Stat, LaneClass.Stat, LaneClass.Intrinsic, LaneClass.Intrinsic };
        var stats = new[]
        {
            new StatDef(strength, 100, 1),
            new StatDef(halfStrength, 0, 1),
            new StatDef(reach, 0, 1),
        };
        var curve = new[] { 1, 2, 4, 8 };
        var sources = new[]
        {
            StatSource.Linear(halfStrength, strength, 5000),
            StatSource.Curve(reach, strength, 0, curve.Length, 100, 100),
        };
        var cards = new[]
        {
            new CardTerm(strength, coreCard, ShapeKind.Added, 20, false, StatLayer.Core),
            new CardTerm(strength, coreCard, ShapeKind.Additive, 5000, false, StatLayer.Core),
            new CardTerm(strength, extCard, ShapeKind.Added, 10, false, StatLayer.External),
        };

        using var world = World.Create(classes, 1, 64, policy, Journal.DefaultRing, 1);
        Assert.Throws<ArgumentException>(() =>
        {
            using var bad = new StatMachine(world, stats, sources, curve, new[] { new CardTerm(strength, coreCard, ShapeKind.Added, 1, false, (StatLayer)2) }, 0);
        });

        using var machine = new StatMachine(world, stats, sources, curve, cards, Journal.DefaultRing);
        var template = new int[classes.Length];
        machine.FoldTemplate(0, template);
        var templateId = world.AddTemplate(template.Select(value => (uint)value).ToArray());
        var slot = world.Acquire(templateId);
        machine.Spawned(slot);

        Assert.Equal(100, world.Read(slot, strength));
        Assert.Equal(50, world.Read(slot, halfStrength));
        Assert.Equal(1, world.Read(slot, reach));

        Assert.True(world.Write(slot, coreCard, 1));
        machine.CardRowsChanged(Slot.Chunk(slot), machine.CardOf(coreCard), Slot.Bit(slot));
        machine.Step(0, new[] { StatOp.Add(slot, strength, ShapeKind.Multi, 1000, 1, long.MaxValue, StatLayer.External) });
        Assert.Equal(198, world.Read(slot, strength));
        Assert.Equal(99, world.Read(slot, halfStrength));
        Assert.Equal(1, world.Read(slot, reach));

        Assert.True(world.Write(slot, extCard, 1));
        machine.CardRowsChanged(Slot.Chunk(slot), machine.CardOf(extCard), Slot.Bit(slot));
        machine.Recalculate();
        Assert.Equal(209, world.Read(slot, strength));
        Assert.Equal(104, world.Read(slot, halfStrength));
        Assert.Equal(2, world.Read(slot, reach));

        var beforeRejected = machine.Rejected;
        var beforeDigest = machine.Digest;
        var badHandle = machine.Add(slot, strength, ShapeKind.Added, 50, 99, long.MaxValue, (StatLayer)2);
        Assert.Equal(0L, badHandle.Id);
        Assert.Equal(beforeRejected + 1, machine.Rejected);
        Assert.Equal(beforeDigest, machine.Digest);
    }

    sealed record MachineSnapshot(uint Epoch, ulong Lanes, int Modifiers, int Expiries, ulong ModifierDigest, long NextId, int BaseVal, int DerivedVal);

    [Theory]
    [InlineData(StoragePolicy.Shared, 11ul)]
    [InlineData(StoragePolicy.Dense, 29ul)]
    public void StatMachine_rewind_restores_layered_modifiers_and_reproduces_digests_and_values(StoragePolicy policy, ulong seed)
    {
        const int baseStat = 0;
        const int derivedStat = 1;
        var classes = new[] { LaneClass.Stat, LaneClass.Stat };
        var stats = new[] { new StatDef(baseStat, 100, 1), new StatDef(derivedStat, 0, 1) };
        var sources = new[] { StatSource.Linear(derivedStat, baseStat, 5000) };
        using var world = World.Create(classes, 1, 128, policy, Journal.DefaultRing, 1);
        using var machine = new StatMachine(world, stats, sources, ReadOnlySpan<int>.Empty, ReadOnlySpan<CardTerm>.Empty, Journal.DefaultRing);
        var template = new int[classes.Length];
        machine.FoldTemplate(0, template);
        var templateId = world.AddTemplate(template.Select(value => (uint)value).ToArray());
        var slots = new[] { world.Acquire(templateId), world.Acquire(templateId), world.Acquire(templateId) };
        foreach (var s in slots)
        {
            machine.Spawned(s);
        }

        const int frames = 20;
        var gen = new Gen(seed);
        var plan = new StatOp[frames][];
        for (var frame = 0; frame < frames; frame++)
        {
            var ops = new List<StatOp>();
            var count = gen.Between(1, 4);
            for (var i = 0; i < count; i++)
            {
                var slot = slots[gen.Below(slots.Length)];
                var source = gen.Below(4);
                if (gen.Chance(25))
                {
                    ops.Add(StatOp.RemoveSource(slot, source));
                    continue;
                }

                var shape = (ShapeKind)gen.Below(4);
                var val = shape is ShapeKind.Added or ShapeKind.Cap ? gen.Between(-20, 50) : gen.Between(-2000, 5000);
                var expiry = gen.Chance(35) ? frame + 1 + gen.Below(4) : long.MaxValue;
                var layer = gen.Chance(50) ? StatLayer.External : StatLayer.Core;
                ops.Add(StatOp.Add(slot, gen.Chance(75) ? baseStat : derivedStat, shape, val, source, expiry, layer));
            }

            plan[frame] = ops.ToArray();
        }

        var straight = new List<MachineSnapshot>();
        for (var frame = 0; frame < frames; frame++)
        {
            machine.Step(frame, plan[frame]);
            world.EndFrame();
            machine.MarkEpoch(world.Epoch);
            straight.Add(new MachineSnapshot(world.Epoch, world.Digest(), machine.ModifierCount, machine.PendingExpiries, machine.Digest, machine.NextId, world.Read(slots[0], baseStat), world.Read(slots[0], derivedStat)));
        }

        for (var from = frames - 2; from >= 0; from -= 3)
        {
            Assert.True(world.Rewind(straight[from].Epoch));
            Assert.True(machine.Rewind(straight[from].Epoch));
            Assert.Equal(straight[from], new MachineSnapshot(world.Epoch, world.Digest(), machine.ModifierCount, machine.PendingExpiries, machine.Digest, machine.NextId, world.Read(slots[0], baseStat), world.Read(slots[0], derivedStat)));
            for (var frame = from + 1; frame < frames; frame++)
            {
                machine.Step(frame, plan[frame]);
                world.EndFrame();
                machine.MarkEpoch(world.Epoch);
                var after = new MachineSnapshot(world.Epoch, world.Digest(), machine.ModifierCount, machine.PendingExpiries, machine.Digest, machine.NextId, world.Read(slots[0], baseStat), world.Read(slots[0], derivedStat));
                Assert.Equal(straight[frame], after);
            }
        }
    }

    [Fact]
    public void Pack_compiler_validates_layered_cards_derived_rows_cycles_and_roundtrips_hash()
    {
        const string noRules = "{\"schema\":1,\"rows\":[]}";
        const string noTemplates = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"t\"}]}";
        const string objectDeriveValues = "{\"schema\":1,\"nextLane\":4,\"rows\":["
            + "{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":100},"
            + "{\"id\":1,\"name\":\"B\",\"class\":\"stat\",\"default\":0,\"derive\":{\"from\":\"A\",\"coefficient\":0.5}},"
            + "{\"id\":2,\"name\":\"C\",\"class\":\"stat\",\"default\":0,\"derive\":{\"from\":\"B\",\"curve\":[1,2,4],\"min\":50,\"step\":25}},"
            + "{\"id\":3,\"name\":\"Ext\",\"class\":\"intrinsic\",\"default\":0}"
            + "]}";
        const string arrayDeriveValues = "{\"schema\":1,\"nextLane\":4,\"rows\":["
            + "{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":100},"
            + "{\"id\":1,\"name\":\"B\",\"class\":\"stat\",\"default\":0,\"derive\":[{\"from\":\"A\",\"coefficient\":0.5}]},"
            + "{\"id\":2,\"name\":\"C\",\"class\":\"stat\",\"default\":0,\"derive\":[{\"from\":\"B\",\"curve\":[1,2,4],\"min\":50,\"step\":25}]},"
            + "{\"id\":3,\"name\":\"Ext\",\"class\":\"intrinsic\",\"default\":0}"
            + "]}";
        const string cards = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"ext_card\",\"value\":\"Ext\",\"modifiers\":[{\"stat\":\"A\",\"added\":20,\"layer\":\"external\"},{\"stat\":\"B\",\"added\":5}]}]}";

        var objPack = PackReader.Read(new PackSource(objectDeriveValues, noRules, cards, noTemplates));
        var arrPack = PackReader.Read(new PackSource(arrayDeriveValues, noRules, cards, noTemplates));
        Assert.NotNull(objPack.Value);
        Assert.NotNull(arrPack.Value);
        Assert.Empty(objPack.Diagnostics);
        Assert.Equal(PackHash.Of(objPack.Value!), PackHash.Of(arrPack.Value!));
        var roundtrip = PackReader.Read(PackWriter.Write(objPack.Value!));
        Assert.NotNull(roundtrip.Value);
        Assert.Equal(PackHash.Of(objPack.Value!), PackHash.Of(roundtrip.Value!));

        var plan = PackCompiler.Compile(new PackSource(objectDeriveValues, noRules, cards, noTemplates));
        Assert.NotNull(plan.Value);
        Assert.Equal(new[] { 0, 1, 2 }, plan.Value!.StatOrder);

        var cycle = PackCompiler.Compile(new PackSource(
            "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1,\"derive\":{\"from\":\"B\",\"coefficient\":1.0}},{\"id\":1,\"name\":\"B\",\"class\":\"stat\",\"default\":1,\"derive\":{\"from\":\"A\",\"coefficient\":1.0}}]}",
            noRules, "{\"schema\":1,\"rows\":[]}", noTemplates));
        Assert.Null(cycle.Value);
        Assert.Contains(cycle.Diagnostics, d => d.Code == DiagnosticCode.CyclicStat);

        var selfCycle = PackCompiler.Compile(new PackSource(
            "{\"schema\":1,\"nextLane\":1,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1,\"derive\":{\"from\":\"A\",\"coefficient\":1.0}}]}",
            noRules, "{\"schema\":1,\"rows\":[]}", noTemplates));
        Assert.Null(selfCycle.Value);
        Assert.Contains(selfCycle.Diagnostics, d => d.Code == DiagnosticCode.CyclicStat);

        var unknownFrom = PackReader.Read(new PackSource(
            "{\"schema\":1,\"nextLane\":1,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1,\"derive\":{\"from\":\"Missing\",\"coefficient\":1.0}}]}",
            noRules, "{\"schema\":1,\"rows\":[]}", noTemplates));
        Assert.Contains(unknownFrom.Diagnostics, d => d.Code == DiagnosticCode.UnknownName);

        var intrinsicDerive = PackReader.Read(new PackSource(
            "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1},{\"id\":1,\"name\":\"I\",\"class\":\"intrinsic\",\"default\":0,\"derive\":{\"from\":\"A\",\"coefficient\":1.0}}]}",
            noRules, "{\"schema\":1,\"rows\":[]}", noTemplates));
        Assert.Contains(intrinsicDerive.Diagnostics, d => d.Code == DiagnosticCode.BadValue);

        var badStep = PackReader.Read(new PackSource(
            "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1},{\"id\":1,\"name\":\"B\",\"class\":\"stat\",\"default\":0,\"derive\":{\"from\":\"A\",\"curve\":[1,2],\"min\":0,\"step\":0}}]}",
            noRules, "{\"schema\":1,\"rows\":[]}", noTemplates));
        Assert.Contains(badStep.Diagnostics, d => d.Code == DiagnosticCode.BadValue);

        var badLayer = PackReader.Read(new PackSource(
            "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1},{\"id\":1,\"name\":\"I\",\"class\":\"intrinsic\",\"default\":0}]}",
            noRules, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"c\",\"value\":\"I\",\"modifiers\":[{\"stat\":\"A\",\"added\":1,\"layer\":\"bogus\"}]}]}", noTemplates));
        Assert.Contains(badLayer.Diagnostics, d => d.Code == DiagnosticCode.BadValue);

        var numLayer = PackReader.Read(new PackSource(
            "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1},{\"id\":1,\"name\":\"I\",\"class\":\"intrinsic\",\"default\":0}]}",
            noRules, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"c\",\"value\":\"I\",\"modifiers\":[{\"stat\":\"A\",\"added\":1,\"layer\":1}]}]}", noTemplates));
        Assert.Contains(numLayer.Diagnostics, d => d.Code == DiagnosticCode.WrongType);
    }
}
