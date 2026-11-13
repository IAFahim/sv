using System.Runtime.InteropServices;
using Sv;
using Sv.Host;
using Sv.Kernels.NorthStar3;
using Sv.Kernels.NorthStar3.Glue;
using Sv.Rules;
using Tl;
using Tl.Gen.Tlb;
using PackKernels = Sv.Kernels.NorthStar3.HostKernels;

namespace Sv.NorthStar3
{
    public readonly record struct Cause(long Frame, int Kind, int Slot, int Value);

    public readonly record struct Tick(long Frame, int Target, int Source, int Kind, int Channel, int Amount);

    public sealed unsafe class Rig : IDisposable
    {
        public const int Capacity = 1024;
        public const int Aim = 0;
        public const int Start = 1;
        public const int Hit = 2;
        public const int Heal = 3;
        public const ushort PoisonTimeline = 1;

        static byte[]? library;

        readonly TimelineWorld world;
        readonly PlaybackTable players;
        readonly KindStore kindStore;
        readonly TimelineRequests requests;
        readonly TlWorkers? tlWorkers;
        readonly PoisonOut[] poison;
        readonly BleedingOut[] bleeding;
        readonly int kindLane = (int)Intrinsic.Kind;
        readonly int targetLane = (int)Intrinsic.Target;

        public Rig(PackPlan plan, int workers)
        {
            Plan = plan;
            Host = new FrameLoop<PackKernels>(HostPack.From(plan), new HostOptions { Workers = workers, Parts = 64 });
            world = new TimelineWorld(Library());
            players = new PlaybackTable(Capacity);
            kindStore = new KindStore(world, players);
            requests = new TimelineRequests(world);
            tlWorkers = workers > 1 ? new TlWorkers(workers) : null;
            poison = GC.AllocateArray<PoisonOut>(Capacity, true);
            bleeding = GC.AllocateArray<BleedingOut>(Capacity, true);
        }

        public PackPlan Plan { get; }

        public FrameLoop<PackKernels> Host { get; }

        public List<Tick> Ticks { get; } = new List<Tick>();

        public int Template(string name)
        {
            for (var t = 0; t < Plan.Schema.Templates.Length; t++)
            {
                if (string.CompareOrdinal(Plan.Schema.Templates[t].Name, name) == 0)
                {
                    return t;
                }
            }

            throw new ArgumentException("no template " + name);
        }

        public int Spawn(string template) => Host.Spawn(Template(template));

        public void Apply(Cause cause)
        {
            if (cause.Kind == Aim)
            {
                Host.Send(cause.Slot, EffectKey.NoSource, 0, (int)Channel.Aim, cause.Value, 1, Origin.Direct);
            }
            else if (cause.Kind == Heal)
            {
                Host.Send(cause.Slot, EffectKey.NoSource, 0, (int)Channel.Healing, cause.Value, 1, Origin.Direct);
            }
            else if (cause.Kind == Hit)
            {
                Host.Send(cause.Slot, EffectKey.NoSource, 0, (int)Channel.Physical, cause.Value, 1, Origin.Direct);
            }
            else
            {
                players.Start(cause.Slot, (ushort)cause.Value);
            }
        }

        public ulong Step()
        {
            world.Tick(players, true);
            var system = new Tl.Generated.TlAuto_Sv_NorthStar3 { C0_PoisonOut = poison.AsSpan(0, Capacity), C1_BleedingOut = bleeding.AsSpan(0, Capacity) };
            if (tlWorkers != null)
            {
                system.Run(world, players, true, tlWorkers);
            }
            else
            {
                system.Run(world, players, true);
            }

            Collect(MemoryMarshal.Cast<PoisonOut, int>(poison.AsSpan()), (int)Channel.Poison);
            Collect(MemoryMarshal.Cast<BleedingOut, int>(bleeding.AsSpan()), (int)Channel.Bleeding);
            Host.Step();
            requests.Apply(players, Host.Events, Host.EventCount);
            return Host.LastDigest;
        }

        void Collect(Span<int> cells, int channel)
        {
            for (var slot = 0; slot < cells.Length; slot++)
            {
                if (cells[slot] != 0)
                {
                    var aimed = Host.Read(slot, targetLane);
                    Ticks.Add(new Tick(Host.Frame, aimed <= 0 ? slot : aimed - 1, slot, Math.Clamp(Host.Read(slot, kindLane), 0, 255), channel, cells[slot]));
                }
            }

            Outbox.Send(Host, cells, channel, kindLane, targetLane);
        }

        public int Health(int slot) => Host.Read(slot, (int)Intrinsic.Health);

        static byte[] Bytes<T>(T value) where T : unmanaged => MemoryMarshal.AsBytes(new Span<T>(ref value)).ToArray();

        static TypedTrack Track<T>(params (uint Start, uint End, T Clip)[] clips) where T : unmanaged
        {
            var track = new TypedTrack { Type = typeof(TlNoSettings<T>) };
            foreach (var (start, end, clip) in clips)
            {
                track.Clips.Add(new TypedClip { Type = typeof(T), Start = start, End = end, Data = Bytes(clip) });
            }

            return track;
        }

        public static byte[] Library()
        {
            if (library != null)
            {
                return library;
            }

            var timelines = new List<TypedTimeline>
            {
                new TypedTimeline { Name = "poison", Duration = 4, Tracks = { Track((0u, 4u, new PoisonClip { Amount = 3 })) } },
            };
            var manifest = LibraryBake.Manifest(timelines.Select(static t => t.Name!).ToList(), new List<string>(), 60);
            var kinds = LibraryBake.Kinds(new[] { typeof(PoisonClip).Assembly });
            var parts = new List<LibraryPart>();
            foreach (var timeline in timelines)
            {
                var tlb = LibraryBake.BakeTyped(timeline, manifest);
                parts.Add(new LibraryPart { Name = timeline.Name!, Tlb = tlb, Kinds = LibraryBake.KindPayload(timeline.Name!, tlb, kinds), Next = (ushort)LibraryAssembler.NextOf(tlb) });
            }

            library = LibraryAssembler.Assemble(parts, 60, LibrarySettings.Default);
            return library;
        }

        public void Dispose()
        {
            Host.Dispose();
            players.Dispose();
            tlWorkers?.Dispose();
        }
    }
}
