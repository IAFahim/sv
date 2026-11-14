using System.Collections.Generic;
using Hello.Sv;
using Sv;
using Sv.Host;
using Sv.Unity;
using UnityEngine;
using Event = Hello.Sv.Event;

namespace Hello
{
    public sealed class HelloSv : MonoBehaviour
    {
        void Start()
        {
            foreach (var line in Run())
            {
                Debug.Log(line);
            }
        }

        public static List<string> Run()
        {
            var lines = new List<string>();
            using var world = PackWorld.Create(new HostOptions { Workers = 1 });
            var hero = world.Spawn((int)Template.hero);
            var knight = world.Spawn((int)Template.knight);
            var goblin = world.Spawn((int)Template.goblin);
            var dragon = world.Spawn((int)Template.dragon);

            world.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, Origin.Direct);
            world.Send(hero, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45, 1, Origin.Direct);
            world.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, Origin.Direct);
            world.Send(knight, dragon, (int)Kind.Dragon, (int)Channel.Fire, 45, 1, Origin.Direct);
            world.Step();
            lines.Add(Report(world, "frame 1", hero, knight));

            world.Send(hero, hero, 0, (int)Channel.Healing, 50, 1, Origin.Direct);
            world.Step();
            lines.Add(Report(world, "frame 2", hero, knight));
            return lines;
        }

        static string Report(ISvWorld world, string when, int hero, int knight) =>
            $"{when}: hero health {world.Read(hero, (int)Intrinsic.Health)}, knight health {world.Read(knight, (int)Intrinsic.Health)} shield {world.Read(knight, (int)Intrinsic.Shield)}, hero low health {world.Level(hero, (int)Event.LowHealth)}, digest {world.LastDigest:X16}";
    }
}
