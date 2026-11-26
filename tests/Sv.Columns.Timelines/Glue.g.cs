using Tl;

namespace Sv.Columns.Timelines.Glue
{
    [TlState]
    public partial struct PoisonCard { public int Value; }

    [TlKind]
    public struct PoisonCardFeed
    {
        public static void Combine(ref int total, in int next) => total = next > total ? next : total;

        public static void Apply(in int total, ref PoisonCard card) => card.Value = total > card.Value ? total : card.Value;
    }

    [TlClip("Sv.Columns.Timelines/poison card")]
    public struct PoisonCardClip { public int Level; }

    public struct PoisonCardTrack : ITlTrack<PoisonCardClip>
    {
        [Feeds(typeof(PoisonCardFeed))]
        public static int Tick(in TlFrame<PoisonCardClip> frame) => frame.Has(FrameFlags.ClipStart) && !frame.IsBackward ? frame.Clip.Level : 0;
    }

    [TlState]
    public partial struct FireOut { public int Value; }

    [TlKind]
    public struct FireFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref FireOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Columns.Timelines/Fire")]
    public struct FireClip { public int Amount; }

    public struct FireTrack : ITlTrack<FireClip>
    {
        [Feeds(typeof(FireFeed))]
        public static int Tick(in TlFrame<FireClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    [TlState]
    public partial struct HealingOut { public int Value; }

    [TlKind]
    public struct HealingFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref HealingOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Columns.Timelines/Healing")]
    public struct HealingClip { public int Amount; }

    public struct HealingTrack : ITlTrack<HealingClip>
    {
        [Feeds(typeof(HealingFeed))]
        public static int Tick(in TlFrame<HealingClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    public sealed class TimelineRequests
    {
        static readonly string[] Events =
        {
            "LowHealth",
        };

        static readonly string[] Timelines =
        {
            "poison",
        };

        readonly int[] events = new int[Events.Length];

        readonly ushort[] resolved = new ushort[Events.Length];

        int[] held = global::System.Array.Empty<int>();

        public long Started;

        public long Stopped;

        public TimelineRequests(TimelineWorld world, global::Sv.Columns.CompiledPack pack)
        {
            for (var i = 0; i < Events.Length; i++)
            {
                events[i] = pack.Event(Events[i]);
                resolved[i] = world.Find(Timelines[i]);
            }
        }

        public void Apply(PlaybackTable players, global::Sv.Columns.ColumnWorld world)
        {
            if (held.Length < world.Capacity)
            {
                global::System.Array.Resize(ref held, world.Capacity);
            }

            for (var slot = 0; slot < world.Capacity; slot++)
            {
                if (!world.IsAlive(slot))
                {
                    held[slot] = 0;
                    continue;
                }

                var now = 0;
                for (var i = 0; i < events.Length; i++)
                {
                    var bit = 1 << (events[i] & 31);
                    var on = world.Event(slot, events[i]);
                    if (on)
                    {
                        now |= bit;
                    }

                    if (resolved[i] == 0)
                    {
                        continue;
                    }

                    if (world.Entered(slot, events[i]))
                    {
                        players.Start(slot, resolved[i]);
                        Started++;
                    }
                    else if (!on && (held[slot] & bit) != 0)
                    {
                        players.Stop(slot, resolved[i]);
                        Stopped++;
                    }
                }

                held[slot] = now;
            }
        }
    }
}
