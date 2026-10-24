// Sv.Editor batch measurement harness (issue 0114, test tooling — managed freely).
// Boots once via `Unity -batchmode -nographics -quit -executeMethod Sv.Editor.SvEditorBatch.Run`,
// builds the FF-1M program (issues/0058 shape) over a real Unity Entities world, then runs the
// clause-7 v2 frame order (UnityPopulation.Reconcile at structural boundaries -> per-chunk
// O(1) SvRange intrinsics visits through Unity's enabled masks -> Board.ExpireModifiers with
// due crossings -> Board.Recalculate) N times IN-PROCESS: no script changes between rounds, so
// no domain reload occurs (AssemblyReloadEvents counters recorded as proof). Writes
// editor-receipt.json + editor-batch.log into SV_EDITOR_RECEIPT_DIR.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sv;
using Sv.Unity;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Sv.Editor
{
    public struct EdHp : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    public struct EdMana : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    public struct EdStamina : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    public struct EdShield : IComponentData, IEnableableComponent
    {
        public int Value;
    }

    public static unsafe class SvEditorBatch
    {
        static readonly StringBuilder Log = new StringBuilder();

        static int reloadsBefore;

        static int reloadsAfter;

        static int Entities = 1 << 20;

        static int Warmup = 16;

        static int Rounds = 512;

        static long Tick;

        static UnityPopulation Population;

        static Board Board;

        static World World;

        static EntityManager Manager;

        static EntityQuery Query;

        static Entity[] EntityOfSlot;

        static int HitOffset;

        static int HealOffset;

        static long ExpiredTotal;

        static long CrossingsTotal;

        static long CrossingFirstId = -1;

        static long CrossingFirstTick = -1;

        static float*[] Bound;

        static Clamp HpClamp;

        static Clamp ManaClamp;

        static Clamp StaminaClamp;

        static Clamp ShieldClamp;

        static StatId Armor;

        static StatId Haste;

        public static void Run()
        {
            var dir = Environment.GetEnvironmentVariable("SV_EDITOR_RECEIPT_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Directory.GetCurrentDirectory(), "receipt");
            }

            Directory.CreateDirectory(dir);
            AssemblyReloadEvents.beforeAssemblyReload += CountReloadBefore;
            AssemblyReloadEvents.afterAssemblyReload += CountReloadAfter;
            var ok = false;
            try
            {
                Measure(dir);
                ok = true;
            }
            catch (Exception e)
            {
                Line("FAIL " + e.GetType().Name + ": " + e.Message);
                Line(e.StackTrace);
                throw;
            }
            finally
            {
                Line("reloads before=" + reloadsBefore + " after=" + reloadsAfter);
                File.WriteAllText(Path.Combine(dir, "editor-batch.log"), Log.ToString());
                File.WriteAllText(Path.Combine(dir, "editor-status.txt"), ok ? "ok" : "failed");
            }
        }

        static void CountReloadBefore()
        {
            reloadsBefore++;
        }

        static void CountReloadAfter()
        {
            reloadsAfter++;
        }

        static void Measure(string dir)
        {
            Entities = EnvInt("SV_EDITOR_ENTITIES", 1 << 20);
            Warmup = EnvInt("SV_EDITOR_WARMUP", 16);
            Rounds = EnvInt("SV_EDITOR_ROUNDS", 512);
            var setupWatch = Stopwatch.StartNew();
            Line("entry epoch ms " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            Line("entities=" + Entities + " warmup=" + Warmup + " rounds=" + Rounds);
            Line("unity " + Application.unityVersion + " burst " + global::Unity.Burst.BurstCompiler.IsEnabled);

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
            Armor = schema.Stat("armor");
            Haste = schema.Stat("haste");
            Population = new UnityPopulation(schema, Entities);
            Board = Population.Board;
            HpClamp = Clamp.StatMirrorMax(Board, schema.Stat("max_hp"));
            ManaClamp = Clamp.StatMirrorMax(Board, schema.Stat("max_mana"));
            StaminaClamp = Clamp.StatMirrorMax(Board, schema.Stat("max_stamina"));
            ShieldClamp = Clamp.StatMirrorMax(Board, schema.Stat("max_shield"));
            World = new World("sv-editor-receipt");
            Manager = World.EntityManager;
            var archetype = Manager.CreateArchetype(
                ComponentType.ReadWrite<SvSlot>(),
                ComponentType.ReadWrite<EdHp>(),
                ComponentType.ReadWrite<EdMana>(),
                ComponentType.ReadWrite<EdStamina>(),
                ComponentType.ReadWrite<EdShield>());
            Manager.CreateEntity(archetype, Entities);
            EntityOfSlot = new Entity[Entities];
            SeedPopulation();

            using (Population.Reconcile(Manager))
            {
            }

            RefreshEntityOfSlot();
            Query = Manager.CreateEntityQuery(typeof(SvSlot), typeof(EdHp), typeof(EdMana), typeof(EdStamina), typeof(EdShield));
            Bound = new float*[8];
            for (var i = 0; i < Bound.Length; i++)
            {
                Bound[i] = (float*)NativeMemory.AllocZeroed((nuint)Board.Capacity, (nuint)sizeof(float));
            }

            Board.BindOutput(schema.Stat("max_hp"), Bound[0]);
            Board.BindOutput(schema.Stat("max_mana"), Bound[1]);
            Board.BindOutput(schema.Stat("max_stamina"), Bound[2]);
            Board.BindOutput(schema.Stat("max_shield"), Bound[3]);
            Board.BindOutput(Armor, Bound[4]);
            Board.BindOutput(schema.Stat("move_speed"), Bound[5]);
            Board.BindOutput(schema.Stat("atk"), Bound[6]);
            Board.BindOutput(schema.Stat("defense"), Bound[7]);
            for (var slot = 0; slot < Entities; slot++)
            {
                Board.AddModifier(slot, Armor, new Modifier(ModifierKind.Multi, 1.25f), 0, ArmorExpiry(slot));
                Board.AddModifier(slot, Haste, new Modifier(ModifierKind.Multi, 1.1f), 0, 100 + (long)((ulong)(uint)slot * 2246822519ul % 262144ul));
            }

            for (var i = 0; i < 8; i++)
            {
                Board.ScheduleCrossing(200 + i * 7, 0x5500000000L + i);
            }

            Board.Recalculate();
            var verify = Verify();
            Line("verify " + verify);
            setupWatch.Stop();
            var setupMs = setupWatch.Elapsed.TotalMilliseconds;

            var totals = new double[Rounds];
            var reconciles = new double[Rounds];
            var visits = new double[Rounds];
            var expires = new double[Rounds];
            var recalcs = new double[Rounds];
            int steady = 0, structural = 0;
            var due = stackalloc long[1024];
            for (var r = -Warmup; r < Rounds; r++)
            {
                var recording = r >= 0;
                BusyFramePreStage();
                var structuralChange = Tick % 64 == 0;
                long total0 = Stopwatch.GetTimestamp();
                if (structuralChange)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    using (Population.Reconcile(Manager))
                    {
                    }

                    long t1 = Stopwatch.GetTimestamp();
                    if (recording)
                    {
                        reconciles[r] = Ms(t0, t1);
                    }

                    RefreshEntityOfSlot();
                }

                long v0 = Stopwatch.GetTimestamp();
                RunStage();
                long v1 = Stopwatch.GetTimestamp();
                long e0 = Stopwatch.GetTimestamp();
                ExpiredTotal += Board.ExpireModifiers(Tick);
                var dueCount = Board.TakeDueCrossings(new Span<long>(due, 1024));
                CrossingsTotal += dueCount;
                if (dueCount > 0 && CrossingFirstId < 0)
                {
                    CrossingFirstTick = Tick;
                    CrossingFirstId = due[0];
                }

                long e1 = Stopwatch.GetTimestamp();
                long c0 = Stopwatch.GetTimestamp();
                Board.Recalculate();
                long c1 = Stopwatch.GetTimestamp();
                long total1 = Stopwatch.GetTimestamp();
                if (recording)
                {
                    totals[r] = Ms(total0, total1);
                    visits[r] = Ms(v0, v1);
                    expires[r] = Ms(e0, e1);
                    recalcs[r] = Ms(c0, c1);
                    if (structuralChange)
                    {
                        structural++;
                    }
                    else
                    {
                        steady++;
                    }
                }
            }

            var verifyEnd = Board.LiveCount == Entities ? "ok live=" + Board.LiveCount : "FAIL live " + Board.LiveCount;
            Line("verify-end " + verifyEnd);
            var chunkCount = Query.CalculateChunkCount();
            File.WriteAllText(Path.Combine(dir, "editor-receipt.json"), BuildJson(verify, verifyEnd, setupMs, totals, reconciles, visits, expires, recalcs, steady, structural, chunkCount));
            Line("chunks " + chunkCount + " live " + Board.LiveCount + " expiredTotal " + ExpiredTotal + " crossingsTotal " + CrossingsTotal);
            Line("sv editor batch ok");

            foreach (var column in Bound)
            {
                NativeMemory.Free(column);
            }

            World.Dispose();
            Population.Dispose();
        }

        struct StageJob : IJobChunk
        {
            public ComponentTypeHandle<EdHp> Hp;

            public ComponentTypeHandle<EdMana> Mana;

            public ComponentTypeHandle<EdStamina> Stamina;

            public ComponentTypeHandle<EdShield> Shield;

            public ComponentTypeHandle<SvSlot> Slot;

            public ComponentTypeHandle<SvRange> Range;

            public int HitOffset;

            public int HealOffset;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rangeHandle = Range;
                var range = chunk.GetChunkComponentData(ref rangeHandle);
                if (range.Count == 0)
                {
                    return;
                }

                var startSlot = range.Start;
                var rows = range.Count;
                var applied = stackalloc int[128];
                var appliedView = applied - startSlot;
                var sv = new SvChunk(Board, Slot, Range);
                var hp = Hp;
                sv.RunIntrinsics(ref hp, chunk, useEnabledMask, chunkEnabledMask, 2, HpClamp, new Span<int>(applied, 128));
                var mana = Mana;
                sv.RunIntrinsics(ref mana, chunk, useEnabledMask, chunkEnabledMask, 3, ManaClamp, new Span<int>(applied, 128));
                var stamina = Stamina;
                sv.RunIntrinsics(ref stamina, chunk, useEnabledMask, chunkEnabledMask, 1, StaminaClamp, new Span<int>(applied, 128));
                var shield = Shield;
                sv.RunIntrinsics(ref shield, chunk, useEnabledMask, chunkEnabledMask, 1, ShieldClamp, new Span<int>(applied, 128));
                Window(chunk, ref hp, startSlot, rows, HitOffset, 64, -25, HpClamp, appliedView);
                Window(chunk, ref mana, startSlot, rows, HealOffset, 32, 15, ManaClamp, appliedView);
            }
        }

        static void Window<TColumn>(in ArchetypeChunk chunk, ref ComponentTypeHandle<TColumn> handle, int startSlot, int rows, int windowStart, int windowWidth, int delta, in Clamp clamp, int* appliedView)
            where TColumn : unmanaged, IComponentData
        {
            var lo = Math.Max(windowStart, startSlot);
            var hi = Math.Min(windowStart + windowWidth, startSlot + rows);
            if (hi <= lo)
            {
                return;
            }

            var width = hi - lo;
            var columns = chunk.GetComponentDataPtrRW(ref handle);
            var value = (int*)columns - startSlot;
            Board.RunEnabledChunk(value, delta, clamp, width == 64 ? ulong.MaxValue : (1ul << width) - 1, lo, appliedView, width);
        }

        static void RunStage()
        {
            var job = new StageJob
            {
                Hp = Manager.GetComponentTypeHandle<EdHp>(false),
                Mana = Manager.GetComponentTypeHandle<EdMana>(false),
                Stamina = Manager.GetComponentTypeHandle<EdStamina>(false),
                Shield = Manager.GetComponentTypeHandle<EdShield>(false),
                Slot = Manager.GetComponentTypeHandle<SvSlot>(false),
                Range = Manager.GetComponentTypeHandle<SvRange>(false),
                HitOffset = HitOffset,
                HealOffset = HealOffset,
            };
            job.Run(Query);
        }

        static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

        static int EnvInt(string name, int fallback)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
        }

        static void SeedPopulation()
        {
            var slotHandle = Manager.GetComponentTypeHandle<SvSlot>(false);
            var hpHandle = Manager.GetComponentTypeHandle<EdHp>(false);
            var manaHandle = Manager.GetComponentTypeHandle<EdMana>(false);
            var staminaHandle = Manager.GetComponentTypeHandle<EdStamina>(false);
            var shieldHandle = Manager.GetComponentTypeHandle<EdShield>(false);
            var entityType = Manager.GetEntityTypeHandle();
            var next = 0;
            using (var chunks = Manager.GetAllChunks())
            {
                for (var c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    var slots = chunk.GetComponentDataPtrRW(ref slotHandle);
                    var hp = (int*)chunk.GetComponentDataPtrRW(ref hpHandle);
                    var mana = (int*)chunk.GetComponentDataPtrRW(ref manaHandle);
                    var stamina = (int*)chunk.GetComponentDataPtrRW(ref staminaHandle);
                    var shield = (int*)chunk.GetComponentDataPtrRW(ref shieldHandle);
                    var entities = chunk.GetNativeArray(entityType);
                    for (var row = 0; row < chunk.Count; row++)
                    {
                        slots[row] = new SvSlot { Slot = next };
                        hp[row] = 80;
                        mana[row] = 40;
                        stamina[row] = 60;
                        shield[row] = 20;
                        EntityOfSlot[next] = entities[row];
                        next++;
                    }
                }
            }

            for (var slot = 0; slot < Entities; slot++)
            {
                Board.Acquire();
            }

            for (var slot = 0; slot < Entities; slot += 16)
            {
                if (slot != 3 && !TouchedByVerifyFrames(slot))
                {
                    Manager.SetComponentEnabled<EdMana>(EntityOfSlot[slot], false);
                }
            }
        }

        static void RefreshEntityOfSlot()
        {
            var slotHandle = Manager.GetComponentTypeHandle<SvSlot>(false);
            var entityType = Manager.GetEntityTypeHandle();
            using (var chunks = Manager.GetAllChunks(Allocator.Persistent))
            {
                for (var c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    if (!chunk.Has<SvSlot>())
                    {
                        continue;
                    }

                    var slots = chunk.GetComponentDataPtrRW(ref slotHandle);
                    var entities = chunk.GetNativeArray(entityType);
                    for (var row = 0; row < chunk.Count; row++)
                    {
                        EntityOfSlot[slots[row].Slot] = entities[row];
                    }
                }
            }
        }

        static bool TouchedByVerifyFrames(int slot)
        {
            for (var tick = 1; tick <= 3; tick++)
            {
                var hit = HitOffsetOf(tick);
                if (slot >= hit && slot < hit + 64)
                {
                    return true;
                }

                var heal = HealOffsetOf(tick);
                if (slot >= heal && slot < heal + 32)
                {
                    return true;
                }
            }

            return false;
        }

        static int HitOffsetOf(long tick) => (int)(((uint)tick * 4096u) % (uint)(Board.Capacity - 64));

        static int HealOffsetOf(long tick)
        {
            var hit = HitOffsetOf(tick);
            return (hit + Board.Capacity / 2) % (Board.Capacity - 64);
        }

        static long ArmorExpiry(int slot) => 100 + (long)((ulong)(uint)slot * 2654435761ul % 262144ul);

        static void BusyFramePreStage()
        {
            Tick++;
            HitOffset = HitOffsetOf(Tick);
            HealOffset = HealOffsetOf(Tick);
            if (Tick % 64 == 0)
            {
                for (var i = 0; i < 8; i++)
                {
                    var slot = (int)(((uint)Tick * 114514u + (uint)i * 941u) % (uint)Entities);
                    var entity = EntityOfSlot[slot];
                    Population.Unbind(Manager, entity);
                    Manager.DestroyEntity(entity);
                    EntityOfSlot[slot] = Entity.Null;
                }

                var spawnArchetype = Manager.CreateArchetype(
                    ComponentType.ReadWrite<EdHp>(),
                    ComponentType.ReadWrite<EdMana>(),
                    ComponentType.ReadWrite<EdStamina>(),
                    ComponentType.ReadWrite<EdShield>());
                for (var i = 0; i < 8; i++)
                {
                    var entity = Manager.CreateEntity(spawnArchetype);
                    Manager.SetComponentData(entity, new EdHp { Value = 80 });
                    Manager.SetComponentData(entity, new EdMana { Value = 40 });
                    Manager.SetComponentData(entity, new EdStamina { Value = 60 });
                    Manager.SetComponentData(entity, new EdShield { Value = 20 });
                    EntityOfSlot[Population.Bind(Manager, entity)] = entity;
                }
            }

            for (var i = 0; i < 8; i++)
            {
                var slot = BuffSlotOf(Tick, i);
                Board.AddModifier(slot, (i & 1) == 0 ? Armor : Haste, new Modifier(ModifierKind.Multi, 1.5f), 0, Tick + 600);
            }
        }

        static int BuffSlotOf(long tick, int i)
        {
            var slot = (int)(((uint)tick * 1_597_317u + (uint)i * 5077u) % (uint)Entities);
            while (EntityOfSlot[slot] == Entity.Null)
            {
                slot = (slot + 1) % Entities;
            }

            return slot;
        }

        static string Verify()
        {
            if (Board.LiveCount != Entities)
            {
                return "FAIL live " + Board.LiveCount + " != " + Entities;
            }

            var probe = 3;
            var buffSlot = BuffSlotOf(1, 0);
            var hitRow = HitOffsetOf(1);
            Tick = 0;
            FrameOnce();
            FrameOnce();
            var probeHp = Manager.GetComponentData<EdHp>(EntityOfSlot[probe]).Value;
            var probeMana = Manager.GetComponentData<EdMana>(EntityOfSlot[probe]).Value;
            if (probeHp != 84 || probeMana != 46)
            {
                return "FAIL probe hp " + probeHp + " mana " + probeMana + " (want 84/46)";
            }

            const int hitExpected = 80 + 2 - 25 + 2;
            var hitHp = Manager.GetComponentData<EdHp>(EntityOfSlot[hitRow]).Value;
            if (hitHp != hitExpected)
            {
                return "FAIL hit row hp " + hitHp + " != " + hitExpected;
            }

            if (BitConverter.SingleToInt32Bits(Bound[4][probe]) != BitConverter.SingleToInt32Bits(22.5f))
            {
return "FAIL armor fold " + Bound[4][probe] + " != 22.5";
            }

            if (BitConverter.SingleToInt32Bits(Bound[4][buffSlot]) != BitConverter.SingleToInt32Bits(56.25f))
            {
                return "FAIL buffed armor fold " + Bound[4][buffSlot] + " != 56.25";
            }

            return "ok probe hp=84 mana=46 hit=" + hitExpected + " armor 22.5/56.25 live=" + Board.LiveCount;
        }

        static void FrameOnce()
        {
            BusyFramePreStage();
            RunStage();
            Board.ExpireModifiers(Tick);
            var due = stackalloc long[1024];
            Board.TakeDueCrossings(new Span<long>(due, 1024));
            Board.Recalculate();
        }

        static string BuildJson(string verify, string verifyEnd, double setupMs, double[] totals, double[] reconciles, double[] visits, double[] expires, double[] recalcs, int steady, int structural, int chunkCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"status\": \"" + (verify.StartsWith("ok", StringComparison.Ordinal) && verifyEnd.StartsWith("ok", StringComparison.Ordinal) ? "ok" : "failed") + "\",");
            sb.AppendLine("  \"unityVersion\": \"" + Application.unityVersion + "\",");
            sb.AppendLine("  \"entities\": " + Entities + ",");
            sb.AppendLine("  \"chunkCount\": " + chunkCount + ",");
            sb.AppendLine("  \"boardCapacity\": " + Board.Capacity + ",");
            sb.AppendLine("  \"boardMemoryBytes\": " + Board.MemoryBytes + ",");
            sb.AppendLine("  \"rounds\": " + Rounds + ",");
            sb.AppendLine("  \"warmup\": " + Warmup + ",");
            sb.AppendLine("  \"steadyRounds\": " + steady + ",");
            sb.AppendLine("  \"structuralRounds\": " + structural + ",");
            sb.AppendLine("  \"setupMs\": " + setupMs.ToString("0.0", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"verify\": \"" + verify.Replace("\"", "'") + "\",");
            sb.AppendLine("  \"verifyEnd\": \"" + verifyEnd.Replace("\"", "'") + "\",");
            AppendStage(sb, "reconcileStructuralMs", reconciles, Mode.Structural);
            AppendStage(sb, "visitsMs", visits, Mode.Steady);
            AppendStage(sb, "expireAndCrossingsMs", expires, Mode.Steady);
            AppendStage(sb, "recalculateMs", recalcs, Mode.Steady);
            AppendStage(sb, "totalSteadyMs", totals, Mode.Steady);
            AppendStage(sb, "totalStructuralMs", totals, Mode.Structural);
            sb.AppendLine("  \"expiredTotal\": " + ExpiredTotal + ",");
            sb.AppendLine("  \"crossingsTotal\": " + CrossingsTotal + ",");
            sb.AppendLine("  \"crossingFirstId\": " + CrossingFirstId + ",");
            sb.AppendLine("  \"crossingFirstTick\": " + CrossingFirstTick + ",");
            sb.AppendLine("  \"reloadsBefore\": " + reloadsBefore + ",");
            sb.AppendLine("  \"reloadsAfter\": " + reloadsAfter + ",");
            sb.AppendLine("  \"rssKib\": " + RssKib());
            sb.AppendLine("}");
            return sb.ToString();
        }

        enum Mode
        {
            Steady,

            Structural,
        }

        static void AppendStage(StringBuilder sb, string name, double[] samples, Mode mode)
        {
            var list = new List<double>();
            for (var i = 0; i < samples.Length; i++)
            {
                var isStructural = (Warmup + i + 3) % 64 == 0;
                if (mode == Mode.Steady && isStructural)
                {
                    continue;
                }

                if (mode == Mode.Structural && !isStructural)
                {
                    continue;
                }

                if (samples[i] > 0)
                {
                    list.Add(samples[i]);
                }
            }

            if (list.Count == 0)
            {
                sb.AppendLine("  \"" + name + "\": null,");
                return;
            }

            list.Sort();
            sb.AppendLine("  \"" + name + "\": { \"n\": " + list.Count + ", \"p50\": " + list[list.Count / 2].ToString("0.000000", CultureInfo.InvariantCulture) + ", \"p99\": " + list[(list.Count * 99) / 100].ToString("0.000000", CultureInfo.InvariantCulture) + " },");
        }

        static long RssKib()
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                {
                    return long.Parse(line["VmRSS:".Length..].TrimEnd('k', 'B', ' ').Trim(), CultureInfo.InvariantCulture);
                }
            }

            return -1;
        }

        static void Line(string text)
        {
            Log.AppendLine(text);
            Console.WriteLine(text);
        }
    }
}
