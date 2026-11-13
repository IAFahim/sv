using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv.Host
{
    [StructLayout(LayoutKind.Sequential, Size = 128)]
    public struct PaddedCounter
    {
        public int Value;
    }

    public static class WorkRange
    {
        public const int MinimumItems = 16;

        public static int PartsFor(int items, int parts)
        {
            var most = (items + MinimumItems - 1) / MinimumItems;
            return parts < most ? parts : most;
        }

        public static void Of(int items, int part, int parts, out int from, out int to)
        {
            var used = PartsFor(items, parts);
            if (used < 1 || (uint)part >= (uint)used)
            {
                from = items;
                to = items;
                return;
            }

            from = (int)((long)items * part / used);
            to = (int)((long)items * (part + 1) / used);
        }
    }

    public sealed unsafe class Workers : IDisposable
    {
        public const double DefaultSpinMicroseconds = 50.0;

        readonly Thread[] threads;
        readonly ManualResetEventSlim[] wake;
        readonly int[] parked;
        readonly long spinTicks;
        delegate*<void*, int, void> body;
        void* state;
        int parts;
        PaddedCounter next;
        PaddedCounter finished;
        PaddedCounter generation;
        int stopping;
        ExceptionDispatchInfo? failure;

        public Workers(int count, double spinMicroseconds = DefaultSpinMicroseconds)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "sv: a worker pool holds at least the main thread");
            }

            Count = count;
            spinTicks = (long)(spinMicroseconds * Stopwatch.Frequency / 1_000_000.0);
            threads = new Thread[count - 1];
            wake = new ManualResetEventSlim[count - 1];
            parked = new int[count - 1];
            for (var index = 0; index < threads.Length; index++)
            {
                wake[index] = new ManualResetEventSlim(false, 0);
                var worker = index;
                threads[index] = new Thread(() => Loop(worker))
                {
                    IsBackground = true,
                    Name = "sv-worker-" + (worker + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                threads[index].Start();
            }
        }

        public int Count { get; }

        public void Run(delegate*<void*, int, void> work, void* workState, int partCount)
        {
            if (partCount <= 0)
            {
                return;
            }

            if (threads.Length == 0 || partCount == 1)
            {
                for (var part = 0; part < partCount; part++)
                {
                    work(workState, part);
                }

                return;
            }

            body = work;
            state = workState;
            parts = partCount;
            Volatile.Write(ref next.Value, 0);
            Volatile.Write(ref finished.Value, 0);
            Interlocked.Increment(ref generation.Value);
            for (var index = 0; index < threads.Length; index++)
            {
                if (Volatile.Read(ref parked[index]) != 0)
                {
                    wake[index].Set();
                }
            }

            Execute();
            var spinner = default(SpinWait);
            while (Volatile.Read(ref finished.Value) != threads.Length)
            {
                Spin(ref spinner);
            }

            body = null;
            state = null;
            var thrown = failure;
            if (thrown != null)
            {
                failure = null;
                thrown.Throw();
            }
        }

        void Execute()
        {
            var work = body;
            var workState = state;
            var count = parts;
            while (true)
            {
                var part = Interlocked.Increment(ref next.Value) - 1;
                if (part >= count)
                {
                    return;
                }

                try
                {
                    work(workState, part);
                }
                catch (Exception error)
                {
                    Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(error), null);
                }
            }
        }

        void Loop(int worker)
        {
            var seen = Volatile.Read(ref generation.Value);
            while (true)
            {
                seen = Await(worker, seen);
                if (Volatile.Read(ref stopping) != 0)
                {
                    return;
                }

                Execute();
                Interlocked.Increment(ref finished.Value);
            }
        }

        int Await(int worker, int seen)
        {
            var started = Stopwatch.GetTimestamp();
            var spinner = default(SpinWait);
            while (true)
            {
                var now = Volatile.Read(ref generation.Value);
                if (now != seen)
                {
                    return now;
                }

                if (Stopwatch.GetTimestamp() - started < spinTicks)
                {
                    Spin(ref spinner);
                    continue;
                }

                wake[worker].Reset();
                Interlocked.Exchange(ref parked[worker], 1);
                now = Volatile.Read(ref generation.Value);
                if (now == seen)
                {
                    wake[worker].Wait();
                }

                Volatile.Write(ref parked[worker], 0);
                started = Stopwatch.GetTimestamp();
                spinner = default(SpinWait);
            }
        }

        static void Spin(ref SpinWait spinner)
        {
#if NET
            spinner.SpinOnce(-1);
#else
            spinner.SpinOnce();
#endif
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref stopping, 1) != 0)
            {
                return;
            }

            Interlocked.Increment(ref generation.Value);
            for (var index = 0; index < threads.Length; index++)
            {
                wake[index].Set();
            }

            for (var index = 0; index < threads.Length; index++)
            {
                threads[index].Join();
                wake[index].Dispose();
            }
        }
    }
}
