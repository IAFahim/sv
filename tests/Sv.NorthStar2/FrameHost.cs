using System.Runtime.InteropServices;
using Tl;
using Tl.Gen.Tlb;

namespace Sv.NorthStar2
{
    public enum CommandKind : byte { Start = 0, AddModifier = 1, RemoveSource = 2, Send = 3, Local = 4 }

    public readonly struct Command
    {
        public readonly CommandKind Kind;
        public readonly int Slot;
        public readonly int Amount;
        public readonly int Source;
        public readonly ushort Stat;
        public readonly ModifierKind Modifier;
        public readonly float Value;
        public readonly long ExpiresAt;

        Command(CommandKind kind, int slot, int amount, int source, ushort stat, ModifierKind modifier, float value, long expiresAt)
        {
            Kind = kind;
            Slot = slot;
            Amount = amount;
            Source = source;
            Stat = stat;
            Modifier = modifier;
            Value = value;
            ExpiresAt = expiresAt;
        }

        public static Command Start(int slot, ushort timeline) => new Command(CommandKind.Start, slot, timeline, 0, 0, default, 0f, 0);

        public static Command Add(int slot, ushort stat, ModifierKind kind, float value, int source, long expiresAt = long.MaxValue) => new Command(CommandKind.AddModifier, slot, 0, source, stat, kind, value, expiresAt);

        public static Command Remove(int slot, int source) => new Command(CommandKind.RemoveSource, slot, 0, source, 0, default, 0f, 0);

        public static Command Send(int slot, int amount) => new Command(CommandKind.Send, slot, amount, 0, 0, default, 0f, 0);

        public static Command Local(int slot, int amount) => new Command(CommandKind.Local, slot, amount, 0, 0, default, 0f, 0);
    }

    public struct LowHp
    {
        public int Slot;
        public int Entered;
    }

    struct LowHpMaker : IEventMaker<LowHp>
    {
        public LowHp Make(int row, int value) => new LowHp { Slot = row, Entered = value };
    }

    public struct StageTimes
    {
        public long Main;
        public long Scatter;
        public long Fold;
        public long Kinds;
        public long Send;
        public long Derive;
        public long Digest;
    }

    public sealed unsafe class FrameHost : IDisposable
    {
        public const int ColumnHp = 1;
        public const int ColumnDelta = 2;
        public const int ColumnMaxHp = 3;
        public const int ColumnArmor = 4;
        public const int ColumnLow = 5;
        public const int ColumnInbox = 6;
        public const int ColumnOutgoing = 8;
        public const ushort Brawl = 1;
        public const ushort Guard = 2;

        static byte[]? library;

        public readonly int Rows;
        readonly int words;
        readonly int workers;
        readonly int capacity;
        readonly Schema schema;
        readonly Board board;
        readonly StatId maxHp;
        readonly StatId armor;
        readonly TimelineWorld world;
        readonly PlaybackTable players;
        readonly KindStore kindStore;
        readonly TlWorkers? tlWorkers;
        readonly HpDelta[] delta;
        readonly Outgoing[] outgoing;
        readonly ArmorPulse[] pulse;
        readonly ushort[] timelineOf;
        readonly int* hp;
        readonly int* floor;
        readonly int* ceiling;
        readonly float* maxHpHost;
        readonly float* armorHost;
        readonly int* targets;
        readonly ulong* changed;
        readonly ulong* low;
        readonly ulong* edges;
        readonly InboxBuffer inbox;
        readonly EventStreams streams;
        readonly Channel<LowHp> lowHp;
        readonly Channel<DenseEvent> dense;
        readonly EventDerive derive;
        readonly StatOp[] ops;
        readonly StatOp[] scratch;
        readonly StatOp[] next;
        readonly StatWrite[] writes;
        readonly ParallelOptions parallel;
        int nextCount;
        long tick;
        StageTimes times;

        readonly InboxLanes? lanes;

