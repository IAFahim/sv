using Sv.Rules.Emit;

namespace Sv.Host.Tests;

public sealed class RuleTests
{
    readonly ITestOutputHelper output;

    public RuleTests(ITestOutputHelper output) => this.output = output;

    const string ArmoredTie = """{"id":4101,"name":"armored_halves","layer":"gate","priority":20,"group":1,"when":{"eq":["record.channel","Physical"]},"then":[{"multi":-0.5}]}""";

    [Fact]
    public void Two_shape_gates_of_one_priority_in_one_exclusive_group_that_can_both_match_are_a_compile_error()
    {
        var tied = """{"id":4102,"name":"plain_quarter","layer":"gate","priority":20,"group":1,"when":{"lt":["record.amount",100]},"then":[{"multi":-0.25}]}""";
        var compiled = PackCompiler.Compile(RulePack.WithGates(ArmoredTie + "," + tied));
        Assert.Null(compiled.Value);
        var tie = Assert.Single(compiled.Diagnostics, d => d.Code == DiagnosticCode.ShapeGroupTie);
        Assert.Equal(Severity.Error, tie.Severity);
        Assert.Equal(new[] { 4101, 4102 }, new[] { tie.RuleId, tie.OtherRuleId }.Order());
        output.WriteLine(tie.ToString());
    }

    [Fact]
    public void Shape_gates_of_one_priority_in_one_exclusive_group_with_disjoint_conditions_compile()
    {
        var disjoint = """{"id":4102,"name":"healing_quarter","layer":"gate","priority":20,"group":1,"when":{"eq":["record.channel","Healing"]},"then":[{"multi":-0.25}]}""";
        var compiled = PackCompiler.Compile(RulePack.WithGates(ArmoredTie + "," + disjoint));
        Assert.DoesNotContain(compiled.Diagnostics, d => d.Code == DiagnosticCode.ShapeGroupTie);
        Assert.NotNull(compiled.Value);
    }

    [Fact]
    public void Shape_gates_of_one_priority_in_different_exclusive_groups_compile()
    {
        var other = """{"id":4102,"name":"plain_quarter","layer":"gate","priority":20,"group":2,"when":{"eq":["record.channel","Physical"]},"then":[{"multi":-0.25}]}""";
        var compiled = PackCompiler.Compile(RulePack.WithGates(ArmoredTie + "," + other));
        Assert.DoesNotContain(compiled.Diagnostics, d => d.Code == DiagnosticCode.ShapeGroupTie);
        Assert.NotNull(compiled.Value);
    }

    [Fact]
    public void A_pack_with_64_exclusive_shape_groups_compiles()
    {
        var unit = PackEmitter.Compile(RulePack.WithGates(RulePack.ShapeGroups(64)));
        Assert.Equal(0, unit.Diagnostics.Errors);
    }

    [Fact]
    public void A_pack_with_more_than_64_exclusive_shape_groups_is_a_compile_error()
    {
        var unit = PackEmitter.Compile(RulePack.WithGates(RulePack.ShapeGroups(65)));
        var errors = Enumerable.Range(0, unit.Diagnostics.Count).Select(at => unit.Diagnostics[at]).Where(d => d.Severity == Severity.Error).ToList();
        var limit = Assert.Single(errors);
        Assert.Equal(DiagnosticCode.BadValue, limit.Code);
        Assert.Contains("64", limit.Message);
        Assert.Contains("65", limit.Message);
        output.WriteLine(limit.ToString());
    }

