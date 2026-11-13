using Tl;
using Sv.Host;

namespace Sv.Kernels.NorthStar3.Glue
{
    [TlState]
    public partial struct PoisonOut { public int Value; }

    [TlKind]
    public struct PoisonFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref PoisonOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Kernels.NorthStar3/Poison")]
    public struct PoisonClip { public int Amount; }

    public struct PoisonTrack : ITlTrack<PoisonClip>
    {
        [Feeds(typeof(PoisonFeed))]
        public static int Tick(in TlFrame<PoisonClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    [TlState]
    public partial struct BleedingOut { public int Value; }

    [TlKind]
    public struct BleedingFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref BleedingOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Kernels.NorthStar3/Bleeding")]
    public struct BleedingClip { public int Amount; }

    public struct BleedingTrack : ITlTrack<BleedingClip>
    {
        [Feeds(typeof(BleedingFeed))]
        public static int Tick(in TlFrame<BleedingClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    public sealed class TimelineRequests
    {
        static readonly int[] Raised =
        {
            (int)global::Sv.Kernels.NorthStar3.Event.LowHealth,
        };

        static readonly string[] Timelines =
        {
            "poison",
        };

        readonly ushort[] resolved = new ushort[Raised.Length];

        public TimelineRequests(TimelineWorld world)
        {
            for (var i = 0; i < Raised.Length; i++)
            {
                resolved[i] = world.Find(Timelines[i]);
            }
        }

        public unsafe void Apply(PlaybackTable players, HostEvent* events, int count)
        {
            for (var at = 0; at < count; at++)
            {
                for (var i = 0; i < Raised.Length; i++)
                {
                    if (events[at].Event == Raised[i] && resolved[i] != 0)
                    {
                        if (events[at].Entered)
                        {
                            players.Start(events[at].Slot, resolved[i]);
                        }
                        else
                        {
                            players.Stop(events[at].Slot, resolved[i]);
                        }
                    }
                }
            }
        }
    }
}