        public FrameHost(int rows, int workers, int eventCapacity = EventDerive.DefaultCapacity, int crossRowPercent = -1, bool useLanes = false, int laneCells = InboxLanes.DefaultCells)
        {
            Rows = rows;
            words = (rows + 63) >> 6;
            this.workers = workers;
            capacity = words << 6;
            parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
            schema = SchemaBuilder.Create().Stat("max_hp", 100f).Stat("armor", 10f).Build();
            maxHp = schema.Stat("max_hp");
            armor = schema.Stat("armor");
            board = Board.Create(schema, rows);
            board.BindSink(maxHp, true);
            board.BindSink(armor, true);
            for (var i = 0; i < rows; i++)
            {
                board.Acquire();
            }

            world = new TimelineWorld(Library());
            players = new PlaybackTable(rows);
            kindStore = new KindStore(world, players);
            tlWorkers = workers > 1 ? new TlWorkers(workers) : null;
            delta = GC.AllocateArray<HpDelta>(capacity, true);
            outgoing = GC.AllocateArray<Outgoing>(capacity, true);
            pulse = GC.AllocateArray<ArmorPulse>(capacity, true);
            timelineOf = new ushort[rows];
            hp = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            floor = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            ceiling = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            maxHpHost = (float*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(float));
            armorHost = (float*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(float));
            targets = (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));
            changed = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            low = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            edges = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            inbox = new InboxBuffer(capacity);
            lanes = useLanes ? new InboxLanes(inbox, workers, laneCells) : null;
            streams = EventStreams.Create(1, 1 << 16);
            lowHp = streams.CreateChannel<LowHp>();
            dense = streams.CreateChannel<DenseEvent>();
            derive = new EventDerive(lowHp.Id, eventCapacity);
            ops = new StatOp[rows * 2 + 4096];
            scratch = new StatOp[ops.Length];
            next = new StatOp[rows * 2];
            writes = new StatWrite[1024];
            for (var r = 0; r < rows; r++)
            {
                hp[r] = 95 - (r % 7) * 10;
                var spread = (long)r * 7 + 13;
                targets[r] = crossRowPercent < 0 ? (int)(spread % rows) : (int)((ulong)(r * 2654435761u) % 100) < crossRowPercent ? (int)(spread % rows) : r;
            }
        }

        public long Tick => tick;

        public StageTimes Times => times;

        public int Hp(int slot) => hp[slot];

        public float MaxHpHost(int slot) => maxHpHost[slot];

        public float ArmorHost(int slot) => armorHost[slot];

        public int Target(int slot) => targets[slot];

        public bool IsLow(int slot) => (low[slot >> 6] & (1ul << (slot & 63))) != 0;

        public ushort TimelineOf(int slot) => timelineOf[slot];

        public static byte[] Library()
        {
            if (library != null)
            {
                return library;
            }

            var timelines = new List<TypedTimeline>
            {
                new TypedTimeline
                {
                    Name = "brawl", Duration = 3,
                    Tracks =
                    {
                        Track((0u, 1u, new HitClip { Amount = 20 })),
                        Track((0u, 1u, new HealClip { Amount = 10 })),
                        Track((1u, 3u, new PoisonClip { Amount = 1 })),
                        Track((1u, 2u, new StrikeClip { Amount = 7 })),
                    },
                },
                new TypedTimeline
                {
                    Name = "guard", Duration = 3,
                    Tracks =
                    {
                        Track((0u, 2u, new GuardClip { Armor = 5 })),
                        Track((0u, 3u, new PoisonClip { Amount = 2 })),
                    },
                },
            };
            var manifest = LibraryBake.Manifest(timelines.Select(static t => t.Name!).ToList(), new List<string>(), 60);
            var kinds = LibraryBake.Kinds(new[] { typeof(HitClip).Assembly });
            var parts = new List<LibraryPart>();
            foreach (var timeline in timelines)
            {
                var tlb = LibraryBake.BakeTyped(timeline, manifest);
                parts.Add(new LibraryPart { Name = timeline.Name!, Tlb = tlb, Kinds = LibraryBake.KindPayload(timeline.Name!, tlb, kinds), Next = (ushort)LibraryAssembler.NextOf(tlb) });
            }

            library = LibraryAssembler.Assemble(parts, 60, LibrarySettings.Default);
            return library;
        }

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

