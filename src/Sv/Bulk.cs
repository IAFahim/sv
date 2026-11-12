#if NET
using System;
using System.Threading;

namespace Sv
{
    /// <summary>
    /// Bulk twin of <see cref="Kernel.Run(Column{int}, Window, Delta, Bounds, Column{int})"/>: the window is
    /// validated, partitioned into fixed contiguous slices, and executed by a persistent pool of
    /// ProcessorCount − 1 spin-parked workers plus the calling thread. Slices are disjoint and pure, so the
    /// result equals the sequential run regardless of scheduling (partition invariance). Reentrant calls
    /// throw; the pool is created once and stays for the process. A slice that throws (nothing on the
    /// validated path can, by construction) completes the round and the first failure is rethrown on the
    /// caller, so a fault surfaces deterministically instead of leaving a partially applied window.
    /// </summary>
    public static unsafe class Bulk
    {
        static readonly object Gate = new();

        static Pool? pool;

        static int busy;

        public static void Run(Column<int> value, Window window, Delta delta, Bounds bounds, Column<int> applied = default)
        {
            Validate(value, window, applied);
            var workers = Volatile.Read(ref pool);
            if (workers == null)
            {
                lock (Gate)
                {
                    workers = pool ??= new Pool();
                }
            }

            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("sv: Bulk.Run is not reentrant");
            }

            try
            {
                if (workers.Participants == 1)
                {
                    Kernel.Run(value, window, delta, bounds, applied);
                    return;
                }

                workers.Publish(value, window, delta, bounds, applied);
                workers.Arrive();
                try
                {
                    workers.Execute(0);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref workers.failure, ex, null);
                }
                finally
                {
                    workers.Arrive();
                }

                var fault = Interlocked.Exchange(ref workers.failure, null);
                if (fault != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        static void Validate(Column<int> value, Window window, Column<int> applied)
        {
            if (window.Words == null)
            {
                Guard.ChunkRange(window.Start, window.Count, value.Length);
                if (applied.Pointer != null && applied.Length < (long)window.Start + window.Count)
                {
                    throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + window.Count}");
                }

                return;
            }

            Guard.EnabledRange(window.Start, window.WordCount, value.Length);
            if (applied.Pointer != null && applied.Length < (long)window.Start + (long)window.WordCount * 64)
            {
                throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + (long)window.WordCount * 64}");
            }
        }

        sealed unsafe class Pool
        {
            internal readonly int Participants;

            readonly int[] partitions;

            int arriving;

            int sense = 1;

            long lastRound;

            internal Exception? failure;

            Column<int> value;

            Window window;

            Delta delta;

            Bounds bounds;

            Column<int> applied;

            internal Pool()
            {
                Participants = Math.Max(1, Environment.ProcessorCount);
                partitions = new int[Participants * 2];
                arriving = Participants;
                for (var i = 1; i < Participants; i++)
                {
                    var index = i;
                    var thread = new Thread(() => Loop(index))
                    {
                        IsBackground = true,
                        Name = $"sv-bulk-{i}",
                    };
                    thread.Start();
                }
            }

            void Loop(int index)
            {
                while (true)
                {
                    Arrive();
                    try
                    {
                        Execute(index);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref failure, ex, null);
                    }

                    Arrive();
                }
            }

            internal void Publish(Column<int> value, Window window, Delta delta, Bounds bounds, Column<int> applied)
            {
                this.value = value;
                this.window = window;
                this.delta = delta;
                this.bounds = bounds;
                this.applied = applied;
                if (window.Words == null)
                {
                    var rows = (window.Count + Participants - 1) / Participants;
                    for (var i = 0; i < Participants; i++)
                    {
                        partitions[i * 2] = window.Start + i * rows;
                        partitions[i * 2 + 1] = Math.Max(0, Math.Min(rows, window.Count - i * rows));
                    }

                    return;
                }

                var wordsPer = (window.WordCount + Participants - 1) / Participants;
                for (var i = 0; i < Participants; i++)
                {
                    var words = Math.Max(0, Math.Min(wordsPer, window.WordCount - i * wordsPer));
                    partitions[i * 2] = window.Start + i * wordsPer * 64;
                    partitions[i * 2 + 1] = words * 64;
                }
            }

            internal void Execute(int index)
            {
                var start = partitions[index * 2];
                var rows = partitions[index * 2 + 1];
                if (rows == 0)
                {
                    return;
                }

                Window slice = window.Words == null
                    ? Window.Range(start, rows)
                    : Window.Masked(start, window.Words + ((start - window.Start) >> 6), rows >> 6);
                Kernel.Run(value, slice, delta, bounds, applied);
            }

            internal void Arrive()
            {
                var captured = Volatile.Read(ref sense);
                if (Interlocked.Decrement(ref arriving) == 0)
                {
                    arriving = Participants;
                    Volatile.Write(ref sense, 1 - captured);
                    Volatile.Write(ref lastRound, Environment.TickCount64);
                    lock (Gate)
                    {
                        Monitor.PulseAll(Gate);
                    }
                }
                else
                {
                    var spins = 0;
                    while (Volatile.Read(ref sense) == captured)
                    {
                        if (Environment.TickCount64 - Volatile.Read(ref lastRound) < 5)
                        {
                            Thread.SpinWait(64);
                            if ((++spins & 15) == 0)
                            {
                                Thread.Yield();
                            }
                        }
                        else
                        {
                            lock (Gate)
                            {
                                if (Volatile.Read(ref sense) == captured)
                                {
                                    Monitor.Wait(Gate, 20);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
#endif
