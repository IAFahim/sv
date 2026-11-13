namespace Sv.Unity.Tests
{
    public interface ISvTwinHost
    {
        void AddDelta(int row, int intrinsic, int amount);

        void Send(int row, int amount);

        void Enqueue(StatOp op);

        void Frame();

        ulong Digest();
    }

    public static class SvTwinScenario
    {
        public const int Rows = 300;
        public const int Ticks = 40;

        static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15ul;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        static int Range(ref ulong state, int low, int high) => low + (int)(Next(ref state) % (ulong)(high - low));

        public static ulong Run(ISvTwinHost host, StatId maxHp)
        {
            var state = 0x7714EEDul;
            var combined = 0xCBF29CE484222325ul;
            host.Frame();
            for (var tick = 0; tick < Ticks; tick++)
            {
                for (var row = 0; row < Rows; row++)
                {
                    var r = Next(ref state);
                    if ((r & 3) == 0)
                    {
                        host.AddDelta(row, 0, Range(ref state, -45, 40));
                    }

                    if ((r & 4) != 0)
                    {
                        host.AddDelta(row, 1, Range(ref state, -30, 31));
                    }
                }

                var sends = Range(ref state, 0, 60);
                for (var s = 0; s < sends; s++)
                {
                    host.Send(Range(ref state, 0, Rows), Range(ref state, -25, 26));
                }

                if (tick % 5 == 0)
                {
                    var row = Range(ref state, 0, Rows);
                    host.Enqueue(StatOp.Add(row, maxHp, ModifierKind.Added, Range(ref state, -60, 60), 1000 + tick));
                }

                if (tick % 7 == 3)
                {
                    host.Enqueue(StatOp.RemoveSource(Range(ref state, 0, Rows), 1000 + tick - 3));
                }

                host.Frame();
                unchecked
                {
                    combined = (combined ^ host.Digest()) * 0x100000001B3ul;
                }
            }

            return combined;
        }
    }
}