        public ulong Step(IReadOnlyList<Command> journal, int shuffle = 0)
        {
            var clock = System.Diagnostics.Stopwatch.GetTimestamp();
            var random = shuffle == 0 ? null : new Random(shuffle * 7919 + (int)tick);
            var order = Order(journal.Count, random);
            var count = 0;
            for (var i = 0; i < nextCount; i++)
            {
                ops[count++] = next[i];
            }

            nextCount = 0;
            var inboxCells = inbox.Pointer;
            for (var k = 0; k < order.Length; k++)
            {
                var command = journal[order[k]];
                switch (command.Kind)
                {
                    case CommandKind.Start:
                        players.Start(command.Slot, (ushort)command.Amount);
                        timelineOf[command.Slot] = (ushort)command.Amount;
                        break;
                    case CommandKind.AddModifier:
                        ops[count++] = StatOp.Add(command.Slot, schema.Stat(schema.Stats[command.Stat].Name), command.Modifier, command.Value, command.Source, command.ExpiresAt);
                        break;
                    case CommandKind.RemoveSource:
                        ops[count++] = StatOp.RemoveSource(command.Slot, command.Source);
                        break;
                    case CommandKind.Send:
                        Inbox.Send(inboxCells, command.Slot, command.Amount);
                        break;
                    case CommandKind.Local:
                        delta[command.Slot].Value = unchecked(delta[command.Slot].Value + command.Amount);
                        break;
                }
            }

            board.ExpireModifiers(tick);
            var batch = ops.AsSpan(0, count);
            StatOps.Canonicalize(batch, scratch);
            board.Apply(batch);
            board.Recalculate();
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Main += t1 - clock;
            Scatter();
            var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Scatter += t2 - t1;
            FoldAll(random);
            var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Fold += t3 - t2;
            world.Tick(players, true);
            var system = new Tl.Generated.TlAuto_Sv_NorthStar2 { C0_HpDelta = delta.AsSpan(0, Rows), C1_Outgoing = outgoing.AsSpan(0, Rows), C2_ArmorPulse = pulse.AsSpan(0, Rows) };
            if (tlWorkers != null)
            {
                system.Run(world, players, true, tlWorkers);
            }
            else
            {
                system.Run(world, players, true);
            }

            var t4 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Kinds += t4 - t3;
            SendAll(random);
            var t5 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Send += t5 - t4;
            var maker = new LowHpMaker();
            derive.Derive(streams, lowHp, dense, tick, edges, low, words, ref maker);
            DerivePulses();
            var t6 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Derive += t6 - t5;
            streams.Swap();
            var digest = Digest();
            times.Digest += System.Diagnostics.Stopwatch.GetTimestamp() - t6;
            tick++;
            return digest;
        }

