using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Sv.Columns.Entities.Tests
{
    public sealed class ColumnsTraffic : IDisposable
    {
        NativeArray<byte> modes;
        NativeArray<int> sources;
        NativeArray<int> targets;
        NativeArray<int> channels;
        NativeArray<long> amounts;
        NativeArray<int> hits;
        NativeArray<ulong> raws;
        NativeArray<int> probe;

        public ColumnsTraffic(int capacity)
        {
            modes = new NativeArray<byte>(capacity, Allocator.Persistent);
            sources = new NativeArray<int>(capacity, Allocator.Persistent);
            targets = new NativeArray<int>(capacity, Allocator.Persistent);
            channels = new NativeArray<int>(capacity, Allocator.Persistent);
            amounts = new NativeArray<long>(capacity, Allocator.Persistent);
            hits = new NativeArray<int>(capacity, Allocator.Persistent);
            raws = new NativeArray<ulong>(capacity, Allocator.Persistent);
            probe = new NativeArray<int>(1, Allocator.Persistent);
        }

        public int Count { get; private set; }

        public bool RanBurst => probe[0] == 1;

        public void Send(int target, int channel, long amount, int times) => Add(0, 0, target, channel, amount, times, 0);

        public void SendFrom(int source, int target, int channel, long amount, int times) => Add(1, source, target, channel, amount, times, 0);

        public void SendRecord(ulong record) => Add(2, 0, 0, 0, 0, 0, record);

        void Add(byte mode, int source, int target, int channel, long amount, int times, ulong raw)
        {
            var at = Count++;
            modes[at] = mode;
            sources[at] = source;
            targets[at] = target;
            channels[at] = channel;
            amounts[at] = amount;
            hits[at] = times;
            raws[at] = raw;
        }

        public JobHandle Schedule(ColumnsRecords.ParallelWriter sender, JobHandle dependsOn = default)
        {
            var handle = new SendJob
            {
                Sender = sender,
                Modes = modes,
                Sources = sources,
                Targets = targets,
                Channels = channels,
                Amounts = amounts,
                Hits = hits,
                Raws = raws,
                Probe = probe,
            }.Schedule(Count, 64, dependsOn);
            Count = 0;
            return handle;
        }

        public void Dispose()
        {
            modes.Dispose();
            sources.Dispose();
            targets.Dispose();
            channels.Dispose();
            amounts.Dispose();
            hits.Dispose();
            raws.Dispose();
            probe.Dispose();
        }

        [BurstDiscard]
        static void Managed(ref int burst) => burst = 0;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        struct SendJob : IJobParallelFor
        {
            public ColumnsRecords.ParallelWriter Sender;

            [ReadOnly]
            public NativeArray<byte> Modes;

            [ReadOnly]
            public NativeArray<int> Sources;

            [ReadOnly]
            public NativeArray<int> Targets;

            [ReadOnly]
            public NativeArray<int> Channels;

            [ReadOnly]
            public NativeArray<long> Amounts;

            [ReadOnly]
            public NativeArray<int> Hits;

            [ReadOnly]
            public NativeArray<ulong> Raws;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> Probe;

            public void Execute(int index)
            {
                if (index == 0)
                {
                    var burst = 1;
                    Managed(ref burst);
                    Probe[0] = burst;
                }

                switch (Modes[index])
                {
                    case 0:
                        Sender.Send(Targets[index], Channels[index], Amounts[index], Hits[index]);
                        break;
                    case 1:
                        Sender.SendFrom(Sources[index], Targets[index], Channels[index], Amounts[index], Hits[index]);
                        break;
                    default:
                        Sender.SendRecord(Raws[index]);
                        break;
                }
            }
        }
    }
}
