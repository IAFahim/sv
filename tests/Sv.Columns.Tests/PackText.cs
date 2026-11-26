namespace Sv.Columns.Tests;

public static class PackText
{
    public static string Describe(ColumnPack pack)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("lanes " + string.Join(", ", pack.Lanes.Select(l => l.Name + "=" + l.Default)) + " kindLane " + pack.KindLane);
        text.AppendLine("channels " + string.Join(", ", pack.Channels) + "; kinds " + string.Join(", ", pack.Kinds));
        string Op(OpSpec o) => o.Channel + " " + o.Amount + (o.Times != null ? " x " + o.Times : "") + (o.Kind != null ? " kind " + o.Kind : "") + (o.When != null ? " when " + o.When : "");
        foreach (var card in pack.Cards)
        {
            text.AppendLine("card " + card.Name + " lane " + card.Lane + ": " + string.Join("; ", card.Ops.Select(Op)));
        }

        foreach (var t in pack.Templates)
        {
            text.AppendLine("template " + t.Name + " values " + string.Join(",", t.Values.Select(p => p.Key + "=" + p.Value)) + " cards " + string.Join(",", t.Cards.Select(p => p.Key + "=" + p.Value)) + ": " + string.Join("; ", t.Ops.Select(Op)));
        }

        foreach (var rule in pack.Rules)
        {
            text.AppendLine(rule switch
            {
                DenyRule d => "deny " + (d.Channels == null ? "*" : string.Join(",", d.Channels)) + " kinds " + (d.Kinds == null ? "*" : string.Join(",", d.Kinds)) + " when " + d.When,
                DenyByMaskRule m => "deny by mask " + m.Lane + (m.ByKind ? " kind" : " channel"),
                ScaleRule s => "scale " + string.Join(",", s.Channels) + " x" + s.Times + " when " + s.When + " source " + s.SourceWhen,
                ConvertRule c => "convert " + c.From + " -> " + c.To + " " + c.Numerator + "/" + c.Denominator + " when " + c.When,
                AbsorbRule a => "absorb " + a.Budget + " from " + string.Join(",", a.Channels),
                FlowRule f => "flow " + f.Lane + " + " + string.Join(",", f.Add) + " - " + string.Join(",", f.Subtract),
                BoundsRule b => "bounds " + b.Lane + " [" + (b.Floor.Present ? b.Floor.Lane ?? b.Floor.Value.ToString() : "-") + ", " + (b.Ceiling.Present ? b.Ceiling.Lane ?? b.Ceiling.Value.ToString() : "-") + "]",
                EventRule e => "event " + e.Name + " enter " + e.Enter + " stay " + e.Stay,
                _ => rule.ToString(),
            });
        }

        return text.ToString();
    }
}
