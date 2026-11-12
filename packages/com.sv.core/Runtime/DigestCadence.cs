using System;
using System.Runtime.CompilerServices;

namespace Sv
{
    public readonly struct DigestCadence : IEquatable<DigestCadence>
    {
        public const int ReleaseEvery = 8;

        public DigestCadence(int every)
        {
            if (every < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(every), $"sv: digest cadence must be >= 1 tick, got {every}");
            }

            Every = every;
        }

        public int Every { get; }

        public static DigestCadence EveryTick => new DigestCadence(1);

        public static DigestCadence Release => new DigestCadence(ReleaseEvery);

#if DEBUG
        public static bool IsDebugBuild => true;
#else
        public static bool IsDebugBuild => false;
#endif

        public static DigestCadence Default => IsDebugBuild ? EveryTick : Release;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Due(long tick)
        {
            var every = Every < 1 ? 1 : Every;
            return every == 1 || tick % every == 0;
        }

        public long LastDue(long tick)
        {
            var every = Every < 1 ? 1 : Every;
            var rest = tick % every;
            return rest < 0 ? tick - rest - every : tick - rest;
        }

        public bool Equals(DigestCadence other) => Every == other.Every;

        public override bool Equals(object? obj) => obj is DigestCadence other && Equals(other);

        public override int GetHashCode() => Every;
    }
}
