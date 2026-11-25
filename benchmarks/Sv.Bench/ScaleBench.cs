extern alias v3;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using v3::Sv;
using v3::Sv.Host;
using v3::Sv.Rules;
using LegoKernels = v3::Sv.Kernels.Lego.HostKernels;

namespace SvV3Bench
{
    internal sealed class ScaleShape
    {
        public int Entities = 1_000_000;
        public int Ops = 1;
        public string Targets = "grouped";
        public string Sources = "mixed";
        public int Modifiers;
        public double Churn = 0.01;
        public int LogLimit = int.MaxValue;
        public int Digest = 1;
        public int Frames = 64;
        public string Order = "key";

        public static ScaleShape FromEnvironment()
        {
            var shape = new ScaleShape();
            shape.Entities = Int("SV_SCALE_ENTITIES", shape.Entities);
            shape.Ops = Int("SV_SCALE_OPS", shape.Ops);
            shape.Targets = Environment.GetEnvironmentVariable("SV_SCALE_TARGETS") ?? shape.Targets;
            shape.Sources = Environment.GetEnvironmentVariable("SV_SCALE_SOURCES") ?? shape.Sources;
            shape.Modifiers = Int("SV_SCALE_MODIFIERS", shape.Modifiers);
            shape.Churn = double.TryParse(Environment.GetEnvironmentVariable("SV_SCALE_CHURN"), NumberStyles.Float, CultureInfo.InvariantCulture, out var churn) ? churn : shape.Churn;
            shape.LogLimit = Int("SV_SCALE_LOGLIMIT", shape.LogLimit);
            shape.Digest = Int("SV_SCALE_DIGEST", shape.Digest);
            shape.Frames = Int("SV_SCALE_FRAMES", shape.Frames);
            shape.Order = Environment.GetEnvironmentVariable("SV_SCALE_ORDER") ?? shape.Order;
            return shape;
        }

        static int Int(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        public string Describe() => string.Format(CultureInfo.InvariantCulture,
            "entities {0}, records per entity {1} ({2} targets, {3} sources, {8} order), modifiers per entity {4} (churn {5:0.###}), log limit {6}, digest {7}",
            Entities, Ops, Targets, Sources, Modifiers, Churn, LogLimit == int.MaxValue ? "none" : LogLimit.ToString(CultureInfo.InvariantCulture), Digest, Order);
    }

    internal static unsafe class Scale
    {
        static readonly string[] Mix = LegoMix();

        static readonly string[] SendChannels = { "Physical", "Fire", "Poison", "Healing", "Tick", "Physical", "Healing", "Fire" };

        static readonly string[] Phases = { "send", "seal", "log", "stats", "collect", "decide", "commit", "events", "digest", "end", "total" };

        static string[] LegoMix() => new[]
        {
            "dummy", "shielded", "dummy", "vampire", "dummy", "thorny", "dummy", "regen_low", "dummy", "ward_charges",
            "dummy", "masked", "dummy", "crit_target", "vampire", "bloodmage", "shielded", "thorny", "dummy", "worked_example_player",
        };

        public static int Run(string scenario, int ms)
        {
            if (scenario != "v3-scale")
            {
                return 0;
            }

            var shape = ScaleShape.FromEnvironment();
            return Frames(shape);
        }

