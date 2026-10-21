using System;
using System.Runtime.InteropServices;
using Sv;
using Sv.Frent;
using Frent;

// Guard-page fuzz child (roadmap issues/0058, atom 0.2). Every window API is driven
// against columns that end (and, where underflow matters, start) flush against a
// PROT_NONE page, so any read or write past the offered memory kills this process —
// the parent test reads the exit status. `--list` prints `id|expected-today` for the
// parent; `--case <id>` runs one case and prints `ok` or `threw:<Type>` on success.
// Expected-today encodes the KNOWN defects (F1/F2/F3/F23); atom 1.1 flips every
// expectation to ok/threw and the same table proves the fixes.

internal static unsafe class Program
{
    public struct Mana
    {
        public int Value;
    }

    const int ProtNone = 0;

    const int ProtReadWrite = 3;

    const int MapPrivate = 0x02;

    const int MapAnonymous = 0x20;

    [DllImport("libc")]
    static extern IntPtr mmap(IntPtr addr, ulong length, int prot, int flags, int fd, long offset);

    [DllImport("libc")]
    static extern int mprotect(IntPtr addr, ulong length, int prot);

    [DllImport("libc")]
    static extern int prctl(int option, ulong arg2);

    [DllImport("libc")]
    static extern int setrlimit(int resource, in RLimit limit);

    [StructLayout(LayoutKind.Sequential)]
    struct RLimit
    {
        public ulong Current;

        public ulong Maximum;
    }

    // The guard-page hits are intentional SIGSEGVs; without this every crash pins a
    // core dump to the system journal (the CLR fault handler turns them into SIGABRT).
    static void NoCoreDumps()
    {
        setrlimit(4 /* RLIMIT_CORE */, new RLimit { Current = 0, Maximum = 0 });
        prctl(4 /* PR_SET_DUMPABLE */, 0);
    }

    static int Main(string[] args)
    {
        NoCoreDumps();
        if (args.Length == 1 && args[0] == "--list")
        {
            foreach (var (id, expected) in Cases.All())
            {
                Console.WriteLine($"{id}|{expected}");
            }

            return 0;
        }

        if (args.Length != 2 || args[0] != "--case")
        {
            Console.Error.WriteLine("usage: Sv.Fuzz --list | --case <id>");
            return 64;
        }

        Run(args[1]);
        return 0;
    }

    static void Run(string id)
    {
        string outcome;
        try
        {
            outcome = Cases.Run(id);
        }
        catch (Exception ex)
        {
            outcome = $"threw:{ex.GetType().Name}";
        }

        Console.WriteLine(outcome);
    }

    /// <summary>
    /// A region of <paramref name="bytes"/> readable/writable memory with a PROT_NONE
    /// page on both sides. endFlush places the array's last byte against the trailing
    /// guard (writes past the end crash); !endFlush places the first byte against the
    /// leading guard (negative indices crash).
    /// </summary>
    internal static byte* Guarded(int bytes, bool endFlush)
    {
        var page = Environment.SystemPageSize;
        var span = (bytes + page - 1) / page * page;
        var total = (ulong)(span + 2 * page);
        var raw = mmap(IntPtr.Zero, total, ProtReadWrite, MapPrivate | MapAnonymous, -1, 0);
        if (raw == new IntPtr(-1))
        {
            throw new InvalidOperationException("sv.fuzz: mmap failed");
        }

        var basePtr = (byte*)raw;
        mprotect(raw, (ulong)page, ProtNone);
        mprotect(new IntPtr(basePtr + page + span), (ulong)page, ProtNone);
        return endFlush ? basePtr + page + span - bytes : basePtr + page;
    }

    internal static int* IntsEndFlush(int count) => (int*)Guarded(count * sizeof(int), endFlush: true);

    internal static ulong* UlongsStartFlush(int count) => (ulong*)Guarded(count * sizeof(ulong), endFlush: false);

    static Span<int> SpanOf(int* pointer, int length) => new(pointer, length);

    static SvSlot[] Slots(int start, int count)
    {
        var slots = new SvSlot[count];
        for (var i = 0; i < count; i++)
        {
            slots[i] = new SvSlot(start + i);
        }

        return slots;
    }

