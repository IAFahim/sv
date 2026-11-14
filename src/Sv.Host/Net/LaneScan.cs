#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.Host.Net
{
    public static unsafe class LaneScan
    {
        public static ulong NonZero(Width width, int* lane)
        {
#if NET
            if (width == Width.Vector256)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)(~Vector256.Equals(Vector256.Load(lane + at), Vector256<int>.Zero)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }

            if (width == Width.Vector128)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 4)
                {
                    mask |= (ulong)(~Vector128.Equals(Vector128.Load(lane + at), Vector128<int>.Zero)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            return NonZeroScalar(lane);
        }

        public static ulong NonZeroScalar(int* lane)
        {
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = lane[row] != 0 ? (byte)1 : (byte)0;
            }

            return Mask.Pack(flags);
        }

        public static ulong Positive(Width width, int* lane)
        {
#if NET
            if (width == Width.Vector256)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.GreaterThan(Vector256.Load(lane + at), Vector256<int>.Zero).ExtractMostSignificantBits() << at;
                }

                return mask;
            }

            if (width == Width.Vector128)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 4)
                {
                    mask |= (ulong)Vector128.GreaterThan(Vector128.Load(lane + at), Vector128<int>.Zero).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            return PositiveScalar(lane);
        }

        public static ulong PositiveScalar(int* lane)
        {
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = lane[row] > 0 ? (byte)1 : (byte)0;
            }

            return Mask.Pack(flags);
        }

        public static ulong Differ(Width width, int* first, int* second)
        {
#if NET
            if (width == Width.Vector256)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)(~Vector256.Equals(Vector256.Load(first + at), Vector256.Load(second + at))).ExtractMostSignificantBits() << at;
                }

                return mask;
            }

            if (width == Width.Vector128)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 4)
                {
                    mask |= (ulong)(~Vector128.Equals(Vector128.Load(first + at), Vector128.Load(second + at))).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            return DifferScalar(first, second);
        }

        public static ulong DifferScalar(int* first, int* second)
        {
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] != second[row] ? (byte)1 : (byte)0;
            }

            return Mask.Pack(flags);
        }

        public static void Broadcast(Width width, int* lane, int value)
        {
#if NET
            if (width == Width.Vector256)
            {
                var filled = Vector256.Create(value);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    filled.Store(lane + at);
                }

                return;
            }

            if (width == Width.Vector128)
            {
                var filled = Vector128.Create(value);
                for (var at = 0; at < Lanes.Rows; at += 4)
                {
                    filled.Store(lane + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                lane[row] = value;
            }
        }
    }
}
