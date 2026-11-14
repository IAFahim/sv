namespace Sv.Host.Tests;

public static class CardPack
{
    public const string Namespace = "HostTests.CardPack";

    const string Values = """
        {"schema":1,"nextLane":8,"rows":[
        {"id":0,"name":"MaxHealth","class":"stat","default":100},
        {"id":1,"name":"MoveSpeed","class":"stat","default":5.0,"scale":100},
        {"id":2,"name":"Health","class":"intrinsic","default":100},
        {"id":3,"name":"Kind","class":"intrinsic","default":0,"role":"kind"},
        {"id":4,"name":"Owner","class":"intrinsic","default":-1,"role":"owner"},
        {"id":5,"name":"Ward","class":"intrinsic","default":0},
        {"id":6,"name":"Fortify","class":"intrinsic","default":0},
        {"lane":7,"word":0,"name":"LowHealth","class":"event","default":false},
        {"lane":7,"word":1,"name":"Warded","class":"event","default":false},
        {"id":0,"name":"Physical","class":"channel"},
        {"id":1,"name":"Healing","class":"channel"},
        {"id":2,"name":"GrantWard","class":"channel","into":"Ward"},
        {"id":3,"name":"GrantFortify","class":"channel","into":"Fortify"},
        {"id":0,"name":"None","class":"kind"},
        {"id":1,"name":"Grunt","class":"kind"}
        ]}
        """;

    const string Rules = """
        {"schema":1,"rows":[
        {"id":4001,"name":"health_flow","layer":"flow","then":[{"add":["Health","Healing"]},{"subtract":["Health","Physical"]}]},
        {"id":4002,"name":"health_bounds","layer":"fold","value":"Health","floor":0,"ceiling":{"stat":"MaxHealth"}},
        {"id":4101,"name":"ward_halves_physical","card":"ward","layer":"gate","priority":10,"when":{"eq":["record.channel","Physical"]},"then":[{"allow":{}},{"multi":-0.5}]},
        {"id":4201,"name":"low_health","layer":"event","event":"LowHealth","enter":{"below":["Health","MaxHealth",30,100]},"stay":{"below":["Health","MaxHealth",40,100]}},
        {"id":4202,"name":"warded","layer":"event","event":"Warded","enter":{"gt":["self.Ward",0]},"stay":{"gt":["self.Ward",0]}},
        {"id":4401,"name":"panic_fortify","layer":"edge","event":"LowHealth","edge":"enter","then":[{"emit":{"to":"Self","channel":"GrantFortify","amount":1}}]}
        ]}
        """;

    const string Cards = """
        {"schema":1,"rows":[
        {"id":1,"name":"ward","value":"Ward","modifiers":[{"stat":"MoveSpeed","multi":-0.5}]},
        {"id":2,"name":"fortify","value":"Fortify","modifiers":[{"stat":"MaxHealth","added":50}]}
        ]}
        """;

    const string Templates = """
        {"schema":1,"rows":[
        {"id":1,"name":"player","values":{"Health":100}},
        {"id":2,"name":"warded","cards":{"ward":1}},
        {"id":3,"name":"grunt","values":{"Kind":1}}
        ]}
        """;

    public static PackSource Source => new(Values, Rules, Cards, Templates);

    public static PackPlan Plan => Packs.Plan(Source);

    public static Type Kernels => Packs.Kernels(Namespace, Plan);
}
