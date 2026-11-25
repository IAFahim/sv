using System;
using System.Runtime.InteropServices;
using Unity.Jobs;

namespace Sv.Columns.Unity
{
    public sealed class ColumnStepper : IDisposable
    {
        readonly ColumnWorld world;
        GCHandle handle;
        JobHandle pending;

        public ColumnStepper(ColumnWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            handle = GCHandle.Alloc(world);
        }

        public ColumnWorld World => world;

        public JobHandle Schedule(JobHandle dependsOn = default)
        {
            if (!handle.IsAllocated)
            {
                throw new ObjectDisposedException(nameof(ColumnStepper));
            }

            pending.Complete();
            var state = GCHandle.ToIntPtr(handle);
            var begun = new BeginJob { State = state }.Schedule(dependsOn);
            var reduced = new ReduceJob { State = state }.Schedule(world.Writers, 1, begun);
            var resolved = new ResolveJob { State = state }.Schedule(world.ResolveParts, 1, reduced);
            pending = new EndJob { State = state }.Schedule(resolved);
            return pending;
        }

        public void Step() => Schedule().Complete();

        public void Complete() => pending.Complete();

        public void Dispose()
        {
            pending.Complete();
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        static ColumnWorld Of(IntPtr state) => (ColumnWorld)GCHandle.FromIntPtr(state).Target!;

        struct BeginJob : IJob
        {
            public IntPtr State;

            public void Execute() => Of(State).BeginFrame();
        }

        struct ReduceJob : IJobParallelFor
        {
            public IntPtr State;

            public void Execute(int index) => Of(State).Reduce(index);
        }

        struct ResolveJob : IJobParallelFor
        {
            public IntPtr State;

            public void Execute(int index) => Of(State).Resolve(index);
        }

        struct EndJob : IJob
        {
            public IntPtr State;

            public void Execute() => Of(State).EndFrame();
        }
    }
}
