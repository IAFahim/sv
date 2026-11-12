using Tl;

namespace Sv.NorthStar2
{
    [TlState]
    public partial struct HpDelta
    {
        public int Value;
    }

    [TlState]
    public partial struct Outgoing
    {
        public int Value;
    }

    [TlState]
    public partial struct ArmorPulse
    {
        public int Value;
    }

    [TlClip("NorthStar2/Hit")]
    public struct HitClip
    {
        public int Amount;
    }

    [TlClip("NorthStar2/Heal")]
    public struct HealClip
    {
        public int Amount;
    }

    [TlClip("NorthStar2/Poison")]
    public struct PoisonClip
    {
        public int Amount;
    }

    [TlClip("NorthStar2/Strike")]
    public struct StrikeClip
    {
        public int Amount;
    }

    [TlClip("NorthStar2/Guard")]
    public struct GuardClip
    {
        public int Armor;
    }

    [TlKind(Accepts = KindEntries.Both, GamePulses = 8)]
    public struct Damage
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref HpDelta delta) => delta.Value += total;
    }

    [TlKind(Accepts = KindEntries.Pulses, Mode = KindMode.Each)]
    public struct Strike
    {
        public static void Apply(in int amount, ref Outgoing outgoing) => outgoing.Value += amount;
    }

    [TlKind(Accepts = KindEntries.Pulses, Mode = KindMode.Each)]
    public struct ArmorEdge
    {
        public static void Apply(in int delta, ref ArmorPulse pulse) => pulse.Value += delta;
    }

    public struct HitTrack : ITlTrack<HitClip>
    {
        [Pulse(typeof(Damage), On = ClipEvent.Start)]
        public static int Hit(in TlFrame<HitClip> frame) => -frame.Direction * frame.Clip.Amount;
    }

    public struct HealTrack : ITlTrack<HealClip>
    {
        [Pulse(typeof(Damage), On = ClipEvent.Start)]
        public static int Heal(in TlFrame<HealClip> frame) => frame.Direction * frame.Clip.Amount;
    }

    public struct PoisonTrack : ITlTrack<PoisonClip>
    {
        [Feeds(typeof(Damage))]
        public static int Tick(in TlFrame<PoisonClip> frame) => -frame.Direction * frame.Clip.Amount;
    }

    public struct StrikeTrack : ITlTrack<StrikeClip>
    {
        [Pulse(typeof(Strike), On = ClipEvent.Start)]
        public static int Swing(in TlFrame<StrikeClip> frame) => -frame.Direction * frame.Clip.Amount;
    }

    public struct GuardTrack : ITlTrack<GuardClip>
    {
        [Pulse(typeof(ArmorEdge), On = ClipEvent.Start)]
        public static int Raise(in TlFrame<GuardClip> frame) => frame.Direction * frame.Clip.Armor;

        [Pulse(typeof(ArmorEdge), On = ClipEvent.End)]
        public static int Lower(in TlFrame<GuardClip> frame) => -frame.Direction * frame.Clip.Armor;
    }
}
