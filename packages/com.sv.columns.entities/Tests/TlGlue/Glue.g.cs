using Tl;

namespace Sv.Columns.Entities.TlTests.Glue
{
    [TlState]
    public partial struct PoisonCard { }

    [TlKind]
    public struct PoisonCardFeed
    {
        public static void Combine(ref int total, in int next) => total = next > total ? next : total;

        public static void Apply(in int total, ref PoisonCard card) => card.Value = total > card.Value ? total : card.Value;
    }

    [TlClip("Sv.Columns.Entities.TlTests/poison card")]
    public struct PoisonCardClip { public int Level; }

    public struct PoisonCardTrack : ITlTrack<PoisonCardClip>
    {
        [Feeds(typeof(PoisonCardFeed))]
        public static int Tick(in TlFrame<PoisonCardClip> frame) => frame.Has(FrameFlags.ClipStart) && !frame.IsBackward ? frame.Clip.Level : 0;
    }

    [TlState]
    public partial struct FireOut { }

    [TlKind]
    public struct FireFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref FireOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Columns.Entities.TlTests/Fire")]
    public struct FireClip { public int Amount; }

    public struct FireTrack : ITlTrack<FireClip>
    {
        [Feeds(typeof(FireFeed))]
        public static int Tick(in TlFrame<FireClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    [TlState]
    public partial struct HealingOut { }

    [TlKind]
    public struct HealingFeed
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref HealingOut outbox) => outbox.Value += total;
    }

    [TlClip("Sv.Columns.Entities.TlTests/Healing")]
    public struct HealingClip { public int Amount; }

    public struct HealingTrack : ITlTrack<HealingClip>
    {
        [Feeds(typeof(HealingFeed))]
        public static int Tick(in TlFrame<HealingClip> frame) => frame.Direction * frame.Clip.Amount;
    }
}
