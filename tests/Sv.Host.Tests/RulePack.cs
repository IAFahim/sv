using System.Text;

namespace Sv.Host.Tests;

public static class RulePack
{
    public const string Namespace = "HostTests.RulePack";

    public const string Values = """
        {"schema":1,"nextLane":6,"rows":[
        {"id":0,"name":"MaxHealth","class":"stat","default":100},
        {"id":1,"name":"Health","class":"intrinsic","default":100},
        {"id":2,"name":"Kind","class":"intrinsic","default":0,"role":"kind"},
        {"id":3,"name":"Owner","class":"intrinsic","default":-1,"role":"owner"},
        {"id":4,"name":"Armor","class":"intrinsic","default":0},
        {"id":5,"name":"Broken","class":"intrinsic","default":0},
        {"id":0,"name":"Physical","class":"channel"},
        {"id":1,"name":"Healing","class":"channel"},
        {"id":2,"name":"Adopt","class":"channel","into":"Owner"},
        {"id":0,"name":"None","class":"kind"}
        ]}
        """;

    const string Rules = """
        {"schema":1,"rows":[
        {"id":4001,"name":"health_flow","layer":"flow","then":[{"add":["Health","Healing"]},{"subtract":["Health","Physical"]}]},
        {"id":4002,"name":"health_bounds","layer":"fold","value":"Health","floor":0,"ceiling":{"stat":"MaxHealth"}},
        {"id":4003,"name":"armor_break","layer":"flow","when":{"gt":[{"total":"Physical"},20]},"then":[{"add":["Broken","Physical"]}]},
        {"id":4101,"name":"armored_halves","layer":"gate","priority":20,"group":1,"when":{"and":[{"eq":["record.channel","Physical"]},{"gt":["self.Armor",0]}]},"then":[{"multi":-0.5}]},
        {"id":4102,"name":"plain_quarter","layer":"gate","priority":10,"group":1,"when":{"eq":["record.channel","Physical"]},"then":[{"multi":-0.25}]},
        {"id":4103,"name":"padding","layer":"gate","priority":5,"when":{"eq":["record.channel","Physical"]},"then":[{"added":-10}]}
        ]}
        """;

    const string Cards = """{"schema":1,"rows":[]}""";

    const string Templates = """
        {"schema":1,"rows":[
        {"id":1,"name":"player"},
        {"id":2,"name":"armored","values":{"Armor":1}},
        {"id":3,"name":"hand","forwards":[{"channel":"Healing","to":"Owner"}]}
        ]}
        """;

    public static PackSource Source => new(Values, Rules, Cards, Templates);

    public static PackPlan Plan => Packs.Plan(Source);

    public static Type Kernels => Packs.Kernels(Namespace, Plan);

    public static PackSource WithGates(string gates) => new(Values, "{\"schema\":1,\"rows\":[" + gates + "]}", Cards, Templates);

    public static string ShapeGroups(int groups)
    {
        var rows = new StringBuilder();
        for (var group = 0; group < groups; group++)
        {
            rows.Append(group == 0 ? string.Empty : ",");
            rows.Append("{\"id\":").Append(5000 + group).Append(",\"name\":\"g").Append(group).Append("\",\"layer\":\"gate\",\"priority\":").Append(group)
                .Append(",\"group\":").Append(group).Append(",\"when\":{\"eq\":[\"record.channel\",\"Physical\"]},\"then\":[{\"multi\":-0.01}]}");
        }

        return rows.ToString();
    }
}
