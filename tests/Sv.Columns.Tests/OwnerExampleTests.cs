namespace Sv.Columns.Tests;

public sealed class OwnerExampleTests : IDisposable
{
    readonly CompiledPack pack = ArenaPack.Build().Compile();

    public void Dispose() => pack.Dispose();

    [Fact]
    public void FireBecomesHealingWhileTheConverterCardIsActive()
    {
        using var twin = new Twin(pack);
        var withCard = twin.Spawn("converter");
        var without = twin.Spawn("dummy");
        twin.Set(without, "Health", 50);
        twin.Send(withCard, "Fire", 20);
        twin.Send(without, "Fire", 20);
        twin.Step();
        Assert.Equal(70, twin.Get(withCard, "Health"));
        Assert.Equal(0, twin.Get(withCard, "FireTaken"));
        Assert.Equal(30, twin.Get(without, "Health"));
        Assert.Equal(20, twin.Get(without, "FireTaken"));
    }

    [Fact]
    public void ExecutingSourcesDoubleDamageBelowTwentyPercent()
    {
        using var twin = new Twin(pack);
        var executioner = twin.Spawn("executioner");
        var plain = twin.Spawn("dummy");
        var weak = twin.Spawn("dummy");
        var weakToo = twin.Spawn("dummy");
        var healthy = twin.Spawn("dummy");
        twin.Set(weak, "Health", 15);
        twin.Set(weakToo, "Health", 15);
        twin.Send(weak, executioner, "Physical", 10);
        twin.Send(weakToo, plain, "Physical", 10);
        twin.Send(healthy, executioner, "Physical", 10);
        twin.Step();
        Assert.Equal(0, twin.Get(weak, "Health"));
        Assert.True(twin.Event(weak, "Dead"));
        Assert.True(twin.Entered(weak, "Dead"));
        Assert.Equal(5, twin.Get(weakToo, "Health"));
        Assert.Equal(90, twin.Get(healthy, "Health"));
    }

    [Fact]
    public void ShieldAbsorbsTheDamagePoolFirstChannelFirst()
    {
        using var twin = new Twin(pack);
        var shielded = twin.Spawn("shielded");
        twin.Send(shielded, "Physical", 20);
        twin.Send(shielded, "Fire", 15);
        twin.Send(shielded, "Healing", -3);
        twin.Step();
        Assert.Equal(0, twin.Get(shielded, "Shield"));
        Assert.Equal(92, twin.Get(shielded, "Health"));
        Assert.Equal(5, twin.Get(shielded, "FireTaken"));
    }

    [Fact]
    public void PoisonTicksAreACardThatCountsItselfDown()
    {
        using var twin = new Twin(pack);
        var poisoned = twin.Spawn("poisoned");
        var expected = new[] { (96, 2), (92, 1), (88, 0), (88, 0) };
        foreach (var (health, ticks) in expected)
        {
            twin.Step();
            Assert.Equal(health, twin.Get(poisoned, "Health"));
            Assert.Equal(ticks, twin.Get(poisoned, "PoisonTicks"));
        }
    }

    [Fact]
    public void RegenHealsByItsRateUpToMaxHealth()
    {
        using var twin = new Twin(pack);
        var regen = twin.Spawn("regen");
        twin.Set(regen, "Health", 93);
        twin.Step();
        Assert.Equal(97, twin.Get(regen, "Health"));
        twin.Step();
        Assert.Equal(100, twin.Get(regen, "Health"));
    }

    [Fact]
    public void ImmunityByChannelAndBySourceKind()
    {
        using var twin = new Twin(pack);
        var sniper = twin.Spawn("sniper");
        var rifle = twin.Spawn("dummy");
        twin.Set(rifle, "Kind", 2);
        var target = twin.Spawn("dummy");
        twin.Set(target, "ImmuneMask", 1 << 2);
        twin.Set(target, "KindMask", 1 << 1);
        twin.Send(target, "Fire", 30);
        twin.Send(target, sniper, "Physical", 30);
        twin.Send(target, rifle, "Physical", 7);
        twin.Step();
        Assert.Equal(93, twin.Get(target, "Health"));
    }

    [Fact]
    public void LowHealthEntersBelowThirtyAndStaysUntilForty()
    {
        using var twin = new Twin(pack);
        var target = twin.Spawn("dummy");
        twin.Send(target, "Physical", 71);
        twin.Step();
        Assert.True(twin.Event(target, "LowHealth"));
        Assert.True(twin.Entered(target, "LowHealth"));
        twin.Send(target, "Healing", 6);
        twin.Step();
        Assert.True(twin.Event(target, "LowHealth"));
        Assert.False(twin.Entered(target, "LowHealth"));
        twin.Send(target, "Healing", 6);
        twin.Step();
        Assert.False(twin.Event(target, "LowHealth"));
    }

    [Fact]
    public void PhoenixRevivesOnceAfterDying()
    {
        using var twin = new Twin(pack);
        var phoenix = twin.Spawn("phoenix");
        twin.Send(phoenix, "Physical", 40);
        twin.Step();
        Assert.Equal(0, twin.Get(phoenix, "Health"));
        Assert.True(twin.Entered(phoenix, "Dead"));
        twin.Step();
        Assert.Equal(50, twin.Get(phoenix, "Health"));
        Assert.Equal(0, twin.Get(phoenix, "ReviveCharges"));
        Assert.False(twin.Event(phoenix, "Dead"));
        twin.Send(phoenix, "Physical", 60);
        twin.Step();
        twin.Step();
        Assert.Equal(0, twin.Get(phoenix, "Health"));
        Assert.True(twin.Event(phoenix, "Dead"));
    }

    [Fact]
    public void BurningTemplateAddsMoreFireBelowHalfHealth()
    {
        using var twin = new Twin(pack);
        var burning = twin.Spawn("burning");
        twin.Set(burning, "Health", 55);
        twin.Step();
        Assert.Equal(52, twin.Get(burning, "Health"));
        twin.Step();
        Assert.Equal(49, twin.Get(burning, "Health"));
        twin.Step();
        Assert.Equal(44, twin.Get(burning, "Health"));
    }

    [Fact]
    public void HitsMultiplyAndAmountsSaturateAtTwentyEightBits()
    {
        using var twin = new Twin(pack);
        var target = twin.Spawn("dummy");
        twin.Set(target, "MaxHealth", int.MaxValue);
        twin.Set(target, "Health", 1000);
        twin.Send(target, "Physical", 7, hits: 3);
        twin.Step();
        Assert.Equal(979, twin.Get(target, "Health"));
        twin.Send(target, "Healing", long.MaxValue / 4);
        twin.Step();
        Assert.Equal(979 + (int)Record.AmountMax, twin.Get(target, "Health"));
    }
}
