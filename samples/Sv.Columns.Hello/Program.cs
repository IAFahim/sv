using Sv.Columns;

using var pack = PackJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pack.json"))).Compile();
using var world = new ColumnWorld(pack, capacity: 256);

var knight = world.Spawn(pack.Template("knight"));
var cleric = world.Spawn(pack.Template("cleric"));
var warded = world.Spawn(pack.Template("warded"));
var wolf = world.Spawn(pack.Template("wolf"));

var health = pack.Lane("Health");
var shield = pack.Lane("Shield");
var burning = pack.Lane("Burning");
var physical = pack.Channel("Physical");
var writer = world.Writer(0);

for (var frame = 1; frame <= 6; frame++)
{
    switch (frame)
    {
        case 1:
            writer.SendFrom(wolf, knight, physical, 12);
            writer.SendFrom(wolf, warded, physical, 12);
            writer.Send(cleric, physical, 15);
            break;
        case 2:
            world.Set(knight, burning, 3);
            writer.Send(knight, physical, 30, hits: 3);
            break;
        case 4:
            writer.Send(100_000, physical, 50);
            break;
        case 5:
            writer.SendFrom(wolf, knight, physical, 12);
            break;
    }

    world.Step();
    Console.WriteLine(
        $"frame {frame}: knight {world.Get(knight, health),3} hp {world.Get(knight, shield),2} shield {world.Get(knight, burning)} burning" +
        $" | cleric {world.Get(cleric, health),3} hp | warded {world.Get(warded, health),3} hp" +
        $" | entered {Entered(knight, "knight")} | dropped {world.Dropped} | digest {world.Digest():X16}");
}

string Entered(int slot, string name)
{
    var names = new List<string>();
    for (var e = 0; e < pack.EventNames.Count; e++)
    {
        if (world.Entered(slot, e))
        {
            names.Add(name + " " + pack.EventNames[e]);
        }
    }

    return names.Count == 0 ? "-" : string.Join(", ", names);
}