    static string ExclusiveGroupKeepsOnlyItsHighestApplicableGate<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var rig = new Rig<TKernels>(plan, Packs.Options());
        var armored = rig.Spawn("armored");
        var plain = rig.Spawn("player");
        rig.Send(armored, EffectKey.NoSource, "Physical", 40);
        rig.Send(plain, EffectKey.NoSource, "Physical", 40);
        rig.Step();
        Assert.Equal(100 - (40 - 10) / 2, rig.Value(armored, "Health"));
        Assert.Equal(100 - (40 - 10) * 3 / 4, rig.Value(plain, "Health"));
        Assert.Equal(rig.ReferenceValue(armored, "Health"), rig.Value(armored, "Health"));
        Assert.Equal(rig.ReferenceValue(plain, "Health"), rig.Value(plain, "Health"));
        Assert.Equal(rig.ReferenceDigest, rig.Digest);
        return "armored " + rig.Value(armored, "Health") + ", plain " + rig.Value(plain, "Health");
    }

    [Fact]
    public void Within_an_exclusive_group_only_the_highest_priority_applicable_gate_shapes_and_ungrouped_gates_stack()
    {
        output.WriteLine((string)Packs.Call(typeof(RuleTests), nameof(ExclusiveGroupKeepsOnlyItsHighestApplicableGate), RulePack.Kernels, RulePack.Plan)!);
    }

    static string TotalReadsTheFrameTakenSumOfAChannel<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var rig = new Rig<TKernels>(plan, Packs.Options());
        var once = rig.Spawn("player");
        var twice = rig.Spawn("player");
        var first = rig.Spawn("player");
        var second = rig.Spawn("player");
        rig.Send(once, first, "Physical", 30);
        rig.Send(twice, first, "Physical", 30);
        rig.Send(twice, second, "Physical", 30);
        rig.Step();
        var taken = (30 - 10) * 3 / 4;
        Assert.Equal(0, rig.Value(once, "Broken"));
        Assert.Equal(100 - taken, rig.Value(once, "Health"));
        Assert.Equal(2 * taken, rig.Value(twice, "Broken"));
        Assert.Equal(100 - 2 * taken, rig.Value(twice, "Health"));
        Assert.Equal(rig.ReferenceValue(twice, "Broken"), rig.Value(twice, "Broken"));
        Assert.Equal(rig.ReferenceDigest, rig.Digest);
        rig.Step();
        Assert.Equal(2 * taken, rig.Value(twice, "Broken"));
        Assert.Equal(rig.ReferenceDigest, rig.Digest);
        return "one hit: Broken 0; two hits of " + taken + ": Broken " + rig.Value(twice, "Broken");
    }

    [Fact]
    public void Total_is_the_taken_sum_of_a_channel_on_the_target_this_frame()
    {
        output.WriteLine((string)Packs.Call(typeof(RuleTests), nameof(TotalReadsTheFrameTakenSumOfAChannel), RulePack.Kernels, RulePack.Plan)!);
    }

    static string ForwardingChainsDeliverToTheLastOwner<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var rig = new Rig<TKernels>(plan, Packs.Options());
        var player = rig.Spawn("player");
        var hand = rig.Spawn("hand");
        var glove = rig.Spawn("hand");
        rig.Send(hand, EffectKey.NoSource, "Adopt", player + 1);
        rig.Send(glove, EffectKey.NoSource, "Adopt", hand + 1);
        rig.Send(player, EffectKey.NoSource, "Physical", 70);
        rig.Step();
        Assert.Equal(player, rig.Value(hand, "Owner"));
        Assert.Equal(hand, rig.Value(glove, "Owner"));
        Assert.Equal(100 - (70 - 10) * 3 / 4, rig.Value(player, "Health"));
        rig.Send(glove, EffectKey.NoSource, "Healing", 20);
        rig.Send(hand, EffectKey.NoSource, "Healing", 5);
        rig.Send(glove, EffectKey.NoSource, "Physical", 50);
        rig.Step();
        Assert.Equal(100 - (70 - 10) * 3 / 4 + 25, rig.Value(player, "Health"));
        Assert.Equal(100, rig.Value(hand, "Health"));
        Assert.Equal(100 - (50 - 10) * 3 / 4, rig.Value(glove, "Health"));
        Assert.Equal(rig.ReferenceValue(player, "Health"), rig.Value(player, "Health"));
        Assert.Equal(rig.ReferenceDigest, rig.Digest);
        return "player " + rig.Value(player, "Health") + ", hand " + rig.Value(hand, "Health") + ", glove " + rig.Value(glove, "Health");
    }

    [Fact]
    public void Healing_sent_to_a_glove_on_a_hand_lands_on_the_player_that_owns_the_hand()
    {
        output.WriteLine((string)Packs.Call(typeof(RuleTests), nameof(ForwardingChainsDeliverToTheLastOwner), RulePack.Kernels, RulePack.Plan)!);
    }

    static List<ulong> ForwardingCrowd<TKernels>(PackPlan plan, int workers)
        where TKernels : struct, IPackKernels
    {
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options(workers));
        var physical = Packs.Id(plan, ValueClass.Channel, "Physical");
        var healing = Packs.Id(plan, ValueClass.Channel, "Healing");
        var adopt = Packs.Id(plan, ValueClass.Channel, "Adopt");
        var players = new int[4000];
        var hands = new int[4000];
        var gloves = new int[4000];
        for (var at = 0; at < players.Length; at++)
        {
            players[at] = host.Spawn(Packs.Template(plan, at % 2 == 0 ? "player" : "armored"));
            hands[at] = host.Spawn(Packs.Template(plan, "hand"));
            gloves[at] = host.Spawn(Packs.Template(plan, "hand"));
        }

        for (var at = 0; at < players.Length; at++)
        {
            host.Send(hands[at], EffectKey.NoSource, 0, adopt, players[(at * 7) % players.Length] + 1);
            host.Send(gloves[at], EffectKey.NoSource, 0, adopt, hands[(at * 13) % hands.Length] + 1);
        }

        var digests = new List<ulong>();
        for (var frame = 0; frame < 30; frame++)
        {
            for (var at = 0; at < players.Length; at++)
            {
                var phase = at + frame;
                host.Send(players[at], players[(at + 1) % players.Length], 0, physical, phase % 9 + 12);
                host.Send(gloves[at], EffectKey.NoSource, 0, healing, phase % 4 + 1);
                if (phase % 3 == 0)
                {
                    host.Send(hands[at], gloves[at], 0, healing, 3);
                }
            }

            host.Step();
            digests.Add(host.Digest());
        }

        return digests;
    }

    [Fact]
    public void Digests_with_forwarding_chains_are_equal_at_1_2_4_7_and_11_workers()
    {
        var one = (List<ulong>)Packs.Call(typeof(RuleTests), nameof(ForwardingCrowd), RulePack.Kernels, RulePack.Plan, 1)!;
        output.WriteLine("1 worker final " + Packs.Hex(one[^1]));
        foreach (var workers in new[] { 2, 4, 7, 11 })
        {
            var other = (List<ulong>)Packs.Call(typeof(RuleTests), nameof(ForwardingCrowd), RulePack.Kernels, RulePack.Plan, workers)!;
            for (var frame = 0; frame < one.Count; frame++)
            {
                Assert.True(one[frame] == other[frame], $"{workers} workers, frame {frame}: {Packs.Hex(other[frame])}, 1 worker {Packs.Hex(one[frame])}");
            }
        }
    }
}