    static class Cases
    {
        internal static (string Id, string Expected)[] All()
        {
            return new[]
            {
                ("board-uniform-ok", "ok"),
                ("board-uniform-past-capacity", "threw"),
                ("board-uniform-count-negative", "threw"),
                ("board-uniform-short-applied", "crash"),
                ("board-varying-short-delta", "crash"),
                ("board-enabled-words-overrun", "crash"),
                ("board-enabled-words-past-capacity", "threw"),
                ("board-enabled-wordcount-zero", "threw"),
                ("board-span-twin-short-applied", "threw"),
                ("board-f23-chunk-overflow", "crash"),
                ("board-f23-enabled-overflow", "crash"),
                ("seam-runchunk-ok", "ok"),
                ("seam-runchunk-short-applied", "crash"),
                ("seam-runchunk-short-deltas", "crash"),
                ("seam-enabled-ok-multiword", "ok"),
                ("seam-enabled-tail-policed", "threw"),
                ("seam-enabled-interior-past-chunk", "crash"),
                ("seam-enabled-applied-window-overrun", "crash"),
                ("seam-enabled-offset-outside", "threw"),
                ("population-enabled-ok", "ok"),
                ("population-enabled-wordcount-zero", "crash"),
                ("population-enabled-wordcount-negative", "crash"),
            };
        }

