using System;

namespace Sv.Columns.Canon
{
    public static class CanonRun
    {
        public const int Capacity = 1024;
        public const int Frames = 48;
        public const ulong Pinned = 0x77EC257ECB72B2CFul;

        public static ulong Run(ColumnWorld world, Action step)
        {
            var pack = world.Pack;
            var slots = new int[600];
            var count = 0;
            for (var t = 0; t < pack.TemplateNames.Count; t++)
            {
                for (var i = 0; i < 60; i++)
                {
                    slots[count++] = world.Spawn(t);
                }
            }

            var channels = pack.ChannelNames.Count;
            var state = 0x2545F491u;
            var hash = 0xCBF29CE484222325ul;
            for (var frame = 0; frame < Frames; frame++)
            {
                if (frame == 12)
                {
                    world.Set(slots[7], pack.Lane("ImmuneMask"), 0b101);
                    world.Set(slots[70], pack.Lane("KindMask"), 0b10);
                }

                if (frame == 24)
                {
                    world.Despawn(slots[3]);
                    slots[3] = world.Spawn(pack.Template("phoenix"));
                }

                for (var r = 0; r < 4000; r++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    var writer = world.Writer((int)(state >> 3) % world.Writers);
                    var target = slots[(int)((state >> 7) % (uint)count)];
                    var channel = (int)((state >> 11) % (uint)channels);
                    var amount = (int)((state >> 15) % 41u) - 12;
                    var hits = 1 + (int)((state >> 23) & 3u);
                    if ((state & 7u) == 0)
                    {
                        writer.SendFrom(slots[(int)((state >> 9) % (uint)count)], target, channel, amount, hits);
                    }
                    else
                    {
                        writer.Send(target, channel, amount, hits);
                    }
                }

                step();
                hash = (hash ^ world.Digest()) * 0x100000001B3ul;
            }

            return hash;
        }
    }
}
