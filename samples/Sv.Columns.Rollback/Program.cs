using Sv.Columns;

const string arenaPackJson = """
{
  "lanes": [ { "name": "Health", "default": 100 }, { "name": "Shield", "default": 0 } ],
  "channels": ["Physical"],
  "templates": [ { "name": "hero" }, { "name": "goblin", "values": { "Health": 40 } } ],
  "rules": [
    { "absorb": { "budget": "Shield", "channels": ["Physical"] } },
    { "flow": { "lane": "Health", "add": [], "subtract": ["Physical"] } },
    { "bounds": { "lane": "Health", "floor": 0, "ceiling": 100 } }
  ]
}
""";

using var pack = PackJson.Read(arenaPackJson).Compile();
var health = pack.Lane("Health");
var shield = pack.Lane("Shield");
var physical = pack.Channel("Physical");
const int framesKeptForRollback = 8;
using var world = new ColumnWorld(pack, capacity: 128, history: framesKeptForRollback);
var hero = world.Spawn(pack.Template("hero"));
var goblin = world.Spawn(pack.Template("goblin"));

for (var frame = 0; frame < 6; frame++)
{
    world.Writer(0).Send(hero, physical, 5);
    world.Step();
}

Console.WriteLine($"predicted: after frame {world.Frame} the hero has {world.Get(hero, health)} health");

var lateFrame = 2;
var remoteShieldUp = ColumnAction.Set(hero, shield, 8);
world.Inputs(lateFrame).AddAction(remoteShieldUp);

var correctedFrame = 4;
var correctedInputs = world.Inputs(correctedFrame);
correctedInputs.RemoveRecord(0);
correctedInputs.AddAction(ColumnAction.Send(hero, physical, 12));

world.Resimulate();
Console.WriteLine($"corrected: the shield was raised in frame {lateFrame} and frame {correctedFrame}'s hit was 12, so after frame {world.Frame} the hero has {world.Get(hero, health)} health and {world.Get(hero, shield)} shield");

using var freshWorldWithCorrectedInputs = new ColumnWorld(pack, capacity: 128);
freshWorldWithCorrectedInputs.Spawn(pack.Template("hero"));
freshWorldWithCorrectedInputs.Spawn(pack.Template("goblin"));
for (var frame = 0; frame < 6; frame++)
{
    if (frame == lateFrame)
    {
        freshWorldWithCorrectedInputs.Set(hero, shield, 8);
    }

    freshWorldWithCorrectedInputs.Writer(0).Send(hero, physical, frame == correctedFrame ? 12 : 5);
    freshWorldWithCorrectedInputs.Step();
}

Console.WriteLine($"a fresh run of the corrected inputs gives the same world: {world.Digest() == freshWorldWithCorrectedInputs.Digest()}");

world.Rewind(3);
Console.WriteLine($"rewound to frame {world.Frame}: the hero has {world.Get(hero, health)} health and {world.Get(hero, shield)} shield, goblin alive {world.IsAlive(goblin)}");
