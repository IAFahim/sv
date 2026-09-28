using Tl;

namespace NorthStar;

public readonly record struct JumpClip(float Velocity);

public readonly record struct JumpTrack(float Scale) : IBlend<JumpClip>
{
    public void Blend(in JumpClip first, in JumpClip second, float factor, out JumpClip result)
    {
        result = new JumpClip(first.Velocity + (second.Velocity - first.Velocity) * factor);
    }
}

public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void OnActive(in Frame<JumpTrack, JumpClip> frame, ref float y)
    {
        y += frame.Direction * frame.Clip.Velocity * frame.Track.Scale;
    }
}