        static int[] Order(int count, Random? random)
        {
            var order = new int[count];
            for (var i = 0; i < count; i++)
            {
                order[i] = i;
            }

            if (random != null)
            {
                for (var i = count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
            }

            return order;
        }

        void Scatter()
        {
            while (true)
            {
                var taken = board.TakeBroadcast(writes);
                if (taken == 0)
                {
                    return;
                }

                for (var i = 0; i < taken; i++)
                {
                    var w = writes[i];
                    var bits = w.Bits;
                    var value = *(float*)&bits;
                    if (w.Stat == maxHp.Index)
                    {
                        maxHpHost[w.Slot] = value;
                        ceiling[w.Slot] = Clamp.BoundOf(value);
                    }
                    else if (w.Stat == armor.Index)
                    {
                        armorHost[w.Slot] = value;
                    }
                }
            }
        }

        (int Start, int Count) Partition(int part)
        {
            var first = (int)((long)part * words / workers);
            var last = (int)((long)(part + 1) * words / workers);
            var start = first << 6;
            var end = Math.Min(Rows, last << 6);
            return (start, Math.Max(0, end - start));
        }

        void FoldAll(Random? random)
        {
            derive.Begin();
            var parts = Order(workers, random);
            if (random != null || workers == 1)
            {
                foreach (var part in parts)
                {
                    FoldPart(part);
                }

                return;
            }

            Parallel.For(0, workers, parallel, FoldPart);
        }

        void FoldPart(int part)
        {
            var (start, count) = Partition(part);
            if (count == 0)
            {
                return;
            }

            fixed (HpDelta* d = delta)
            {
                var first = start >> 6;
                var wordCount = (count + 63) >> 6;
                Kernel.Fold(new Column<int>(hp, capacity), Window.Range(start, count), new Column<int>((int*)d, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed + first, wordCount), inbox.Column);
                long flips = 0;
                for (var w = first; w < first + wordCount; w++)
                {
                    var rows = Math.Min(64, Rows - (w << 6));
                    ulong enter = 0;
                    ulong stay = 0;
                    for (var r = 0; r < rows; r++)
                    {
                        var slot = (w << 6) + r;
                        enter |= (Condition.Below(hp[slot], ceiling[slot], 3, 10) ? 1ul : 0ul) << r;
                        stay |= (Condition.Below(hp[slot], ceiling[slot], 4, 10) ? 1ul : 0ul) << r;
                    }

                    var state = low[w];
                    var nextState = Condition.Next(state, enter, stay);
                    edges[w] = Condition.Edges(state, nextState);
                    low[w] = nextState;
                    flips += Bits.PopCount(edges[w]);
                }

                derive.AddCount(flips);
            }
        }

        void SendAll(Random? random)
        {
            var parts = Order(workers, random);
            if (random != null || workers == 1)
            {
                foreach (var part in parts)
                {
                    SendPart(part, random);
                }

                return;
            }

            Parallel.For(0, workers, parallel, part => SendPart(part, null));
        }

        void SendPart(int part, Random? random)
        {
            var (start, count) = Partition(part);
            var cells = inbox.Pointer;
            var lane = lanes == null ? null : lanes.Lane(part);
            var rows = random == null ? null : Order(count, random);
            for (var i = 0; i < count; i++)
            {
                var row = start + (rows == null ? i : rows[i]);
                var amount = outgoing[row].Value;
                if (amount != 0)
                {
                    if (lane != null)
                    {
                        lane->Send(targets[row], amount);
                    }
                    else
                    {
                        Inbox.Send(cells, targets[row], amount);
                    }

                    outgoing[row].Value = 0;
                }
            }

            if (lane != null)
            {
                lane->Flush();
            }
        }

        void DerivePulses()
        {
            for (var row = 0; row < Rows; row++)
            {
                var value = pulse[row].Value;
                if (value == 0)
                {
                    continue;
                }

                var source = 0x40000000 | (timelineOf[row] << 8);
                next[nextCount++] = value > 0 ? StatOp.Add(row, armor, ModifierKind.Added, value, source) : StatOp.RemoveSource(row, source);
                pulse[row].Value = 0;
            }
        }

        ulong Digest()
        {
            ulong state = 0;
            var cells = inbox.Pointer;
            unchecked
            {
                for (var slot = 0; slot < Rows; slot++)
                {
                    state += CellDigest.Key(ColumnHp, slot, (uint)hp[slot]);
                    state += CellDigest.Key(ColumnDelta, slot, (uint)delta[slot].Value);
                    var m = maxHpHost[slot];
                    var a = armorHost[slot];
                    state += CellDigest.Key(ColumnMaxHp, slot, *(uint*)&m);
                    state += CellDigest.Key(ColumnArmor, slot, *(uint*)&a);
                    state += CellDigest.Key(ColumnLow, slot, (uint)((low[slot >> 6] >> (slot & 63)) & 1ul));
                    state += CellDigest.Key(ColumnInbox, slot, (uint)cells[slot]);
                    state += CellDigest.Key(ColumnInbox + 1, slot, (uint)(cells[slot] >> 32));
                    state += CellDigest.Key(ColumnOutgoing, slot, (uint)outgoing[slot].Value);
                }

                var events = 0xCBF29CE484222325ul;
                while (streams.TryReadNext(out var type, out var payload, out var size))
                {
                    events = (events ^ type) * 0x100000001B3ul;
                    for (var i = 0; i < size; i += 4)
                    {
                        events = (events ^ *(uint*)((byte*)payload + i)) * 0x100000001B3ul;
                    }
                }

                var pending = (ulong)nextCount;
                for (var i = 0; i < nextCount; i++)
                {
                    var op = next[i];
                    pending = (pending ^ (ulong)op.Slot ^ ((ulong)op.ValueBits << 32) ^ ((ulong)op.Op << 62)) * 0x100000001B3ul;
                }

                return Mix(Mix(Mix(state, events), board.Digest), pending);
            }
        }

        static ulong Mix(ulong a, ulong b)
        {
            unchecked
            {
                var h = a ^ (b * 0x9E3779B97F4A7C15ul);
                h ^= h >> 31;
                h *= 0xBF58476D1CE4E5B9ul;
                return h ^ (h >> 29);
            }
        }

        public void Dispose()
        {
            derive.Dispose();
            streams.Dispose();
            lanes?.Dispose();
            inbox.Dispose();
            tlWorkers?.Dispose();
            kindStore.Dispose();
            players.Dispose();
            world.Dispose();
            board.Dispose();
            NativeMemory.Free(hp);
            NativeMemory.Free(floor);
            NativeMemory.Free(ceiling);
            NativeMemory.Free(maxHpHost);
            NativeMemory.Free(armorHost);
            NativeMemory.Free(targets);
            NativeMemory.Free(changed);
            NativeMemory.Free(low);
            NativeMemory.Free(edges);
        }
    }
}
