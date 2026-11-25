using System;

namespace Sv.V4
{
    public sealed unsafe class RecordStream : IDisposable
    {
        static readonly byte[] SendChannels = { Channel.Physical, Channel.Fire, Channel.Poison, Channel.Healing, Channel.Tick, Channel.Physical, Channel.Healing, Channel.Fire };

        public readonly int Writers;
        public readonly Effect** Lists;
        public readonly int* Counts;
        public readonly long Total;

        public RecordStream(LaneWorld world, int recordsPerEntity, int writers, string sources, bool someNegativeAmounts, bool shuffled = false)
        {
            Writers = writers;
            Lists = (Effect**)Memory.AllocZeroed((nuint)writers, (nuint)sizeof(Effect*));
            Counts = (int*)Memory.AllocZeroed((nuint)writers, sizeof(int));
            var entities = world.Entities;
            for (var w = 0; w < writers; w++)
            {
                var from = (long)entities * w / writers;
                var to = (long)entities * (w + 1) / writers;
                Counts[w] = (int)((to - from) * recordsPerEntity);
                Lists[w] = (Effect*)Memory.Alloc((nuint)Math.Max(1, Counts[w]), (nuint)sizeof(Effect));
                var state = (uint)(0x9E3779B9u ^ (uint)(w * 7919 + 1)) | 1u;
                var at = 0;
                for (var entity = from; entity < to; entity++)
                {
                    for (var op = 0; op < recordsPerEntity; op++)
                    {
                        var roll = Next(ref state);
                        var source = SourceOf(sources, roll, entity, entities, ref state);
                        var kind = source == EffectKey.NoSource ? (byte)0 : Templates.KindOf(Templates.OfChunk(source >> 6));
                        var channel = SendChannels[(roll >> 2) & 7u];
                        var amount = 1 + (int)((roll >> 5) & 7u);
                        amount = someNegativeAmounts && ((roll >> 24) & 15u) == 0u ? -amount : amount;
                        var hits = 1 + (int)((roll >> 8) & 1u);
                        Lists[w][at++] = new Effect(EffectKey.Pack((int)entity, source, kind, channel, Origin.Direct, 0), amount, hits);
                    }
                }

                new Span<Effect>(Lists[w], Counts[w]).Sort((a, b) => EffectLaw.Compare(a, b));
                if (shuffled)
                {
                    for (var i = Counts[w] - 1; i > 0; i--)
                    {
                        var j = (int)(Next(ref state) % (uint)(i + 1));
                        (Lists[w][i], Lists[w][j]) = (Lists[w][j], Lists[w][i]);
                    }
                }

                Total += Counts[w];
            }
        }

        static int SourceOf(string sources, uint roll, long entity, int entities, ref uint state)
        {
            if (sources == "none")
            {
                return EffectKey.NoSource;
            }

            var pick = roll & 3u;
            return pick == 0u ? EffectKey.NoSource
                : pick == 1u ? (int)(Next(ref state) % (uint)entities)
                : (int)(((entity & ~63L) + (Next(ref state) & 63u)) % entities);
        }

        static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public Effect[] Gather()
        {
            var all = new Effect[Total];
            var at = 0;
            for (var w = 0; w < Writers; w++)
            {
                new ReadOnlySpan<Effect>(Lists[w], Counts[w]).CopyTo(new Span<Effect>(all, at, Counts[w]));
                at += Counts[w];
            }

            return all;
        }

        public void Dispose()
        {
            for (var w = 0; w < Writers; w++)
            {
                Memory.Free(Lists[w]);
            }

            Memory.Free(Lists);
            Memory.Free(Counts);
        }
    }
}
