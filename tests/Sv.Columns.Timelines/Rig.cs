using System.Runtime.InteropServices;
using Sv.Columns.Timelines.Glue;
using Tl;
using Tl.Gen.Tlb;

namespace Sv.Columns.Timelines
{
    public readonly record struct Cell(int Frame, int Slot, string Column, int Value);

    public interface ICellSource
    {
        void Write(int frame, ColumnWorld world, List<Cell> cells);

        void After(ColumnWorld world);
    }

    public sealed class Rig : IDisposable
    {
        public const int Capacity = 1024;
        public const string Card = "poison";
        public const string Fire = "Fire";
        public const string Healing = "Healing";
        public const string Aim = "Aim";

        static string? packText;

        readonly ICellSource source;

        public Rig(Func<CompiledPack, ICellSource> source)
        {
            Pack = PackJson.Read(PackText()).Compile();
            World = new ColumnWorld(Pack, Capacity, 1, 1);
            this.source = source(Pack);
        }

        public CompiledPack Pack { get; }

        public ColumnWorld World { get; }

        public ICellSource Source => source;

        public List<Cell> Cells { get; } = new List<Cell>();

        public int Frame { get; private set; }

        public static string Root
        {
            get
            {
                var folder = AppContext.BaseDirectory;
                while (folder != null && !Directory.Exists(Path.Combine(folder, "packages", "com.sv.columns.entities")))
                {
                    folder = Path.GetDirectoryName(folder);
                }

                return folder ?? throw new DirectoryNotFoundException("no packages/com.sv.columns.entities above " + AppContext.BaseDirectory);
            }
        }

        public static string PackPath => Path.Combine(Root, "packages", "com.sv.columns.entities", "Tests", "Glue", "Pack~", "arena-tl.json");

        public static string PackText() => packText ??= File.ReadAllText(PackPath);

        public int Spawn(string template) => World.Spawn(Pack.Template(template));

        public void Set(int slot, string lane, int value) => World.Set(slot, Pack.Lane(lane), value);

        public int Get(int slot, string lane) => World.Get(slot, Pack.Lane(lane));

        public ulong Step()
        {
            source.Write(Frame, World, Cells);
            World.Step();
            source.After(World);
            Frame++;
            return World.Digest();
        }

        public static void Apply(ColumnWorld world, CompiledPack pack, Cell cell)
        {
            if (cell.Column == Card)
            {
                world.Set(cell.Slot, pack.CardLane(Card), cell.Value);
                return;
            }

            var target = cell.Slot;
            if (cell.Column == Fire)
            {
                var aimed = world.Get(cell.Slot, pack.Lane(Aim));
                target = aimed <= 0 ? cell.Slot : aimed - 1;
            }

            world.Writer(0).SendFrom(cell.Slot, target, pack.Channel(cell.Column), cell.Value);
        }

        public void Dispose()
        {
            (source as IDisposable)?.Dispose();
            World.Dispose();
            Pack.Dispose();
        }
    }

    public sealed class TimelineCells : ICellSource, IDisposable
    {
        public const string Poison = "poison";
        public const string Burn = "burn";
        public const string Mend = "mend";
        public const int PoisonLevel = 3;
        public const int BurnAmount = 20;
        public const int MendAmount = 15;

        static byte[]? library;

        readonly CompiledPack pack;
        readonly TimelineWorld timelines;
        readonly PlaybackTable players;
        readonly KindStore kinds;
        readonly TlWorkers? workers;
        readonly PoisonCard[] cards;
        readonly FireOut[] fire;
        readonly HealingOut[] healing;

        public TimelineCells(CompiledPack pack, int workers)
        {
            this.pack = pack;
            timelines = new TimelineWorld(Library());
            players = new PlaybackTable(Rig.Capacity);
            kinds = new KindStore(timelines, players);
            Requests = new TimelineRequests(timelines, pack);
            this.workers = workers > 1 ? new TlWorkers(workers) : null;
            cards = GC.AllocateArray<PoisonCard>(Rig.Capacity, true);
            fire = GC.AllocateArray<FireOut>(Rig.Capacity, true);
            healing = GC.AllocateArray<HealingOut>(Rig.Capacity, true);
        }

