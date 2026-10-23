using System;
using Sv;

// Roadmap 5.4 receipt: a keyframe at 2^20 entities costs O(dirty pages), not
// O(capacity x stats). One iteration = 96 scattered modifier edits + Recalculate +
// SaveKeyframe (dirty pages only) + folding it onto a second board loaded from the
// epoch base (LoadKeyframe re-resolves and digest-verifies) — the late-join loop.

internal sealed unsafe class KeyframeBench : BenchBase
{
    const int Entities = 1 << 20;

    const int Edits = 96;

    Board source;

    Board receiver;

    SvState keyframe;

    StatId power;

    uint seed = 0x9E3779B9u;

    long checksum;

    internal static KeyframeBench New()
    {
        return new KeyframeBench();
    }

    KeyframeBench()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Stat("resolve", 5f).Build();
        power = schema.Stat("power");
        source = Board.Create(schema, Entities);
        for (var i = 0; i < Entities; i++)
        {
            source.Acquire();
        }

        source.Recalculate();
        var baseline = new SvState();
        source.SaveState(baseline);
        receiver = Board.Create(schema, Entities);
        receiver.LoadState(baseline);
        baseline.Dispose();
        keyframe = new SvState();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);
    }

    void Frame()
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        for (var i = 0; i < Edits; i++)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            source.AddModifier((int)(seed % (uint)Entities), power, new Modifier(ModifierKind.Multi, 1f + (seed % 3) * 0.25f));
        }

        source.Recalculate();
        source.SaveKeyframe(keyframe);
        receiver.LoadKeyframe(keyframe);
    }

    public override long Frame(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            Frame();
            checksum = checksum * 31 + (long)receiver.Digest;
        }

        return checksum;
    }

    public override string Verify()
    {
        Frame();
        return source.Digest == receiver.Digest ? "ok" : $"digest diverged ({source.Digest:X16} vs {receiver.Digest:X16})";
    }

    public override int RowsPerFrame => 1;

    public override void Dispose()
    {
        keyframe.Dispose();
        receiver.Dispose();
        source.Dispose();
    }
}
