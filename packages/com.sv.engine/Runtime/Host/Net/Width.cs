using System;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.Host.Net
{
    public enum Width : byte
    {
        Scalar = 0,
        Vector128 = 1,
        Vector256 = 2,
    }

    public static class Widths
    {
        public const string Override = "SV_WIDTH";

        public static Width Widest()
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                return Width.Vector256;
            }

            if (Vector128.IsHardwareAccelerated)
            {
                return Width.Vector128;
            }
#endif
            return Width.Scalar;
        }

        public static Width Choose(string? requested)
        {
            var widest = Widest();
            if (string.IsNullOrEmpty(requested))
            {
                return widest;
            }

            var wanted = string.Equals(requested, "scalar", StringComparison.OrdinalIgnoreCase) ? Width.Scalar
                : string.Equals(requested, "128", StringComparison.Ordinal) ? Width.Vector128
                : Width.Vector256;
            return wanted < widest ? wanted : widest;
        }

        public static Width FromEnvironment() => Choose(Environment.GetEnvironmentVariable(Override));
    }
}
