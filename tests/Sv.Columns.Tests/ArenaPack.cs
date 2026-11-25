namespace Sv.Columns.Tests;

public static class ArenaPack
{
    public static ColumnPack Build()
    {
        var pack = new ColumnPack { KindLane = "Kind" };
        foreach (var (name, value) in new[]
        {
            ("Health", 100), ("MaxHealth", 100), ("Shield", 0), ("PoisonTicks", 0), ("RegenRate", 0), ("RegenCard", 0),
            ("Converter", 0), ("Execute", 0), ("ImmuneMask", 0), ("KindMask", 0), ("Kind", 0), ("ReviveCharges", 0), ("FireTaken", 0),
        })
        {
            pack.Lanes.Add(new LaneSpec(name, value));
        }

        pack.Channels.AddRange(new[] { "Physical", "Poison", "Fire", "Healing", "PoisonTime", "ReviveSpend" });
        pack.Kinds.AddRange(new[] { "None", "Sniper", "Rifle", "Beast" });
        pack.Cards.Add(new CardSpec("poison", "PoisonTicks", new OpSpec("Poison", 4), new OpSpec("PoisonTime", -1)));
        pack.Cards.Add(new CardSpec("regen", "RegenCard", new OpSpec("Healing", 1, times: "RegenRate")));
        pack.Cards.Add(new CardSpec("converter", "Converter"));
        pack.Cards.Add(new CardSpec("revive", "ReviveCharges", new OpSpec("Healing", 50, when: Cond.Entered("Dead")), new OpSpec("ReviveSpend", -1, when: Cond.Entered("Dead"))));

        pack.Templates.Add(new TemplateSpec("dummy"));
        var poisoned = new TemplateSpec("poisoned");
        poisoned.Values["PoisonTicks"] = 3;
        pack.Templates.Add(poisoned);
        var regen = new TemplateSpec("regen");
        regen.Values["Health"] = 25;
        regen.Values["RegenRate"] = 4;
        regen.Cards["regen"] = 1;
        pack.Templates.Add(regen);
        var shielded = new TemplateSpec("shielded");
        shielded.Values["Shield"] = 30;
        pack.Templates.Add(shielded);
        var converter = new TemplateSpec("converter");
        converter.Values["Health"] = 50;
        converter.Cards["converter"] = 1;
        pack.Templates.Add(converter);
        var executioner = new TemplateSpec("executioner");
        executioner.Values["Execute"] = 1;
        pack.Templates.Add(executioner);
        var sniper = new TemplateSpec("sniper");
        sniper.Values["Kind"] = 1;
        pack.Templates.Add(sniper);
        var phoenix = new TemplateSpec("phoenix");
        phoenix.Values["Health"] = 30;
        phoenix.Cards["revive"] = 1;
        pack.Templates.Add(phoenix);
        var burning = new TemplateSpec("burning");
        burning.Ops.Add(new OpSpec("Fire", 3));
        burning.Ops.Add(new OpSpec("Fire", 2, when: Cond.Below("Health", "MaxHealth", 50)));
        pack.Templates.Add(burning);

        pack.Rules.Add(new DenyByMaskRule("ImmuneMask", byKind: false));
        pack.Rules.Add(new DenyByMaskRule("KindMask", byKind: true));
        pack.Rules.Add(new ScaleRule(new[] { "Physical" }, 2, Cond.Below("Health", "MaxHealth", 20), Cond.Gt("Execute", 0)));
        pack.Rules.Add(new ConvertRule("Fire", "Healing", 1, 1, Cond.Gt("Converter", 0)));
        pack.Rules.Add(new AbsorbRule("Shield", "Physical", "Poison", "Fire"));
        pack.Rules.Add(new FlowRule("Health", new[] { "Healing" }, new[] { "Physical", "Poison", "Fire" }));
        pack.Rules.Add(new FlowRule("FireTaken", new[] { "Fire" }, System.Array.Empty<string>()));
        pack.Rules.Add(new FlowRule("PoisonTicks", new[] { "PoisonTime" }, System.Array.Empty<string>()));
        pack.Rules.Add(new FlowRule("ReviveCharges", new[] { "ReviveSpend" }, System.Array.Empty<string>()));
        pack.Rules.Add(new BoundsRule("Health", Bound.Of(0), Bound.Of("MaxHealth")));
        pack.Rules.Add(new BoundsRule("PoisonTicks", Bound.Of(0), Bound.None));
        pack.Rules.Add(new BoundsRule("ReviveCharges", Bound.Of(0), Bound.None));
        pack.Rules.Add(new EventRule("Dead", Cond.Le("Health", 0)));
        pack.Rules.Add(new EventRule("LowHealth", Cond.Below("Health", "MaxHealth", 30), Cond.Below("Health", "MaxHealth", 40)));
        return pack;
    }
}
