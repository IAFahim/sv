using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;
using Sv.Compose;
using static Program;

// FF-1M — the roadmap's reference workload (issues/0058, Roadmap.md 2.2): 2^20 entities,
// 16 stats (12 base, 4 derived), 4 stat-bounded intrinsics, 2 timed modifiers per entity,
// 1 timeline, 4 event channels, and a busy frame of 128 changes (64 hits, 32 heals,
// 8 buffs, 8 expiries, 8 spawns, 8 despawns). Reports p50/p99 per frame, the memory
// bill (board, game, rollback snapshot, RSS) and keeps the run allocation-free.

internal struct HitEvent
{
    public int Slot;

    public int Amount;
}

internal struct HealEvent
{
    public int Slot;

    public int Amount;
}

internal struct BuffEvent
{
    public int Slot;

    public int Amount;
}

internal struct ExpiredEvent
{
    public int Count;
}

internal static unsafe class Ff
{
    internal const int Entities = 1 << 20;

    internal const int ChangesPerFrame = 128;

    internal const int EventsPerFrame = 64 + 32 + 8 + 1;

    internal static long Tick;

    internal static Clamp HpClamp;

    internal static Clamp ManaClamp;

    internal static Clamp StaminaClamp;

    internal static Clamp ShieldClamp;

    internal static float* AtkColumn;

    internal static int HitOffset(long tick, int capacity) => (int)(((uint)tick * 4096u) % (uint)(capacity - 64));

    internal static int HealOffset(long tick, int capacity) => (HitOffset(tick, capacity) + capacity / 2) % (capacity - 64);

    internal static int ReleasedSlot(long tick, int i) => (int)(((uint)tick * 114514u + (uint)i * 941u) % (uint)Entities);

    internal static bool IsReleased(long tick, int slot)
    {
        for (var i = 0; i < 8; i++)
        {
            if (ReleasedSlot(tick, i) == slot)
            {
                return true;
            }
        }

        return false;
    }

    internal static int BuffSlot(long tick, int i)
    {
        var slot = (int)(((uint)tick * 1_597_317u + (uint)i * 5077u) % (uint)Entities);
        while (IsReleased(tick, slot))
        {
            slot = (slot + 1) % Entities;
        }

        return slot;
    }

    internal static long ArmorExpiry(int slot) => 100 + (long)((ulong)(uint)slot * 2654435761ul % 262144ul);
}

internal unsafe struct FfWorld : IFrameWorld
{
    public int* Hp;

    public int* Mana;

    public int* Stamina;

    public int* Shield;

    public long* EventsDrained;

    public StatId MaxHp;

    public StatId MaxMana;

    public StatId MaxStamina;

    public StatId MaxShield;

    public StatId Armor;

    public StatId Haste;

    public StatId Atk;

    public StatId Defense;

    public void Reconcile(Game game)
    {
        var board = game.Board;
        for (var i = 0; i < 8; i++)
        {
            board.Release(Ff.ReleasedSlot(Ff.Tick, i));
        }

        for (var i = 0; i < 8; i++)
        {
            var slot = board.Acquire();
            Hp[slot] = 100;
            Mana[slot] = 50;
            Stamina[slot] = 60;
            Shield[slot] = 20;
            game.TimelineIds[slot] = (ushort)(slot & 1);
            game.TimelineClocks[slot] = 0;
        }
    }

    public void DispatchEvent(Game game, ushort typeId, void* payload)
    {
        (*EventsDrained)++;
    }

    public void RunIntrinsics(Game game)
    {
        var board = game.Board;
        var capacity = board.Capacity;
        board.RunIntrinsicsChunk(Hp, 2, Ff.HpClamp, 0, capacity, game.Applied);
        board.RunIntrinsicsChunk(Mana, 3, Ff.ManaClamp, 0, capacity, game.Applied);
        board.RunIntrinsicsChunk(Stamina, 1, Ff.StaminaClamp, 0, capacity, game.Applied);
        board.RunIntrinsicsChunk(Shield, 1, Ff.ShieldClamp, 0, capacity, game.Applied);
        var hitOffset = Ff.HitOffset(Ff.Tick, capacity);
        board.RunEnabledChunk(Hp, -25, Ff.HpClamp, ulong.MaxValue, hitOffset, game.Applied);
        var healOffset = Ff.HealOffset(Ff.Tick, capacity);
        board.RunEnabledChunk(Mana, 15, Ff.ManaClamp, 0xFFFF_FFFFul, healOffset, game.Applied);
    }

