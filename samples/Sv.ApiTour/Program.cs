using System.Diagnostics;
using Hello.Sv;
using Sv;
using Sv.Host;
using Sv.Rules;
using Ref = Sv.Rules.Reference;

unsafe
{
    Section("1 create, spawn, send, step, read");
    using (var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 1024 }))
    {
        var hero = host.Spawn((int)Template.hero);
        var knight = host.Spawn((int)Template.knight);
        var goblin = host.Spawn((int)Template.goblin);
        Console.WriteLine($"slots: hero {hero} knight {knight} goblin {goblin}   (a template fills its own 64-entity chunk, so slots start at 0, 64, 128)");
        Console.WriteLine($"before: hero health {host.Read(hero, (int)Intrinsic.Health)}  knight shield {host.Read(knight, (int)Intrinsic.Shield)}  MaxHealth(stat, raw) {host.Read(hero, (int)Stat.MaxHealth)}");
        host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
        host.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30, hits: 2);
        Console.WriteLine($"frame before Step: {host.Frame}");
        host.Step();
        Console.WriteLine($"frame after Step: {host.Frame}  hero health {host.Read(hero, (int)Intrinsic.Health)}  knight health {host.Read(knight, (int)Intrinsic.Health)} shield {host.Read(knight, (int)Intrinsic.Shield)}");
        Console.WriteLine($"LastDigest {host.LastDigest:X16}  Digest() {host.Digest():X16}");
    }

    Section("2 events: edges this frame, level now");
    using (var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 }))
    {
        var hero = host.Spawn((int)Template.hero);
        host.Send(hero, EffectKey.NoSource, 0, (int)Channel.Physical, 75);
        host.Step();
        for (var at = 0; at < host.EventCount; at++)
        {
            var change = host.Events[at];
            Console.WriteLine($"event {(Event)change.Event} slot {change.Slot} {(change.Entered ? "entered" : "left")}  Level now: {host.Level(change.Slot, change.Event)}");
        }

        host.Send(hero, EffectKey.NoSource, 0, (int)Channel.Healing, 50);
        host.Step();
        for (var at = 0; at < host.EventCount; at++)
        {
            var change = host.Events[at];
            Console.WriteLine($"event {(Event)change.Event} slot {change.Slot} {(change.Entered ? "entered" : "left")}  Level now: {host.Level(change.Slot, change.Event)}");
        }
    }

    Section("3 stat ops: a modifier with a source, removed by source");
    using (var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 }))
    {
        var hero = host.Spawn((int)Template.hero);
        host.Step();
        Console.WriteLine($"MaxHealth raw: {host.Read(hero, (int)Stat.MaxHealth)}");
        host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Added, 5000, source: 7));
        host.Step();
        Console.WriteLine($"after Added +5000 (source 7): {host.Read(hero, (int)Stat.MaxHealth)}");
        host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Multi, 5000, source: 8));
        host.Step();
        Console.WriteLine($"after Multi +5000 (source 8): {host.Read(hero, (int)Stat.MaxHealth)}");
        host.Modify(StatOp.RemoveSource(hero, 7));
        host.Step();
        Console.WriteLine($"after RemoveSource 7:         {host.Read(hero, (int)Stat.MaxHealth)}");
    }

    Section("3b stat shapes and layers");
    foreach (var (shape, value) in new[] { (ShapeKind.Added, 20), (ShapeKind.Additive, 5000), (ShapeKind.Multi, 5000), (ShapeKind.Cap, 120) })
    {
        using var shapeHost = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 256 });
        var subject = shapeHost.Spawn((int)Template.hero);
        shapeHost.Step();
        shapeHost.Modify(StatOp.Add(subject, (int)Stat.MaxHealth, shape, value, source: 1));
        shapeHost.Step();
        Console.WriteLine($"{shape,-9} {value,5}: MaxHealth 100 -> {shapeHost.Read(subject, (int)Stat.MaxHealth)}");
    }

    using (var layerHost = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 256 }))
    {
        var subject = layerHost.Spawn((int)Template.hero);
        layerHost.Step();
        layerHost.Modify(StatOp.Add(subject, (int)Stat.MaxHealth, ShapeKind.Added, 50, source: 1, layer: StatLayer.Core));
        layerHost.Modify(StatOp.Add(subject, (int)Stat.MaxHealth, ShapeKind.Multi, 10000, source: 2, layer: StatLayer.External));
        layerHost.Step();
        Console.WriteLine($"Core Added 50 then External Multi 10000: MaxHealth 100 -> {layerHost.Read(subject, (int)Stat.MaxHealth)}");
    }

    Section("4 rewind and replay");
    {
        ulong Run(Action<FrameLoop<HostKernels>, int, int> afterNine)
        {
            using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
            var hero = host.Spawn((int)Template.hero);
            var goblin = host.Spawn((int)Template.goblin);
            for (var frame = 0; frame < 9; frame++)
            {
                host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 3);
                host.Step();
            }

            afterNine(host, hero, goblin);
            return host.Digest();
        }

        var straight = Run((host, hero, goblin) => { });
        var rewound = Run((host, hero, goblin) =>
        {
            var epoch = host.World.EpochAtOrBefore(4);
            Console.WriteLine($"Epoch {host.World.Epoch}, oldest in ring {host.World.OldestEpoch}, rewinding to epoch {epoch} (state at the start of frame {host.World.Journal.BeginOf(epoch)})");
            Console.WriteLine($"Rewind returned {host.Rewind(epoch)}, Frame is now {host.Frame}");
            for (var frame = host.Frame; frame < 9; frame++)
            {
                host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 3);
                host.Step();
            }
        });
        Console.WriteLine($"straight digest {straight:X16}   rewound and replayed {rewound:X16}   equal: {straight == rewound}");
        using var refusals = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
        refusals.Spawn((int)Template.hero);
        for (var frame = 0; frame < 9; frame++)
        {
            refusals.Step();
        }

        Console.WriteLine($"ring holds epochs {refusals.World.OldestEpoch}..{refusals.World.Epoch}; Rewind(oldest - 1) {refusals.Rewind(refusals.World.OldestEpoch - 1)}, Rewind(current + 1) {refusals.Rewind(refusals.World.Epoch + 1)}, Rewind(oldest) {refusals.Rewind(refusals.World.OldestEpoch)}");
    }

    Section("5 late input: a record that arrives after its frame");
    {
        ulong Straight()
        {
            using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
            var hero = host.Spawn((int)Template.hero);
            var goblin = host.Spawn((int)Template.goblin);
            for (var frame = 0; frame < 6; frame++)
            {
                host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 3);
                if (frame == 2)
                {
                    host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 11);
                }

                host.Step();
            }

            return host.Digest();
        }

        using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
        var hero = host.Spawn((int)Template.hero);
        var goblin = host.Spawn((int)Template.goblin);
        for (var frame = 0; frame < 6; frame++)
        {
            host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 3);
            host.Step();
        }

        var late = new LoggedRecord(EffectKey.Pack(hero, goblin, (byte)Kind.Goblin, (byte)Channel.Physical, Origin.Direct, 0), 11, 1);
        var placedAt = host.Late(2, &late, 1);
        var resolved = host.Resolve();
        Console.WriteLine($"Late(2, ...) placed it at frame {placedAt}; Resolve {resolved}; ReplayFaults {host.ReplayFaults}");
        Console.WriteLine($"digest after Resolve {host.Digest():X16}   a straight run that had it at frame 2 {Straight():X16}   equal: {host.Digest() == Straight()}");
    }

    Section("6 the digest does not depend on workers, parts, storage");
    {
        ulong Digest(int workers, int parts, StoragePolicy policy)
        {
            using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { Workers = workers, Parts = parts, Policy = policy, EntityCapacity = 4096 });
            var slots = new int[300];
            for (var at = 0; at < slots.Length; at++)
            {
                slots[at] = host.Spawn(at % 2 == 0 ? (int)Template.hero : (int)Template.knight);
            }

            var goblin = host.Spawn((int)Template.goblin);
            for (var frame = 0; frame < 5; frame++)
            {
                for (var at = 0; at < slots.Length; at += 3)
                {
                    host.Send(slots[at], goblin, (int)Kind.Goblin, (int)Channel.Physical, 7 + at % 5);
                }

                host.Step();
            }

            return host.Digest();
        }

        foreach (var (workers, parts, policy) in new[] { (1, 64, StoragePolicy.Shared), (4, 64, StoragePolicy.Shared), (11, 16, StoragePolicy.Shared), (4, 64, StoragePolicy.Dense) })
        {
            Console.WriteLine($"workers {workers,2} parts {parts,2} {policy,-6} digest {Digest(workers, parts, policy):X16}");
        }
    }

    Section("7 sending from several threads");
    {
        using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024, ExternalWriters = 4 });
        var hero = host.Spawn((int)Template.hero);
        var goblin = host.Spawn((int)Template.goblin);
        host.Step();
        var threads = new Thread[4];
        for (var index = 0; index < threads.Length; index++)
        {
            var mine = index;
            threads[index] = new Thread(() =>
            {
                var writer = host.Sender(mine);
                for (var at = 0; at < 10; at++)
                {
                    writer->Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 1, 1);
                }
            });
            threads[index].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        host.Step();
        Console.WriteLine($"4 threads x 10 sends of 1 Physical: hero health {host.Read(hero, (int)Intrinsic.Health)} (100 - 40)");

        Exception? refused = null;
        host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 1);
        var second = new Thread(() =>
        {
            try
            {
                host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 1);
            }
            catch (Exception error)
            {
                refused = error;
            }
        });
        second.Start();
        second.Join();
        Console.WriteLine($"FrameLoop.Send from a second thread in one frame: {refused?.GetType().Name}: {refused?.Message}");
    }

    Section("8 what a frame costs: phase marks");
    {
        using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
        var hero = host.Spawn((int)Template.hero);
        for (var warm = 0; warm < 20; warm++)
        {
            host.Send(hero, EffectKey.NoSource, 0, (int)Channel.Healing, 1);
            host.Step();
        }

        var marks = host.Marks;
        string[] names = { "Begin", "Sealed", "Logged", "Stats", "Collected", "Decided", "Committed", "Evented", "Digested", "Ended" };
        var line = new List<string>();
        for (var at = 1; at < FrameMarks.Count; at++)
        {
            line.Add($"{names[at]} {(marks[at] - marks[at - 1]) * 1_000_000.0 / Stopwatch.Frequency:F0}us");
        }

        Console.WriteLine("last frame, microseconds per phase: " + string.Join(", ", line));
        Console.WriteLine($"raw arena grows {host.Raw.Grows}, external drops {host.ExternalDropped}, input log dropped {host.Log.DroppedRecords}");
    }

    Section("9 a pack is data: compile, hash, diagnostics");
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pack");
        string File(string name) => System.IO.File.ReadAllText(Path.Combine(dir, name));
        var source = new PackSource(File("values.sv.json"), File("rules.sv.json"), File("cards.sv.json"), File("templates.sv.json"));
        var compiled = PackCompiler.Compile(source);
        Console.WriteLine($"Compile ok {compiled.Ok}, PackHash {PackHash.Of(PackReader.Read(source).Value!):X16}, generated Layout.PackHash {Layout.PackHash:X16}");

        var bad = new PackSource(File("values.sv.json"), File("rules.sv.json").Replace("\"id\":3,", "\"id\":-3,"), File("cards.sv.json"), File("templates.sv.json"));
        var refused = PackCompiler.Compile(bad);
        Console.WriteLine($"negative rule id: ok {refused.Ok}; {string.Join("; ", refused.Diagnostics.Select(d => d.ToString()))}");
    }

    Section("10 the oracle: the slow reference interpreter against the compiled kernels");
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pack");
        string File(string name) => System.IO.File.ReadAllText(Path.Combine(dir, name));
        var plan = PackCompiler.Compile(new PackSource(File("values.sv.json"), File("rules.sv.json"), File("cards.sv.json"), File("templates.sv.json"))).Value!;
        var interpreter = new Ref.Interpreter(plan);
        var state = interpreter.NewWorld(256);
        var first = new Ref.FrameInputs();
        int IdOf(string name) => plan.Schema.Templates.First(template => template.Name == name).Id;
        first.Spawn(0, IdOf("hero"));
        first.Spawn(64, IdOf("knight"));
        first.Spawn(128, IdOf("goblin"));
        first.Send(0, 128, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, RecordOrigin.Direct);
        first.Send(64, 128, (int)Kind.Goblin, (int)Channel.Physical, 30, 1, RecordOrigin.Direct);
        state = interpreter.Step(state, first).State;
        var healthLane = plan.Schema.Intrinsics.First(row => row.Name == "Health").Id;
        var shieldLane = plan.Schema.Intrinsics.First(row => row.Name == "Shield").Id;
        Console.WriteLine($"reference: hero health {state.Intrinsic(0, healthLane)}, knight health {state.Intrinsic(64, healthLane)} shield {state.Intrinsic(64, shieldLane)}");

        using var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 256 });
        var hero = host.Spawn((int)Template.hero);
        var knight = host.Spawn((int)Template.knight);
        var goblin = host.Spawn((int)Template.goblin);
        host.Send(hero, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
        host.Send(knight, goblin, (int)Kind.Goblin, (int)Channel.Physical, 30);
        host.Step();
        Console.WriteLine($"compiled:  hero health {host.Read(hero, (int)Intrinsic.Health)}, knight health {host.Read(knight, (int)Intrinsic.Health)} shield {host.Read(knight, (int)Intrinsic.Shield)}");
    }

    Section("11 misuse fails loudly");
    {
        var host = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 1024 });
        var hero = host.Spawn((int)Template.hero);
        try { host.Send(hero, EffectKey.NoSource, 300, 0, 5); } catch (Exception error) { Console.WriteLine($"{error.GetType().Name}: {error.Message}"); }
        host.Dispose();
        try { host.Step(); } catch (Exception error) { Console.WriteLine($"{error.GetType().Name}: {error.Message}"); }
        using var small = new FrameLoop<HostKernels>(PackData.Create(), new HostOptions { EntityCapacity = 64 });
        for (var at = 0; at < 64; at++)
        {
            small.Spawn((int)Template.hero);
        }

        try { small.Spawn((int)Template.hero); } catch (Exception error) { Console.WriteLine($"{error.GetType().Name}: {error.Message}"); }
    }
}

static void Section(string title) => Console.WriteLine($"\n=== {title}");