        static int Frames(ScaleShape shape)
        {
            var workers = LegoWorld.Workers;
            var build = Stopwatch.StartNew();
            var plan = PackCompiler.Compile(LegoWorld.Read(LegoWorld.LegoFolder())).Value!;
            var pack = HostPack.From(plan);
            var total = (long)shape.Entities * shape.Ops;
            if (total > int.MaxValue / 2)
            {
                throw new ArgumentException("sv bench: entities x ops must stay under 2^30 records per frame");
            }

            using var host = new FrameLoop<LegoKernels>(pack, new HostOptions
            {
                EntityCapacity = shape.Entities + Mix.Length * Slot.Rows,
                Workers = workers,
                Parts = 64,
                ExternalWriters = workers,
                RawCapacity = (int)Math.Min(int.MaxValue / 2, total + total / 4 + (1 << 16)),
                DigestEvery = shape.Digest,
                InputLogFrameLimit = shape.LogLimit,
            });
            var schema = plan.Schema;
            var templateIndex = new Dictionary<string, int>();
            for (var t = 0; t < schema.Templates.Length; t++)
            {
                templateIndex[schema.Templates[t].Name] = t;
            }

            var kindLane = schema.KindIntrinsic;
            var slots = new int[shape.Entities];
            var kindOfSlot = new byte[shape.Entities + Mix.Length * Slot.Rows];
            var spawned = 0;
            for (var chunk = 0; spawned < shape.Entities; chunk++)
            {
                var template = templateIndex[Mix[chunk % Mix.Length]];
                var kind = (byte)Math.Clamp(pack.TemplateValues[template][kindLane], 0, 255);
                for (var row = 0; row < Slot.Rows && spawned < shape.Entities; row++)
                {
                    var slot = host.Spawn(template);
                    slots[spawned] = slot;
                    kindOfSlot[slot] = kind;
                    spawned++;
                }
            }

            var channels = new byte[SendChannels.Length];
            for (var c = 0; c < channels.Length; c++)
            {
                channels[c] = (byte)Array.Find(schema.Channels, x => x.Name == SendChannels[c])!.Id;
            }

            var lists = new Effect*[workers];
            var counts = new int[workers];
            Fill(shape, slots, kindOfSlot, channels, lists, counts, workers);
            var spawnSeconds = build.Elapsed.TotalSeconds;

            var statIds = new int[pack.Stats.Length];
            for (var s = 0; s < statIds.Length; s++)
            {
                statIds[s] = pack.Stats[s].Lane;
            }

            var modifierSetup = 0.0;
            if (shape.Modifiers > 0)
            {
                var clock = Stopwatch.StartNew();
                for (var e = 0; e < shape.Entities; e++)
                {
                    for (var m = 0; m < shape.Modifiers; m++)
                    {
                        host.Modify(Modifier(slots[e], m, statIds));
                    }
                }

                host.Step();
                modifierSetup = clock.Elapsed.TotalSeconds;
            }

            var senders = new v3::Sv.Host.Workers(workers);
            var send = (SendState*)Memory.AllocZeroed(1, (nuint)sizeof(SendState));
            send->Lists = (Effect**)Memory.AllocZeroed((nuint)workers, (nuint)sizeof(Effect*));
            send->Counts = (int*)Memory.AllocZeroed((nuint)workers, sizeof(int));
            send->Writers = (EffectWriter**)Memory.AllocZeroed((nuint)workers, (nuint)sizeof(EffectWriter*));
            for (var w = 0; w < workers; w++)
            {
                send->Writers[w] = host.Sender(w);
                send->Lists[w] = lists[w];
                send->Counts[w] = counts[w];
            }

            var churned = (int)Math.Round(shape.Entities * shape.Churn);
            var churnAt = 0;
            void Churn()
            {
                if (shape.Modifiers == 0)
                {
                    return;
                }

                for (var at = 0; at < churned; at++)
                {
                    var slot = slots[churnAt];
                    churnAt = churnAt + 1 == slots.Length ? 0 : churnAt + 1;
                    host.Modify(StatOp.RemoveSource(slot, ModifierSource(0)));
                    host.Modify(Modifier(slot, 0, statIds));
                }
            }

            var warm = Math.Min(64, Math.Max(8, shape.Frames / 2));
            for (var f = 0; f < warm; f++)
            {
                Churn();
                senders.Run(&LegoSend.Part, send, workers);
                host.Step();
            }

            var frames = shape.Frames;
            var samples = new double[Phases.Length][];
            for (var p = 0; p < Phases.Length; p++)
            {
                samples[p] = new double[frames];
            }

            var scale = 1000.0 / Stopwatch.Frequency;
            var allocated = 0L;
            var canonical = new double[frames];
            var active = new double[frames];
            for (var f = 0; f < frames; f++)
            {
                Churn();
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                senders.Run(&LegoSend.Part, send, workers);
                var sent = Stopwatch.GetTimestamp();
                host.Step();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                var marks = host.Marks;
                samples[0][f] = (sent - start) * scale;
                for (var p = 1; p < FrameMarks.Count; p++)
                {
                    samples[p][f] = (marks[p] - marks[p - 1]) * scale;
                }

                samples[Phases.Length - 1][f] = (marks[FrameMarks.Ended] - start) * scale;
                canonical[f] = host.Canonical.Count;
                active[f] = host.Canonical.ActiveCount;
            }

            Array.Sort(canonical);
            Array.Sort(active);
            var medianTotal = Median(samples[Phases.Length - 1]);
            var sentTotal = 0L;
            for (var w = 0; w < workers; w++)
            {
                sentTotal += counts[w];
            }

            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var line = new StringBuilder();
            line.Append("v3-scale ").Append(shape.Describe()).Append('\n');
            line.Append(string.Format(CultureInfo.InvariantCulture,
                "  workers {0}, frames {1}, world+records built {2:0.0} s, modifier setup {3:0.0} s ({4} modifiers live), records sent {5}, canonical {6:0}, active chunks {7:0}\n",
                workers, frames, spawnSeconds, modifierSetup, host.Stats.ModifierCount, sentTotal, canonical[frames / 2], active[frames / 2]));
            line.Append(string.Format(CultureInfo.InvariantCulture,
                "  total median {0:0.000} ms = {1:0.00} ns per sent record ({2:0.0} M records/s), {3:0.00} ns per entity; alloc(main) {4} B; private {5:0} MB; input log {6:0.0} MB; raw grows {7}; dropped {9}; digest {8:X16}\n",
                medianTotal, medianTotal * 1e6 / Math.Max(1, sentTotal), sentTotal / Math.Max(1e-9, medianTotal) / 1e3, medianTotal * 1e6 / shape.Entities,
                allocated, process.PrivateMemorySize64 / 1e6, host.Log.Bytes / 1e6, host.Raw.Grows, host.LastDigest, host.ExternalDropped));
            for (var p = 0; p < Phases.Length; p++)
            {
                Array.Sort(samples[p]);
                line.Append(string.Format(CultureInfo.InvariantCulture, "  {0,-8} median {1,9:0.000} ms  p10 {2,9:0.000}  p90 {3,9:0.000}  max {4,9:0.000}\n",
                    Phases[p], samples[p][frames / 2], samples[p][frames / 10], samples[p][frames * 9 / 10], samples[p][frames - 1]));
            }

            Console.Write(line.ToString());
            senders.Dispose();
            Memory.Free(send->Lists);
            Memory.Free(send->Counts);
            Memory.Free(send->Writers);
            Memory.Free(send);
            for (var w = 0; w < workers; w++)
            {
                Memory.Free(lists[w]);
            }

            return host.ExternalDropped == 0 && allocated == 0 ? 0 : 1;
        }

