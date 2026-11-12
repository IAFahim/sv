using Tl;

namespace SvTlGame
{
    [TlState]
    public partial struct Vigor
    {
        public int Value;
    }

    [TlState]
    public partial struct VigorDelta
    {
        public int Value;
    }

    [TlState]
    public partial struct VigorMax
    {
        public float Value;
    }

    [TlClip("SvTlGame/Drain")]
    public struct DrainClip
    {
        public int Amount;
    }

    [TlKind]
    public struct DrainKind
    {
        public static void Combine(ref int total, in int next) => total += next;

        public static void Apply(in int total, ref VigorDelta delta) => delta.Value -= total;
    }

    public struct DrainTrack : ITlTrack<DrainClip>
    {
        [Feeds(typeof(DrainKind))]
        public static int Evaluate(in TlFrame<DrainClip> frame) => frame.Clip.Amount;
    }
}
