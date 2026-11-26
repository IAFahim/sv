using System;
using System.Collections.Generic;
using Sv.Host;
using UnityEngine;

namespace Sv.Unity
{
    public interface ISvWorld : IDisposable
    {
        bool IsDisposed { get; }

        long Frame { get; }

        ulong LastDigest { get; }

        int Spawn(int template);

        bool Kill(int slot);

        bool Send(int target, int source, int kind, int channel, int amount, int hits, Origin origin);

        void Modify(in StatOp op);

        void Step();

        int Read(int slot, int lane);

        bool Level(int slot, int raised);

        ulong Digest();

        int ProbeOf(FramePhase phase);

        void ClearProbes();

        SvFrame View();
    }

    public sealed class SvWorld<TKernels> : ISvWorld
        where TKernels : struct, IPackKernels
    {
        readonly SvJobs? jobs;
        FrameLoop<TKernels>? loop;

        public SvWorld(HostPack pack, HostOptions options, IPhaseRunner runner)
        {
            jobs = runner as SvJobs;
            loop = new FrameLoop<TKernels>(pack, options, runner);
            SvWorlds.Add(this);
        }

        public FrameLoop<TKernels> Loop => loop ?? throw new ObjectDisposedException(nameof(SvWorld<TKernels>));

        public bool IsDisposed => loop == null;

        public long Frame => Loop.Frame;

        public ulong LastDigest => Loop.LastDigest;

        public int Spawn(int template) => Loop.Spawn(template);

        public bool Kill(int slot) => Loop.Kill(slot);

        public bool Send(int target, int source, int kind, int channel, int amount, int hits, Origin origin) =>
            Loop.Send(target, source, kind, channel, amount, hits, origin);

        public void Modify(in StatOp op) => Loop.Modify(op);

        public void Step() => Loop.Step();

        public int Read(int slot, int lane) => Loop.Read(slot, lane);

        public bool Level(int slot, int raised) => Loop.Level(slot, raised);

        public ulong Digest() => Loop.Digest();

        public int ProbeOf(FramePhase phase) => jobs == null ? SvProbe.Unseen : jobs.ProbeOf(phase);

        public void ClearProbes() => jobs?.ClearProbes();

        public unsafe SvFrame View()
        {
            var running = Loop;
            return new SvFrame
            {
                World = running.World.View,
                Sender = running.Sender(0),
                Events = running.Events,
                EventCount = running.EventCount,
                Frame = running.Frame,
                LastDigest = running.LastDigest,
            };
        }

        public void Dispose()
        {
            var disposing = loop;
            if (disposing == null)
            {
                return;
            }

            loop = null;
            SvWorlds.Remove(this);
            disposing.Dispose();
        }
    }

    public static class SvWorlds
    {
        static readonly List<ISvWorld> Live = new List<ISvWorld>();

        public static int Count => Live.Count;

        internal static void Add(ISvWorld world) => Live.Add(world);

        internal static void Remove(ISvWorld world) => Live.Remove(world);

        public static int DisposeAll()
        {
            var disposed = Live.Count;
            while (Live.Count > 0)
            {
                Live[Live.Count - 1].Dispose();
            }

            SvProbe.ManagedRuns = 0;
            return disposed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DisposeBeforePlay() => DisposeAll();
    }
}
