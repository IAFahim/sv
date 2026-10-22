using System;
using System.Collections.Generic;
using Sv;
using Xunit;

public class ClampShiftTests
{
    static readonly int[] XLattice = { int.MinValue, -1, 0, 1, int.MaxValue, 40, -40 };

    static ClampShift[] Rules()
    {
        var rules = new List<ClampShift>();
        foreach (var delta in new[] { -2, -1, 0, 1, 2 })
        {
            foreach (var floor in new[] { -5, 0, 3, int.MinValue })
            {
                foreach (var ceiling in new[] { -1, 4, 7, int.MaxValue })
                {
                    if (floor <= ceiling)
                    {
                        rules.Add(ClampShift.Step(delta, floor, ceiling));
                    }
                }
            }
        }

        rules.Add(ClampShift.Step(-3, int.MinValue, int.MaxValue));
        rules.Add(ClampShift.Step(3, int.MinValue, int.MaxValue));
        rules.Add(ClampShift.Step(int.MinValue + 1, -10, 10));
        rules.Add(ClampShift.Step(int.MaxValue, -10, 10));
        rules.Add(ClampShift.Step(7, 0, 10));
        rules.Add(ClampShift.Step(-7, 0, 10));
        return rules.ToArray();
    }

    static bool Same(ClampShift a, ClampShift b) => a.Shift == b.Shift && a.Floor == b.Floor && a.Ceiling == b.Ceiling;

    [Fact]
    public void Step_RejectsCrossedBounds()
    {
        Assert.Throws<ArgumentException>(() => ClampShift.Step(1, 5, -5));
    }

    [Fact]
    public void Identity_IsNeutral()
    {
        foreach (var x in XLattice)
        {
            Assert.Equal(x, ClampShift.Identity.Apply(x));
        }

        foreach (var rule in Rules())
        {
            Assert.True(Same(rule, rule.Then(ClampShift.Identity)), $"right identity moved {rule.Shift}/{rule.Floor}/{rule.Ceiling}");
            Assert.True(Same(rule, ClampShift.Identity.Then(rule)), $"left identity moved {rule.Shift}/{rule.Floor}/{rule.Ceiling}");
        }
    }

    [Fact]
    public void CanonicalForm_IsUnique_PerFunction()
    {
        Assert.True(Same(ClampShift.Step(5, 0, 10), ClampShift.Step(5, 0, 10)));
        Assert.Equal(0, ClampShift.Step(1_000_000, 0, 0).Shift);
        Assert.True(Same(ClampShift.Step(1_000_000, 3, 3), ClampShift.Step(-9, 3, 3)));
    }

    [Fact]
    public void Then_IsAssociative()
    {
        var rules = Rules();
        var count = 0;
        foreach (var a in rules)
        {
            foreach (var b in rules)
            {
                foreach (var c in rules)
                {
                    var left = a.Then(b).Then(c);
                    var right = a.Then(b.Then(c));
                    Assert.True(Same(left, right), $"({a.Shift},{a.Floor},{a.Ceiling}).Then({b.Shift},{b.Floor},{b.Ceiling}).Then({c.Shift},{c.Floor},{c.Ceiling}) diverged");
                    count++;
                }
            }
        }

        Assert.True(count > 50_000, $"lattice too small: {count}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public void Repeat_IsThenPow(long ticks)
    {
        foreach (var rule in Rules())
        {
            var pow = ClampShift.Identity;
            for (var i = 0; i < ticks; i++)
            {
                pow = pow.Then(rule);
            }

            Assert.True(Same(pow, rule.Repeat(ticks)), $"Repeat({ticks}) of ({rule.Shift},{rule.Floor},{rule.Ceiling}) diverged from Then^{ticks}");
        }
    }

    [Fact]
    public void Repeat_MatchesEagerSteps_OnLattice()
    {
        foreach (var rule in Rules())
        {
            foreach (var x in XLattice)
            {
                foreach (var ticks in new long[] { 0, 1, 2, 3, 5, 17, 29 })
                {
                    var eager = x;
                    for (var i = 0; i < ticks; i++)
                    {
                        eager = rule.Apply(eager);
                    }

                    Assert.Equal(eager, rule.Repeat(ticks).Apply(x));
                }
            }
        }
    }

    [Fact]
    public void Repeat_MatchesEagerSteps_AtExtremes()
    {
        var extremeRules = new[]
        {
            ClampShift.Step(1, int.MinValue, int.MaxValue),
            ClampShift.Step(-1, int.MinValue, int.MaxValue),
            ClampShift.Step(int.MaxValue, int.MinValue, int.MaxValue),
            ClampShift.Step(int.MinValue + 1, int.MinValue, int.MaxValue),
            ClampShift.Step(4, -2, 2),
            ClampShift.Step(-4, -2, 2),
            ClampShift.Step(2, -2, 2),
        };

        foreach (var rule in extremeRules)
        {
            foreach (var x in XLattice)
            {
                var eager = x;
                for (var i = 0; i < 12; i++)
                {
                    eager = rule.Apply(eager);
                    Assert.Equal(eager, rule.Repeat(i + 1).Apply(x));
                }
            }
        }
    }

    [Fact]
    public void Repeat_SettlesAtTheBound()
    {
        foreach (var x in XLattice)
        {
            Assert.Equal(10, ClampShift.Step(2, 0, 10).Repeat(1L << 40).Apply(x));
            Assert.Equal(0, ClampShift.Step(-2, 0, 10).Repeat(1L << 40).Apply(x));
            Assert.Equal(7, ClampShift.Step(9, 3, 7).Repeat(3).Apply(x));
        }
    }

    [Fact]
    public void Composition_MatchesSequentialApplication_Random()
    {
        var rng = new Random(20261022);
        for (var trial = 0; trial < 20_000; trial++)
        {
            var chain = ClampShift.Identity;
            var value = rng.Next(int.MinValue, int.MaxValue);
            var eager = value;
            var depth = rng.Next(1, 9);
            for (var i = 0; i < depth; i++)
            {
                var delta = rng.Next(-100, 101);
                int floor;
                int ceiling;
                do
                {
                    floor = rng.Next(-1_000, 1_001);
                    ceiling = rng.Next(-1_000, 1_001);
                }
                while (floor > ceiling);

                var step = ClampShift.Step(delta, floor, ceiling);
                chain = chain.Then(step);
                eager = step.Apply(eager);
            }

            Assert.Equal(eager, chain.Apply(value));
        }
    }
}