    public void RunConditions(Game game)
    {
        var board = game.Board;
        var tick = Ff.Tick;
        var capacity = board.Capacity;
        var expired = board.ExpireModifiers(tick);
        var buffs = game.Channel<BuffEvent>();
        for (var i = 0; i < 8; i++)
        {
            var slot = Ff.BuffSlot(tick, i);
            board.AddModifier(slot, (i & 1) == 0 ? Armor : Haste, new Modifier(ModifierKind.Multi, 1.5f), 0, tick + 600);
            buffs.Write(new BuffEvent { Slot = slot, Amount = 1 });
        }

        var hits = game.Channel<HitEvent>();
        var hitOffset = Ff.HitOffset(tick, capacity);
        for (var i = 0; i < 64; i++)
        {
            hits.Write(new HitEvent { Slot = hitOffset + i, Amount = 25 });
        }

        var heals = game.Channel<HealEvent>();
        var healOffset = Ff.HealOffset(Ff.Tick, capacity);
        for (var i = 0; i < 32; i++)
        {
            heals.Write(new HealEvent { Slot = healOffset + i, Amount = 15 });
        }

        game.Channel<ExpiredEvent>().Write(new ExpiredEvent { Count = expired });
        WalkChanged(board, Armor);
        WalkChanged(board, Haste);
        WalkChanged(board, Atk);
        WalkChanged(board, Defense);
        var applied = game.Applied;
        for (var i = 0; i < capacity; i++)
        {
            applied[i] = 0;
        }
    }

    static void WalkChanged(Board board, StatId stat)
    {
        var words = board.ChangedWords(stat);
        var count = (board.Capacity + 63) >> 6;
        long changed = 0;
        for (var w = 0; w < count; w++)
        {
            var word = words[w];
            while (word != 0)
            {
                changed++;
                word &= word - 1;
            }
        }

        Drain += changed;
    }

    public static long Drain;
}

internal unsafe struct FfTimeline : ITimelineStep
{
    public static bool Loop => true;

    public static void Apply(Game game, float[] effect)
    {
        var ids = game.TimelineIds;
        var clocks = game.TimelineClocks;
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] != 0)
            {
                effect[i] += (clocks[i] & 1023) * 0.001f;
            }
        }
    }

    public static void Transform(Game game, float[] effect)
    {
        var power = Ff.AtkColumn;
        for (var i = 0; i < effect.Length; i++)
        {
            effect[i] *= power[i];
        }
    }

    public static void Advance(Game game)
    {
        var ids = game.TimelineIds;
        var clocks = game.TimelineClocks;
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] != 0)
            {
                clocks[i] = (ushort)((clocks[i] + 1) & 1023);
            }
        }
    }
}

internal sealed unsafe class Ff1mBench : IDisposable
{
    readonly GameHost<EventLink<ExpiredEvent, EventLink<BuffEvent, EventLink<HealEvent, EventLink<HitEvent, Chain>>>>, TimelineLink<FfTimeline, Chain>, Chain, FfWorld> host;

    readonly int* hp;

    readonly int* mana;

    readonly int* stamina;

    readonly int* shield;

    readonly long* drained;

    readonly float*[] bound;

    readonly double[] samples;

    int sampled;

    Ff1mBench(GameHost<EventLink<ExpiredEvent, EventLink<BuffEvent, EventLink<HealEvent, EventLink<HitEvent, Chain>>>>, TimelineLink<FfTimeline, Chain>, Chain, FfWorld> host, int* hp, int* mana, int* stamina, int* shield, long* drained, float*[] bound)
    {
        this.host = host;
        this.hp = hp;
        this.mana = mana;
        this.stamina = stamina;
        this.shield = shield;
        this.drained = drained;
        this.bound = bound;
        samples = new double[65536];
    }

