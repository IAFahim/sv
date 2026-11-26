using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv.Columns;

namespace Sv.Host
{
    public sealed unsafe class ColumnRunner : IDisposable
    {
        readonly ColumnWorld world;
        readonly Workers workers;
        GCHandle self;

        public ColumnRunner(ColumnWorld world, Workers workers)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.workers = workers ?? throw new ArgumentNullException(nameof(workers));
            self = GCHandle.Alloc(this);
        }

        public ColumnWorld World => world;

        public double ReduceMilliseconds { get; private set; }

        public double ResolveMilliseconds { get; private set; }

        public void Step()
        {
            var state = (void*)GCHandle.ToIntPtr(self);
            var start = Stopwatch.GetTimestamp();
            world.BeginFrame();
            workers.Run(&ReducePart, state, world.Writers);
            var reduced = Stopwatch.GetTimestamp();
            workers.Run(&ResolvePart, state, world.ResolveParts);
            world.EndFrame();
            var resolved = Stopwatch.GetTimestamp();
            ReduceMilliseconds = (reduced - start) * 1000.0 / Stopwatch.Frequency;
            ResolveMilliseconds = (resolved - reduced) * 1000.0 / Stopwatch.Frequency;
        }

        public void Resimulate()
        {
            world.BeginResimulate();
            while (world.Resimulating)
            {
                Step();
            }
        }

        static ColumnRunner Of(void* state) => (ColumnRunner)GCHandle.FromIntPtr((IntPtr)state).Target!;

        static void ReducePart(void* state, int part) => Of(state).world.Reduce(part);

        static void ResolvePart(void* state, int part) => Of(state).world.Resolve(part);

        public void Dispose()
        {
            if (self.IsAllocated)
            {
                self.Free();
            }
        }
    }
}
