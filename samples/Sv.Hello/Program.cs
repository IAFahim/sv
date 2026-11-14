using Hello.Sv;
using Sv;
using Sv.Host;

using var world = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { Workers = 1 });

var hero = world.Spawn((int)Template.hero);
var knight = world.Spawn((int)Template.knight);
var goblin = world.Spawn((int)Template.goblin);
var dragon = world.Spawn((int)Template.dragon);

world.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
world.Send(hero, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45);
world.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
world.Send(knight, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45);
world.Step();
Report("frame 1");

world.Send(hero, hero, 0, (int)Channel.Healing, 50);
world.Step();
Report("frame 2");

void Report(string when)
{
    Console.WriteLine($"{when}: hero health {world.Read(hero, (int)Intrinsic.Health)}, knight health {world.Read(knight, (int)Intrinsic.Health)} shield {world.Read(knight, (int)Intrinsic.Shield)}, hero low health {world.Level(hero, (int)Event.LowHealth)}, digest {world.LastDigest:X16}");
}