    internal static Ff1mBench Create()
    {
        var schema = SchemaBuilder.Create()
            .Stat("max_hp", 100f)
            .Stat("max_mana", 100f)
            .Stat("max_stamina", 100f)
            .Stat("max_shield", 50f)
            .Stat("armor", 10f)
            .Stat("move_speed", 6f)
            .Stat("strength", 10f)
            .Stat("crit", 5f)
            .Stat("regen_hp", 2f)
            .Stat("regen_mana", 3f)
            .Stat("bonus_armor", 0f)
            .Stat("haste", 0f)
            .Derived("atk", 1f, ("strength", 2f))
            .Derived("defense", 1f, ("armor", 1.5f))
            .Derived("power", 1f, ("strength", 0.5f))
            .Derived("crit_cap", 75f, ("crit", 1f))
            .Intrinsic("hp")
            .Intrinsic("mana")
            .Intrinsic("stamina")
            .Intrinsic("shield")
            .Build();
        var capacity = Ff.Entities;
        var hp = Allocator.New<int>(capacity);
        var mana = Allocator.New<int>(capacity);
        var stamina = Allocator.New<int>(capacity);
        var shield = Allocator.New<int>(capacity);
        var drained = (long*)NativeMemory.AllocZeroed(sizeof(long), sizeof(long));
        var world = new FfWorld
        {
            Hp = hp,
            Mana = mana,
            Stamina = stamina,
            Shield = shield,
            EventsDrained = drained,
            MaxHp = schema.Stat("max_hp"),
            MaxMana = schema.Stat("max_mana"),
            MaxStamina = schema.Stat("max_stamina"),
            MaxShield = schema.Stat("max_shield"),
            Armor = schema.Stat("armor"),
            Haste = schema.Stat("haste"),
            Atk = schema.Stat("atk"),
            Defense = schema.Stat("defense"),
        };
        var host = Game.Create(schema, capacity)
            .Event<HitEvent>()
            .Event<HealEvent>()
            .Event<BuffEvent>()
            .Event<ExpiredEvent>()
            .Timeline<FfTimeline>()
            .Build(world, InputState.Create(2, 1, 512));
        var board = host.Game.Board;
        for (var i = 0; i < capacity; i++)
        {
            board.Acquire();
        }

        var bound = new float*[]
        {
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
            Allocator.New<float>(capacity),
        };
        board.BindOutput(world.MaxHp, bound[0]);
        board.BindOutput(world.MaxMana, bound[1]);
        board.BindOutput(world.MaxStamina, bound[2]);
        board.BindOutput(world.MaxShield, bound[3]);
        board.BindOutput(world.Armor, bound[4]);
        board.BindOutput(schema.Stat("move_speed"), bound[5]);
        board.BindOutput(world.Atk, bound[6]);
        board.BindOutput(world.Defense, bound[7]);
        for (var i = 0; i < capacity; i++)
        {
            board.AddModifier(i, world.Armor, new Modifier(ModifierKind.Multi, 1.25f), 0, Ff.ArmorExpiry(i));
            board.AddModifier(i, world.Haste, new Modifier(ModifierKind.Multi, 1.1f), 0, 100 + (long)((ulong)(uint)i * 2246822519ul % 262144ul));
            hp[i] = 80;
            mana[i] = 40;
            stamina[i] = 60;
            shield[i] = 20;
            host.Game.TimelineIds[i] = (ushort)(i & 1);
        }

        board.Recalculate();
        Ff.HpClamp = Clamp.StatMirrorMax(board, world.MaxHp);
        Ff.ManaClamp = Clamp.StatMirrorMax(board, world.MaxMana);
        Ff.StaminaClamp = Clamp.StatMirrorMax(board, world.MaxStamina);
        Ff.ShieldClamp = Clamp.StatMirrorMax(board, world.MaxShield);
        Ff.AtkColumn = bound[6];
        return new Ff1mBench(host, hp, mana, stamina, shield, drained, bound);
    }

    static bool RowTouched(int row, int tick, int capacity)
    {
        if (Ff.IsReleased(tick, row))
        {
            return true;
        }

        var hit = Ff.HitOffset(tick, capacity);
        if (row >= hit && row < hit + 64)
        {
            return true;
        }

        var heal = Ff.HealOffset(tick, capacity);
        return row >= heal && row < heal + 32;
    }

    static bool BuffedThisTick(int slot, int tick)
    {
        for (var i = 0; i < 8; i++)
        {
            if (Ff.BuffSlot(tick, i) == slot)
            {
                return true;
            }
        }

        return false;
    }

    internal string Verify()
    {
        Ff.Tick = 0;
        FfWorld.Drain = 0;
        *drained = 0;
        var board = host.Game.Board;
        var capacity = board.Capacity;
        var probe = 3;
        while (RowTouched(probe, 1, capacity) || RowTouched(probe, 2, capacity))
        {
            probe += 7;
        }

        var buffSlot = Ff.BuffSlot(1, 0);
        var bi = 0;
        while (Ff.ArmorExpiry(buffSlot) <= 3000 || RowTouched(buffSlot, 1, capacity) || RowTouched(buffSlot, 2, capacity) || BuffedThisTick(buffSlot, 2))
        {
            bi += 2;
            if (bi > 14)
            {
                return "FAIL no clean armor buff slot";
            }

            buffSlot = Ff.BuffSlot(1, bi);
        }

        var hitRow = Ff.HitOffset(1, capacity);
        while (Ff.IsReleased(1, hitRow) || Ff.IsReleased(2, hitRow))
        {
            hitRow++;
        }

        for (var i = 0; i < capacity; i++)
        {
            hp[i] = 80;
            mana[i] = 40;
        }

        Ff.Tick = 0;
        BusyFrame();
        BusyFrame();
        if (*drained != Ff.EventsPerFrame)
        {
            return $"FAIL drained {*drained} != {Ff.EventsPerFrame}";
        }

        if (board.LiveCount != Ff.Entities)
        {
            return $"FAIL live {board.LiveCount} != {Ff.Entities}";
        }

        if (hp[probe] != 84 || mana[probe] != 46)
        {
            return $"FAIL probe hp {hp[probe]} != 84, mana {mana[probe]} != 46";
        }

        const int hitExpected = 80 + 2 - 25 + 2;
        if (hp[hitRow] != hitExpected)
        {
            return $"FAIL hit row hp {hp[hitRow]} != {hitExpected}";
        }

        var armor = BitConverter.SingleToUInt32Bits(bound[4][buffSlot]);
        if (armor != BitConverter.SingleToUInt32Bits(56.25f))
        {
            return $"FAIL armor fold {bound[4][buffSlot]} != 56.25";
        }

        return $"ok events={Ff.EventsPerFrame}/frame live={board.LiveCount}";
    }

