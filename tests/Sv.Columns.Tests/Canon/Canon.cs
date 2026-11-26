using System;

namespace Sv.Columns.Canon
{
    public static class CanonRun
    {
        public const int Capacity = 1024;
        public const int Frames = 48;
        public const ulong Pinned = 0x77EC257ECB72B2CFul;

        public const int LateHistory = 8;
        public const ulong PinnedLate = 0xC32E6C06820B3D2Eul;

        public static ulong RunWithLateInput(ColumnWorld world, Action step, Action resimulate) => Late(world, step, resimulate, true);

        public static ulong RunWithInputOnTime(ColumnWorld world, Action step) => Late(world, step, step, false);

        static ulong Late(ColumnWorld world, Action step, Action resimulate, bool late)
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

            var fire = ColumnAction.SendFrom(slots[61], slots[2], pack.Channel("Fire"), 40, 2);
            var gone = ColumnAction.Despawn(slots[9]);
            var channels = pack.ChannelNames.Count;
            var state = 0x2545F491u;
            for (var frame = 0; frame < Frames; frame++)
            {
                if (!late && frame == 15)
                {
                    world.Writer(0).Send(fire.Slot, fire.Source, fire.Channel, fire.Amount, fire.Hits);
                }

                if (!late && frame == 33)
                {
                    world.Despawn(gone.Slot);
                }

                if (late && frame == 20)
                {
                    world.Inputs(15).AddAction(fire);
                    resimulate();
                }

                if (late && frame == 36)
                {
                    world.Inputs(33).InsertAction(0, gone);
                    resimulate();
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
                    if ((state & 7u) == 0)
                    {
                        writer.Send(target, slots[(int)((state >> 9) % (uint)count)], channel, amount);
                    }
                    else
                    {
                        writer.Send(target, channel, amount);
                    }
                }

                step();
            }

            return world.Digest();
        }

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
                        writer.Send(target, slots[(int)((state >> 9) % (uint)count)], channel, amount, hits);
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