        public TimelineRequests Requests { get; }

        public ushort Find(string timeline) => timelines.Find(timeline);

        public void Start(int slot, string timeline) => players.Start(slot, Find(timeline));

        public void Write(int frame, ColumnWorld world, List<Cell> cells)
        {
            timelines.Tick(players, true);
            var system = new Tl.Generated.TlAuto_Sv_Columns_Timelines { C0_PoisonCard = cards, C1_FireOut = fire, C2_HealingOut = healing };
            if (workers != null)
            {
                system.Run(timelines, players, true, workers);
            }
            else
            {
                system.Run(timelines, players, true);
            }

            Drain(frame, world, cells, MemoryMarshal.Cast<PoisonCard, int>(cards.AsSpan()), Rig.Card);
            Drain(frame, world, cells, MemoryMarshal.Cast<FireOut, int>(fire.AsSpan()), Rig.Fire);
            Drain(frame, world, cells, MemoryMarshal.Cast<HealingOut, int>(healing.AsSpan()), Rig.Healing);
        }

        public void After(ColumnWorld world) => Requests.Apply(players, world);

        void Drain(int frame, ColumnWorld world, List<Cell> cells, Span<int> values, string column)
        {
            for (var slot = 0; slot < values.Length; slot++)
            {
                if (values[slot] == 0)
                {
                    continue;
                }

                var cell = new Cell(frame, slot, column, values[slot]);
                cells.Add(cell);
                Rig.Apply(world, pack, cell);
                values[slot] = 0;
            }
        }

        static byte[] Bytes<T>(T value) where T : unmanaged => MemoryMarshal.AsBytes(new Span<T>(ref value)).ToArray();

        static TypedTrack Track<T>(uint start, uint end, T clip) where T : unmanaged
        {
            var track = new TypedTrack { Type = typeof(TlNoSettings<T>) };
            track.Clips.Add(new TypedClip { Type = typeof(T), Start = start, End = end, Data = Bytes(clip) });
            return track;
        }

        public static byte[] Library()
        {
            if (library != null)
            {
                return library;
            }

            var made = new List<TypedTimeline>
            {
                new TypedTimeline { Name = Burn, Duration = 4, Tracks = { Track(0u, 4u, new FireClip { Amount = BurnAmount }) } },
                new TypedTimeline { Name = Mend, Duration = 3, Tracks = { Track(0u, 3u, new HealingClip { Amount = MendAmount }) } },
                new TypedTimeline { Name = Poison, Duration = 4, Tracks = { Track(0u, 4u, new PoisonCardClip { Level = PoisonLevel }) } },
            };
            var manifest = LibraryBake.Manifest(made.Select(static t => t.Name!).ToList(), new List<string>(), 60);
            var kinds = LibraryBake.Kinds(new[] { typeof(PoisonCardClip).Assembly });
            var parts = new List<LibraryPart>();
            foreach (var timeline in made)
            {
                var tlb = LibraryBake.BakeTyped(timeline, manifest);
                parts.Add(new LibraryPart { Name = timeline.Name!, Tlb = tlb, Kinds = LibraryBake.KindPayload(timeline.Name!, tlb, kinds), Next = (ushort)LibraryAssembler.NextOf(tlb) });
            }

            library = LibraryAssembler.Assemble(parts, 60, LibrarySettings.Default);
            return library;
        }

        public void Dispose()
        {
            players.Dispose();
            workers?.Dispose();
        }
    }

    public sealed class HandCells : ICellSource
    {
        readonly CompiledPack pack;
        readonly List<Cell> script;

        public HandCells(CompiledPack pack, List<Cell> script)
        {
            this.pack = pack;
            this.script = script;
        }

        public void Write(int frame, ColumnWorld world, List<Cell> cells)
        {
            foreach (var cell in script)
            {
                if (cell.Frame == frame)
                {
                    cells.Add(cell);
                    Rig.Apply(world, pack, cell);
                }
            }
        }

        public void After(ColumnWorld world)
        {
        }
    }
}
