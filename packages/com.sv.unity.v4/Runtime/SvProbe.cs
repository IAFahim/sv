using System.Threading;
using Unity.Burst;

namespace Sv.Unity
{
    public static unsafe class SvProbe
    {
        public const int Unseen = 0;
        public const int Burst = 1;
        public const int Managed = 2;

        public static int ManagedRuns;

        public static void Mark(int* probe)
        {
            *probe = Burst;
            RanManaged(probe);
        }

        [BurstDiscard]
        static void RanManaged(int* probe)
        {
            *probe = Managed;
            Interlocked.Increment(ref ManagedRuns);
        }
    }
}