        static int ModifierSource(int m) => 1 + m;

        static StatOp Modifier(int slot, int m, int[] stats)
        {
            var stat = stats[m % stats.Length];
            var kind = (ShapeKind)((m / stats.Length) % 3);
            var value = kind == ShapeKind.Added ? 1 + m % 5 : kind == ShapeKind.Additive ? 10 + m % 50 : 10_000 + m % 100;
            var layer = (m & 1) == 0 ? StatLayer.Core : StatLayer.External;
            return StatOp.Add(slot, stat, kind, value, ModifierSource(m), long.MaxValue, layer);
        }

        static void Fill(ScaleShape shape, int[] slots, byte[] kindOfSlot, byte[] channels, Effect*[] lists, int[] counts, int workers)
        {
            var n = shape.Entities;
            var k = shape.Ops;
            for (var w = 0; w < workers; w++)
            {
                var from = (long)n * w / workers;
                var to = (long)n * (w + 1) / workers;
                counts[w] = (int)((to - from) * k);
                lists[w] = (Effect*)Memory.Alloc((nuint)Math.Max(1, counts[w]), (nuint)sizeof(Effect));
                var state = (uint)(0x9E3779B9u ^ (uint)(w * 7919 + 1)) | 1u;
                var at = 0;
                for (var e = from; e < to; e++)
                {
                    for (var op = 0; op < k; op++)
                    {
                        var roll = Next(ref state);
                        var target = shape.Targets == "random" ? slots[Next(ref state) % (uint)n] : slots[e];
                        int source;
                        switch (shape.Sources)
                        {
                            case "none":
                                source = EffectKey.NoSource;
                                break;
                            case "local":
                                source = slots[(int)((e & ~63L) + (Next(ref state) & 63u)) % n];
                                break;
                            case "random":
                                source = slots[Next(ref state) % (uint)n];
                                break;
                            default:
                                var pick = roll & 3u;
                                source = pick == 0u ? EffectKey.NoSource : pick == 1u ? slots[Next(ref state) % (uint)n] : slots[(int)((e & ~63L) + (Next(ref state) & 63u)) % n];
                                break;
                        }

                        var channel = channels[(roll >> 2) & 7u];
                        var kind = source == EffectKey.NoSource ? (byte)0 : kindOfSlot[source];
                        var amount = 1 + (int)((roll >> 5) & 7u);
                        var hits = 1 + (int)((roll >> 8) & 1u);
                        lists[w][at++] = new Effect(EffectKey.Pack(target, source, kind, channel, Origin.Direct, 0), amount, hits);
                    }
                }

                if (shape.Order == "key")
                {
                    new Span<Effect>(lists[w], counts[w]).Sort((a, b) => a.Key.CompareTo(b.Key));
                }
                else if (shape.Order == "canonical")
                {
                    new Span<Effect>(lists[w], counts[w]).Sort((a, b) => EffectLaw.Compare(a, b));
                }
            }
        }

        static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        static double Median(double[] values)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }
    }
}