    void BusyFrame()
    {
        Ff.Tick++;
        var axes = stackalloc uint[2];
        var buttons = stackalloc ulong[1];
        axes[0] = 0x3F800000u;
        axes[1] = (uint)Ff.Tick;
        buttons[0] = (ulong)(uint)Ff.Tick;
        host.Frame.Tick(0.016666668f, axes, buttons);
    }

    internal long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            var start = Stopwatch.GetTimestamp();
            BusyFrame();
            var ticks = Stopwatch.GetTimestamp() - start;
            samples[sampled % samples.Length] = ticks / (double)Stopwatch.Frequency * 1_000_000.0;
            sampled++;
            checksum += *drained + hp[it % Ff.Entities] + FfWorld.Drain;
        }

        return checksum;
    }

    internal (double P50, double P99) Percentiles()
    {
        var n = Math.Min(sampled, samples.Length);
        var copy = new double[n];
        Array.Copy(samples, copy, n);
        Array.Sort(copy);
        return (copy[n / 2], copy[(n * 99) / 100]);
    }

    internal string Memory()
    {
        var board = host.Game.Board;
        var capacity = board.Capacity;
        long snapshot;
        using (var probe = new SvState())
        {
            host.Game.SaveState(probe);
            snapshot = probe.Length;
        }

        var rss = RssKib();
        return $"board={board.MemoryBytes / 1048576.0:0} MiB game={capacity * 12L / 1048576.0:0} MiB" +
               $" snapshot={snapshot / 1048576.0:0} MiB (x8 window={snapshot * 8 / 1048576.0:0} MiB)" +
               $" rss={(rss < 0 ? "n/a" : $"{rss / 1024.0:0} MiB")} capacity={board.Capacity}";
    }

    static long RssKib()
    {
        if (!OperatingSystem.IsLinux())
        {
            return -1;
        }

        foreach (var line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                return long.Parse(line["VmRSS:".Length..].TrimEnd('k', 'B', ' ').Trim());
            }
        }

        return -1;
    }

    public void Dispose()
    {
        host.Dispose();
        NativeMemory.Free(hp);
        NativeMemory.Free(mana);
        NativeMemory.Free(stamina);
        NativeMemory.Free(shield);
        NativeMemory.Free(drained);
        foreach (var column in bound)
        {
            NativeMemory.Free(column);
        }
    }
}

internal static class Ff1m
{
    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "ff1m")
        {
            return 0;
        }

        using var bench = Ff1mBench.Create();
        var verify = bench.Verify();
        bench.Frame(16);
        var sw = Stopwatch.GetTimestamp();
        bench.Frame(8);
        var perCall = Math.Max((Stopwatch.GetTimestamp() - sw) / 8.0 / Stopwatch.Frequency, 1e-9);
        var iterations = Math.Max(8, (int)(ms / 3000.0 / perCall));
        for (var b = 0; b < 3; b++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var checksum = bench.Frame(iterations);
            if (GC.GetAllocatedBytesForCurrentThread() - before != 0 || checksum == long.MinValue)
            {
                verify = "FAIL allocated managed bytes on the warm frame";
            }
        }

        if (!verify.StartsWith("ok", StringComparison.Ordinal))
        {
            Console.WriteLine(Markdown ? $"| ff1m | FAIL: {verify} | — | — |" : $"ff1m                   FAIL: {verify}");
            return 1;
        }

        var (p50, p99) = bench.Percentiles();
        if (Markdown)
        {
            Console.WriteLine($"| ff1m | {p50:0.0} | p99 {p99:0.0} µs · {Ff.Entities} rows · Δ={Ff.ChangesPerFrame} | 0 B |");
        }
        else
        {
            Console.WriteLine($"ff1m                   {p50,9:0.0} us/frame p50  {p99,9:0.0} us/frame p99  ({Ff.Entities:N0} entities, Δ={Ff.ChangesPerFrame})");
            Console.WriteLine($"  {bench.Memory()}");
        }

        return 0;
    }
}