        internal static string Run(string id)
        {
            var schema = SchemaBuilder.Create().Intrinsic("mana").Build();
            using var board = Board.Create(schema, 1024);
            for (var i = 0; i < 1024; i++)
            {
                board.Acquire();
            }

            var clamp = Clamp.Constant(0, 900_000);
            switch (id)
            {
                case "board-uniform-ok":
                {
                    var value = IntsEndFlush(1024);
                    var applied = IntsEndFlush(1024);
                    board.RunIntrinsicsChunk(value, 7, clamp, 0, 64, applied);
                    return "ok";
                }

                case "board-uniform-past-capacity":
                {
                    var value = IntsEndFlush(1024);
                    board.RunIntrinsicsChunk(value, 7, clamp, 960, 128, null);
                    return "ok";
                }

                case "board-uniform-count-negative":
                {
                    var value = IntsEndFlush(1024);
                    board.RunIntrinsicsChunk(value, 7, clamp, 0, -1, null);
                    return "ok";
                }

                case "board-uniform-short-applied":
                {
                    var value = IntsEndFlush(64);
                    var applied = IntsEndFlush(32);
                    board.RunIntrinsicsChunk(value, 7, clamp, 0, 64, applied);
                    return "ok";
                }

                case "board-varying-short-delta":
                {
                    var value = IntsEndFlush(64);
                    var delta = IntsEndFlush(32);
                    board.RunIntrinsicsChunk(value, delta, clamp, 0, 64, null);
                    return "ok";
                }

                case "board-enabled-words-overrun":
                {
                    var value = IntsEndFlush(64);
                    var words = UlongsStartFlush(3);
                    words[0] = 0;
                    words[1] = 1;
                    words[2] = 0;
                    board.RunEnabledChunk(value, 7, clamp, words, 3, 0, null);
                    return "ok";
                }

                case "board-enabled-words-past-capacity":
                {
                    var value = IntsEndFlush(1024);
                    var words = UlongsStartFlush(2);
                    words[0] = 1;
                    words[1] = 1;
                    board.RunEnabledChunk(value, 7, clamp, words, 2, 960, null);
                    return "ok";
                }

                case "board-enabled-wordcount-zero":
                {
                    var value = IntsEndFlush(1024);
                    var words = UlongsStartFlush(1);
                    words[0] = 1;
                    board.RunEnabledChunk(value, 7, clamp, words, 0, 0, null);
                    return "ok";
                }

                case "board-span-twin-short-applied":
                {
                    var value = IntsEndFlush(64);
                    var applied = IntsEndFlush(64);
                    var words = UlongsStartFlush(2);
                    words[0] = ulong.MaxValue;
                    words[1] = 0;
                    board.RunEnabledChunk(SpanOf(value, 64), 7, clamp, words, 2, 0, SpanOf(applied, 64));
                    return "ok";
                }

                case "board-f23-chunk-overflow":
                {
                    var value = IntsEndFlush(64);
                    board.RunIntrinsicsChunk(value, 7, clamp, int.MaxValue - 10, 20, null);
                    return "ok";
                }

                case "board-f23-enabled-overflow":
                {
                    var value = IntsEndFlush(64);
                    var words = UlongsStartFlush(1);
                    words[0] = 1;
                    board.RunEnabledChunk(value, 7, clamp, words, 1, int.MaxValue - 63, null);
                    return "ok";
                }

                case "seam-runchunk-ok":
                {
                    var rows = IntsEndFlush(64);
                    var applied = IntsEndFlush(64);
                    ChunkSeam.RunChunk(board, SpanOf(rows, 64), Slots(0, 64), 7, clamp, SpanOf(applied, 64));
                    return "ok";
                }

                case "seam-runchunk-short-applied":
                {
                    var rows = IntsEndFlush(64);
                    var applied = IntsEndFlush(32);
                    ChunkSeam.RunChunk(board, SpanOf(rows, 64), Slots(0, 64), 7, clamp, SpanOf(applied, 32));
                    return "ok";
                }

                case "seam-runchunk-short-deltas":
                {
                    var rows = IntsEndFlush(64);
                    var deltas = IntsEndFlush(32);
                    ChunkSeam.RunChunk(board, SpanOf(rows, 64), SpanOf(deltas, 32), Slots(0, 64), clamp, Span<int>.Empty);
                    return "ok";
                }

                case "seam-enabled-ok-multiword":
                {
                    var rows = IntsEndFlush(128);
                    var applied = IntsEndFlush(128);
                    var words = UlongsStartFlush(2);
                    words[0] = ulong.MaxValue;
                    words[1] = 0x0000_FFFF;
                    ChunkSeam.RunEnabledChunk(board, SpanOf(rows, 128), Slots(0, 128), words, 2, 0, 7, clamp, SpanOf(applied, 128));
                    return "ok";
                }

                case "seam-enabled-tail-policed":
                {
                    var rows = IntsEndFlush(64);
                    var words = UlongsStartFlush(2);
                    words[0] = 0;
                    words[1] = 1;
                    ChunkSeam.RunEnabledChunk(board, SpanOf(rows, 64), Slots(64, 64), words, 2, 64, 7, clamp, Span<int>.Empty);
                    return "ok";
                }

                case "seam-enabled-interior-past-chunk":
                {
                    var rows = IntsEndFlush(64);
                    var words = UlongsStartFlush(3);
                    words[0] = 0;
                    words[1] = 1;
                    words[2] = 0;
                    ChunkSeam.RunEnabledChunk(board, SpanOf(rows, 64), Slots(0, 64), words, 3, 0, 7, clamp, Span<int>.Empty);
                    return "ok";
                }

                case "seam-enabled-applied-window-overrun":
                {
                    var rows = IntsEndFlush(64);
                    var applied = IntsEndFlush(64);
                    var words = UlongsStartFlush(2);
                    words[0] = ulong.MaxValue;
                    words[1] = 0;
                    ChunkSeam.RunEnabledChunk(board, SpanOf(rows, 64), Slots(64, 64), words, 2, 64, 7, clamp, SpanOf(applied, 64));
                    return "ok";
                }

                case "seam-enabled-offset-outside":
                {
                    var rows = IntsEndFlush(64);
                    var words = UlongsStartFlush(1);
                    words[0] = 1;
                    ChunkSeam.RunEnabledChunk(board, SpanOf(rows, 64), Slots(64, 64), words, 1, 0, 7, clamp, Span<int>.Empty);
                    return "ok";
                }

                case "population-enabled-ok":
                {
                    using var world = new World();
                    using var population = new Population(world, schema, 512);
                    for (var i = 0; i < 128; i++)
                    {
                        population.Bind(world.Create(new Mana { Value = 40 }));
                    }

                    var words = UlongsStartFlush(2);
                    words[0] = 1;
                    words[1] = 0;
                    foreach (var window in population.Chunks<Mana>())
                    {
                        var applied = IntsEndFlush(64);
                        window.ApplyEnabled(population.Board, words, 1, 0, 7, clamp, SpanOf(applied, 64));
                        break;
                    }

                    return "ok";
                }

                case "population-enabled-wordcount-zero":
                {
                    using var world = new World();
                    using var population = new Population(world, schema, 512);
                    for (var i = 0; i < 128; i++)
                    {
                        population.Bind(world.Create(new Mana { Value = 40 }));
                    }

                    var words = UlongsStartFlush(1);
                    words[0] = 1;
                    foreach (var window in population.Chunks<Mana>())
                    {
                        window.ApplyEnabled(population.Board, words, 0, 0, 7, clamp, Span<int>.Empty);
                        break;
                    }

                    return "ok";
                }

                case "population-enabled-wordcount-negative":
                {
                    using var world = new World();
                    using var population = new Population(world, schema, 512);
                    for (var i = 0; i < 128; i++)
                    {
                        population.Bind(world.Create(new Mana { Value = 40 }));
                    }

                    var words = UlongsStartFlush(1);
                    words[0] = 1;
                    foreach (var window in population.Chunks<Mana>())
                    {
                        window.ApplyEnabled(population.Board, words, -1, 0, 7, clamp, Span<int>.Empty);
                        break;
                    }

                    return "ok";
                }

                default:
                    throw new ArgumentException($"sv.fuzz: unknown case {id}");
            }
        }
    }
}
