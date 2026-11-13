using Sv.Rules;
using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed class RollLaws
{
    static ulong AnyKey(Gen gen) => gen.Next();

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void CoreHashEqualsTheReferenceHashOnEveryInput(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 2000; i++)
        {
            var packSeed = gen.Next();
            var frame = (long)gen.Next();
            var key = AnyKey(gen);
            var amount = gen.EdgeOrAny();
            var rule = gen.EdgeOrAny();
            var ordinal = gen.Below(4);
            Assert.True(Roll.Hash(packSeed, frame, key, amount, rule, ordinal) == ReferenceLaw.RollHash(packSeed, frame, key, amount, rule, ordinal), gen.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void CorePassEqualsTheReferencePassForEveryShare(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 2000; i++)
        {
            var key = AnyKey(gen);
            var frame = (long)gen.Below(1000);
            var amount = gen.Between(-100, 100000);
            var rule = gen.Between(0, 9999);
            var share = gen.Between(0, Roll.Always);
            Assert.True(Roll.Passes(seed, frame, key, amount, rule, 0, share) == ReferenceLaw.Rolls(seed, frame, key, amount, rule, 0, share), gen.ToString());
        }
    }

    [Fact]
    public void TheHashMatchesVectorsComputedByHandFromTheContractFormula()
    {
        var key = 64ul << 42;
        Assert.Equal(3974, Roll.BasisPointOf(Roll.Hash(0ul, 0, key, 10, 2033, 0)));
        Assert.Equal(1364, Roll.BasisPointOf(Roll.Hash(0ul, 1, key, 10, 2033, 0)));
        Assert.Equal(2281, Roll.BasisPointOf(Roll.Hash(0ul, 2, key, 10, 2033, 0)));
        Assert.Equal(7508, Roll.BasisPointOf(Roll.Hash(0ul, 5, key, 10, 2033, 0)));
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void ZeroNeverPassesAndTenThousandAlwaysPasses(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 5000; i++)
        {
            var key = AnyKey(gen);
            var frame = (long)gen.Next();
            Assert.False(Roll.Passes(seed, frame, key, gen.AnyInt(), gen.AnyInt(), 0, 0));
            Assert.True(Roll.Passes(seed, frame, key, gen.AnyInt(), gen.AnyInt(), 0, Roll.Always));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void PassingIsMonotoneInTheShare(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 2000; i++)
        {
            var key = AnyKey(gen);
            var low = gen.Between(0, Roll.Always);
            var high = gen.Between(low, Roll.Always);
            var frame = (long)gen.Below(100);
            if (Roll.Passes(seed, frame, key, 7, 11, 0, low))
            {
                Assert.True(Roll.Passes(seed, frame, key, 7, 11, 0, high), gen.ToString());
            }
        }
    }

    [Theory]
    [InlineData(2500)]
    [InlineData(5000)]
    [InlineData(100)]
    public void TheShareOfPassingRecordsIsTheShareAsked(int share)
    {
        var passed = 0;
        const int Records = 200000;
        for (var i = 0; i < Records; i++)
        {
            var key = ((ulong)(i & 1023) << 42) | ((ulong)(i >> 10) << 20);
            passed += Roll.Passes(0ul, i % 7, key, 1 + (i % 50), 2001, 0, share) ? 1 : 0;
        }

        var expected = (double)Records * share / Roll.Always;
        var tolerance = 5.0 * Math.Sqrt(expected);
        Assert.InRange(passed, expected - tolerance, expected + tolerance);
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void EveryInputMovesTheHash(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 1000; i++)
        {
            var key = AnyKey(gen);
            var frame = (long)gen.Below(1 << 20);
            var amount = gen.AnyInt();
            var rule = gen.AnyInt();
            var hash = Roll.Hash(seed, frame, key, amount, rule, 0);
            Assert.NotEqual(hash, Roll.Hash(seed ^ 1ul, frame, key, amount, rule, 0));
            Assert.NotEqual(hash, Roll.Hash(seed, frame + 1, key, amount, rule, 0));
            Assert.NotEqual(hash, Roll.Hash(seed, frame, key ^ (1ul << gen.Below(64)), amount, rule, 0));
            Assert.NotEqual(hash, Roll.Hash(seed, frame, key, amount ^ 1, rule, 0));
            Assert.NotEqual(hash, Roll.Hash(seed, frame, key, amount, rule ^ 1, 0));
            Assert.NotEqual(hash, Roll.Hash(seed, frame, key, amount, rule, 1));
        }
    }
}

public sealed class AssignModeLaws
{
    static long Fold(AssignMode mode, long start, IList<long> values)
    {
        var assigned = start;
        for (var i = 0; i < values.Count; i++)
        {
            assigned = AssignModes.Apply(mode, assigned, values[i]);
        }

        return assigned;
    }

    static List<long> Values(Gen gen)
    {
        var count = gen.Between(1, 12);
        var values = new List<long>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(gen.Chance(10) ? (gen.Chance(50) ? long.MaxValue : long.MinValue) : gen.EdgeOrAny());
        }

        return values;
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void MaxAndMinDependOnlyOnTheMultisetAndIncludeTheStartValue(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 500; i++)
        {
            var start = (long)gen.EdgeOrAny();
            var values = Values(gen);
            var shuffled = new List<long>(values);
            gen.Shuffle(shuffled);
            Assert.Equal(Fold(AssignMode.Max, start, values), Fold(AssignMode.Max, start, shuffled));
            Assert.Equal(Fold(AssignMode.Min, start, values), Fold(AssignMode.Min, start, shuffled));
            Assert.Equal(Math.Max(start, values.Max()), Fold(AssignMode.Max, start, values));
            Assert.Equal(Math.Min(start, values.Min()), Fold(AssignMode.Min, start, values));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void SetAndSourceKeepTheLastValueInOrder(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 500; i++)
        {
            var start = (long)gen.EdgeOrAny();
            var values = Values(gen);
            Assert.Equal(values[values.Count - 1], Fold(AssignMode.Set, start, values));
            Assert.Equal(values[values.Count - 1], Fold(AssignMode.Source, start, values));
            Assert.Equal(start, Fold(AssignMode.Set, start, new long[0]));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void ApplyingAValueTwiceEqualsApplyingItOnceSoMergedHitsAssignAlike(ulong seed)
    {
        var gen = new Gen(seed);
        foreach (var mode in new[] { AssignMode.Set, AssignMode.Max, AssignMode.Min, AssignMode.Source })
        {
            for (var i = 0; i < 300; i++)
            {
                var start = (long)gen.EdgeOrAny();
                var value = (long)gen.EdgeOrAny();
                var once = AssignModes.Apply(mode, start, value);
                Assert.Equal(once, AssignModes.Apply(mode, once, value));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void AddModeNeverAssigns(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 500; i++)
        {
            var start = (long)gen.EdgeOrAny();
            Assert.Equal(start, AssignModes.Apply(AssignMode.Add, start, gen.EdgeOrAny()));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void TheOneDeltaOfAnAssignedIntrinsicFoldsToTheAssignedValuePlusTheAddTermsClampedOnce(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 2000; i++)
        {
            var before = gen.Between(-100000, 100000);
            var assigned = (long)gen.Between(-100000, 100000);
            var adds = (long)gen.Between(-100000, 100000);
            var floor = gen.Between(-200000, 0);
            var ceiling = gen.Between(0, 200000);
            var delta = ReferenceLaw.Add(adds, ReferenceLaw.Add(assigned, ReferenceLaw.Negate(before)));
            var after = ReferenceLaw.Step(before, ReferenceLaw.Saturate(delta), floor, ceiling);
            var expected = Math.Clamp(assigned + adds, floor, ceiling);
            Assert.True(expected == after, gen.ToString());
        }
    }
}

public sealed class PerCountLaws
{
    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void ThePerCountTermIsTheSaturatedProductOnBothHosts(ulong seed)
    {
        var gen = new Gen(seed);
        for (var i = 0; i < 2000; i++)
        {
            var value = gen.EdgeOrAny();
            var count = gen.Chance(30) ? Gen.EdgeInts[gen.Below(Gen.EdgeInts.Length)] : gen.Between(1, 1000);
            var core = CardTerm.TermValue(value, count, true);
            Assert.Equal(ReferenceLaw.CardTerm(value, count, true), core);
            Assert.Equal((int)Math.Clamp((long)value * count, int.MinValue, int.MaxValue), core);
            Assert.Equal(value, CardTerm.TermValue(value, count, false));
        }
    }
}

public sealed class BrickPackLaws
{
    const string Values = """
        {"schema":1,"nextLane":8,"seed":12345,"rows":[
        {"id":0,"name":"Max","class":"stat","default":5},
        {"id":1,"name":"Bonus","class":"stat","default":0,"scale":100},
        {"id":2,"name":"Health","class":"intrinsic","default":100},
        {"id":3,"name":"Charges","class":"intrinsic","default":2},
        {"id":4,"name":"Owner","class":"intrinsic","default":-1,"role":"owner"},
        {"id":5,"name":"Stacks","class":"intrinsic","default":0},
        {"id":6,"name":"Done","class":"intrinsic","default":0},
        {"id":0,"name":"Hit","class":"channel"},
        {"id":1,"name":"Refill","class":"channel","into":"Charges","mode":"set"},
        {"id":2,"name":"Claim","class":"channel","into":"Owner","mode":"source"},
        {"id":3,"name":"Gain","class":"channel","into":"Stacks"},
        {"id":4,"name":"Use","class":"channel"},
        {"id":0,"name":"None","class":"kind"}
        ]}
        """;

    const string Cards = """
        {"schema":1,"rows":[{"id":1,"name":"stack","value":"Stacks","modifiers":[{"stat":"Bonus","added":0.1,"perCount":true}]}]}
        """;

    const string Rules = """
        {"schema":1,"rows":[
        {"id":1,"name":"health","layer":"flow","then":[{"subtract":["Health","Hit"]},{"max":["Done","Use"]}]},
        {"id":2,"name":"bonus","layer":"gate","priority":1,"when":{"and":[{"eq":["record.channel","Hit"]},{"roll":2500}]},"then":[{"additive":{"cell":"source.Bonus"}}]},
        {"id":3,"name":"use","layer":"gate","priority":2,"when":{"eq":["record.channel","Use"]},"then":[{"spend":"Charges"}]},
        {"id":4,"name":"echo","layer":"emit","when":{"eq":["record.channel","Hit"]},"then":[{"emit":{"to":"Source","channel":"Hit","amount":{"cell":"self.Max"},"scale":"Charges"}}]}
        ]}
        """;

    static Checked<ContentPack> Read(string values, string rules, string cards = "") => PackReader.Read(new PackSource(values, rules, cards, string.Empty));

    static Checked<PackPlan> Compile(string values, string rules, string cards = "") => PackCompiler.Compile(new PackSource(values, rules, cards, string.Empty));

    [Fact]
    public void APackWithEveryBrickRoundTripsThroughTheCanonicalWriterWithAStableHash()
    {
        var first = Compile(Values, Rules, Cards);
        Assert.True(first.Value != null, string.Join("\n", first.Diagnostics));
        var canonical = PackWriter.Write(first.Value!.Pack);
        var again = PackCompiler.Compile(canonical);
        Assert.True(again.Value != null, string.Join("\n", again.Diagnostics));
        Assert.Equal(first.Value.Hash, again.Value!.Hash);
        Assert.Equal(12345ul, again.Value.Schema.Seed);
        Assert.Contains("\"mode\":\"set\"", canonical.Values);
        Assert.Contains("\"perCount\":true", canonical.Cards);
        Assert.Contains("{\"roll\":2500}", canonical.Rules);
        Assert.Contains("{\"spend\":\"Charges\"}", canonical.Rules);
        Assert.Contains("{\"cell\":\"source.Bonus\"}", canonical.Rules);
    }

    [Fact]
    public void APackWithoutBricksWritesNoneOfTheNewKeys()
    {
        var plain = Compile("{\"schema\":1,\"nextLane\":1,\"rows\":[{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":1},{\"id\":0,\"name\":\"Hit\",\"class\":\"channel\",\"into\":\"Health\"}]}", "{\"schema\":1,\"rows\":[]}");
        Assert.NotNull(plain.Value);
        var canonical = PackWriter.Write(plain.Value!.Pack);
        foreach (var key in new[] { "\"mode\"", "\"seed\"", "\"perCount\"", "\"spend\"", "\"roll\"", "\"cell\"" })
        {
            Assert.DoesNotContain(key, canonical.Values + canonical.Rules + canonical.Cards);
        }
    }

    static void Rejected(DiagnosticCode code, string values, string rules)
    {
        var compiled = Compile(values, rules);
        Assert.Null(compiled.Value);
        Assert.Contains(compiled.Diagnostics, d => d.Severity == Severity.Error && d.Code == code);
    }

    [Fact]
    public void AModeWithoutIntoIsRejected() =>
        Rejected(DiagnosticCode.BadValue, Values.Replace("{\"id\":4,\"name\":\"Use\",\"class\":\"channel\"}", "{\"id\":4,\"name\":\"Use\",\"class\":\"channel\",\"mode\":\"set\"}"), "{\"schema\":1,\"rows\":[]}");

    [Fact]
    public void AnUnknownModeIsRejected() =>
        Rejected(DiagnosticCode.BadValue, Values.Replace("\"mode\":\"set\"", "\"mode\":\"double\""), "{\"schema\":1,\"rows\":[]}");

    [Fact]
    public void TwoAssignModesOnOneIntrinsicAreRejected() =>
        Rejected(DiagnosticCode.AssignConflict, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"f\",\"layer\":\"flow\",\"then\":[{\"max\":[\"Charges\",\"Use\"]}]}]}");

    [Fact]
    public void AFlowCannotAssignASource() =>
        Rejected(DiagnosticCode.BadValue, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"f\",\"layer\":\"flow\",\"then\":[{\"source\":[\"Done\",\"Use\"]}]}]}");

    [Fact]
    public void ARollOutsideBasisPointsIsRejected() =>
        Rejected(DiagnosticCode.BadValue, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"g\",\"layer\":\"gate\",\"priority\":1,\"when\":{\"roll\":10001},\"then\":[{\"deny\":{}}]}]}");

    [Fact]
    public void ARollInAFlowIsRejectedBecauseAFlowHasNoRecord() =>
        Rejected(DiagnosticCode.ScopeViolation, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"f\",\"layer\":\"flow\",\"when\":{\"roll\":5000},\"then\":[{\"add\":[\"Done\",\"Use\"]}]}]}");

    [Fact]
    public void ARollInAnEmitPerSourceIsRejected() =>
        Rejected(DiagnosticCode.ScopeViolation, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"e\",\"layer\":\"emit\",\"when\":{\"roll\":5000},\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"Gain\",\"amount\":1,\"per\":\"Source\"}}]}]}");

    [Fact]
    public void AGateThatAbsorbsAndSpendsIsRejected() =>
        Rejected(DiagnosticCode.BadValue, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"g\",\"layer\":\"gate\",\"priority\":1,\"then\":[{\"spend\":\"Charges\"},{\"absorb\":{\"budget\":\"Charges\"}}]}]}");

    [Fact]
    public void ASpendOnNoIntrinsicIsRejected() =>
        Rejected(DiagnosticCode.UnknownName, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"g\",\"layer\":\"gate\",\"priority\":1,\"then\":[{\"spend\":\"Nothing\"}]}]}");

    [Fact]
    public void AnEdgeCannotTakeItsAmountFromACell() =>
        Rejected(DiagnosticCode.BadEmit, Values.Replace("{\"id\":0,\"name\":\"None\",\"class\":\"kind\"}", "{\"id\":0,\"name\":\"None\",\"class\":\"kind\"},{\"lane\":7,\"word\":0,\"name\":\"Low\",\"class\":\"event\"}"),
            "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"low\",\"layer\":\"event\",\"event\":\"Low\",\"enter\":{\"lt\":[\"Health\",10]}},{\"id\":2,\"name\":\"x\",\"layer\":\"edge\",\"event\":\"Low\",\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"Gain\",\"amount\":{\"cell\":\"self.Max\"}}}]}]}");

    [Fact]
    public void AnEmitPerFrameCannotReadItsAmountFromTheSource() =>
        Rejected(DiagnosticCode.ScopeViolation, Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"e\",\"layer\":\"emit\",\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"Gain\",\"amount\":{\"cell\":\"source.Health\"},\"per\":\"Frame\"}}]}]}");

    [Fact]
    public void AnAmountCellThatNamesAnEventOrNothingIsRejected()
    {
        var read = Read(Values, "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"e\",\"layer\":\"emit\",\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"Gain\",\"amount\":{\"cell\":\"self.Nothing\"}}}]}]}");
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, d => d.Code == DiagnosticCode.UnknownName);
    }

    [Fact]
    public void AShapeCellOnTheSourceMakesItsGateRecordDependent()
    {
        var compiled = Compile(Values, Rules, Cards);
        var plan = compiled.Value!;
        var bonus = Array.FindIndex(plan.GateOrder, gate => gate.Id == 2);
        var use = Array.FindIndex(plan.GateOrder, gate => gate.Id == 3);
        Assert.False(plan.GateIsChannelState[bonus]);
        Assert.True(plan.GateIsChannelState[use]);
    }
}
