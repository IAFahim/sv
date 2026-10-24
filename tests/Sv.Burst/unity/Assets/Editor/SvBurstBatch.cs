using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst;
using UnityEngine;

namespace Sv.Burst
{
    public unsafe delegate void IntBufferOp(int* buffer, int count);

    public unsafe delegate void FloatBufferOp(float* buffer, int count);

    public unsafe delegate ulong IntDigest(int* buffer, int count);

    public unsafe delegate ulong FloatDigest(float* buffer, int count);

    /// <summary>
    /// Atom 6.2 batch entry: run the seeded cross-target program under Mono and under the real
    /// Burst compiler (FloatMode.Strict, FloatPrecision.Standard), assert identical digests,
    /// dump the compiled code for the FMA scan, and record the BCL audit probes. Invoked as
    /// `Unity -batchmode -nographics -quit -executeMethod Sv.Burst.SvBurstBatch.Run` with
    /// SV_BURST_RECEIPT_DIR naming the output directory.
    /// </summary>
    public static unsafe class SvBurstBatch
    {
        static FunctionPointer<Kernels.RunProgramDelegate> runProgram;

        static readonly StringBuilder Log = new StringBuilder();

        static int failures;

        public static void Run()
        {
            var dir = Environment.GetEnvironmentVariable("SV_BURST_RECEIPT_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Directory.GetCurrentDirectory(), "receipt");
            }

            Directory.CreateDirectory(dir);
            try
            {
                BurstCompiler.Options.EnableBurstCompileSynchronously = true;
                if (Environment.GetEnvironmentVariable("SV_BURST_DISABLE") == "1")
                {
                    BurstCompiler.Options.EnableBurstCompilation = false;
                }

                Line("burst compiler enabled=" + BurstCompiler.IsEnabled + " executionMode=" + BurstCompiler.GetExecutionMode() + " forcedOff=" + (Environment.GetEnvironmentVariable("SV_BURST_DISABLE") == "1"));

                var digestMono = RunScenario(false);
                Line("digestMono   " + digestMono.ToString("X16"));

                runProgram = BurstCompiler.CompileFunctionPointer<Kernels.RunProgramDelegate>(Kernels.RunProgram);
                Line("runProgram compiled ptr=0x" + runProgram.Value.ToInt64().ToString("X16"));

                var digestBurst = RunScenario(true);
                Line("digestBurst  " + digestBurst.ToString("X16"));
                if (digestMono != digestBurst)
                {
                    Fail("digest mismatch: mono " + digestMono.ToString("X16") + " burst " + digestBurst.ToString("X16"));
                }

                DumpCode(dir, "RunProgram", runProgram.Value);
                var burstNanos = TimeLoop(true);
                var monoNanos = TimeLoop(false);
                Line("timing managed " + monoNanos.ToString("0.0") + " ns/program burst " + burstNanos.ToString("0.0") + " ns/program ratio " + (monoNanos / burstNanos).ToString("0.00") + "x");
                RunProbes(dir);
                WriteReceipt(dir, digestMono, digestBurst, failures == 0 ? "ok" : "failed", monoNanos, burstNanos);
                if (failures != 0)
                {
                    throw new Exception("sv burst batch failed with " + failures + " failure(s)");
                }

                Line("sv burst batch ok");
            }
            catch (Exception e)
            {
                Fail(e.ToString());
                WriteReceipt(dir, 0, 0, "failed", 0, 0);
                throw;
            }
            finally
            {
                File.WriteAllText(Path.Combine(dir, "burst-receipt.log"), Log.ToString());
            }
        }

        static ulong RunScenario(bool burst)
        {
            var state = AllocScenario();
            try
            {
                var count = Scenario.Build(state.Ops, state.Value, state.Delta, state.Applied, state.MinRows, state.MaxRows, state.MinStats, state.MaxStats, state.Words);
                var kernelState = new KState { Value = state.Value, Applied = state.Applied };
                InvokeProgram(burst, &kernelState, state.Ops, count);
                return Scenario.Digest(state.Value, state.Applied, Scenario.Rows);
            }
            finally
            {
                FreeScenario(state);
            }
        }

        static double TimeLoop(bool burst)
        {
            var state = AllocScenario();
            try
            {
                var count = Scenario.Build(state.Ops, state.Value, state.Delta, state.Applied, state.MinRows, state.MaxRows, state.MinStats, state.MaxStats, state.Words);
                var kernelState = new KState { Value = state.Value, Applied = state.Applied };
                const int warmup = 16;
                const int iterations = 400;
                for (var i = 0; i < warmup; i++)
                {
                    InvokeProgram(burst, &kernelState, state.Ops, count);
                }

                var watch = Stopwatch.StartNew();
                for (var i = 0; i < iterations; i++)
                {
                    InvokeProgram(burst, &kernelState, state.Ops, count);
                }

                watch.Stop();
                return watch.Elapsed.TotalMilliseconds * 1e6 / iterations;
            }
            finally
            {
                FreeScenario(state);
            }
        }

        static void InvokeProgram(bool burst, KState* kernelState, KOp* ops, int count)
        {
            if (burst)
            {
                runProgram.Invoke(kernelState, ops, count);
            }
            else
            {
                Kernels.RunProgram(kernelState, ops, count);
            }
        }

        sealed class ScenarioBuffers
        {
            public int* Value;
            public int* Delta;
            public int* Applied;
            public int* MinRows;
            public int* MaxRows;
            public float* MinStats;
            public float* MaxStats;
            public ulong* Words;
            public KOp* Ops;
        }

        static ScenarioBuffers AllocScenario()
        {
            const int rows = Scenario.Rows;
            return new ScenarioBuffers
            {
                Value = (int*)Marshal.AllocHGlobal(sizeof(int) * rows),
                Delta = (int*)Marshal.AllocHGlobal(sizeof(int) * rows),
                Applied = (int*)Marshal.AllocHGlobal(sizeof(int) * rows),
                MinRows = (int*)Marshal.AllocHGlobal(sizeof(int) * rows),
                MaxRows = (int*)Marshal.AllocHGlobal(sizeof(int) * rows),
                MinStats = (float*)Marshal.AllocHGlobal(sizeof(float) * rows),
                MaxStats = (float*)Marshal.AllocHGlobal(sizeof(float) * rows),
                Words = (ulong*)Marshal.AllocHGlobal(sizeof(ulong) * (rows / 64)),
                Ops = (KOp*)Marshal.AllocHGlobal(sizeof(KOp) * Scenario.Ops),
            };
        }

        static void FreeScenario(ScenarioBuffers buffers)
        {
            Marshal.FreeHGlobal(new IntPtr(buffers.Value));
            Marshal.FreeHGlobal(new IntPtr(buffers.Delta));
            Marshal.FreeHGlobal(new IntPtr(buffers.Applied));
            Marshal.FreeHGlobal(new IntPtr(buffers.MinRows));
            Marshal.FreeHGlobal(new IntPtr(buffers.MaxRows));
            Marshal.FreeHGlobal(new IntPtr(buffers.MinStats));
            Marshal.FreeHGlobal(new IntPtr(buffers.MaxStats));
            Marshal.FreeHGlobal(new IntPtr(buffers.Words));
            Marshal.FreeHGlobal(new IntPtr(buffers.Ops));
        }

        static void RunProbes(string dir)
        {
            var mathClamp = BurstCompiler.CompileFunctionPointer<Probes.IntProbe>(Probes.MathClampProbe);
            Probe(
                dir,
                "mathclamp",
                "System.Math.Clamp/Min/Max on ints",
                (buffer, count) => Probes.MathClampProbe(buffer, count),
                (buffer, count) => mathClamp.Invoke(buffer, count),
                DigestInts);
            DumpCode(dir, "MathClampProbe", mathClamp.Value);

            var floatFold = BurstCompiler.CompileFunctionPointer<Probes.FloatProbe>(Probes.FloatFoldProbe);
            Probe(
                dir,
                "floatfold",
                "float a*b+c under FloatMode.Strict (must not fuse to FMA)",
                (buffer, count) => Probes.FloatFoldProbe(buffer, count),
                (buffer, count) => floatFold.Invoke(buffer, count),
                DigestFloatBits);
            DumpCode(dir, "FloatFoldProbe", floatFold.Value);

            ProbeCompileOnly("throwinterpolated", "throw new ArgumentException with interpolated string", delegate
            {
                BurstCompiler.CompileFunctionPointer<Probes.ThrowProbe>(Probes.ThrowInterpolatedProbe);
            });
        }

        static void Probe(string dir, string name, string what, IntBufferOp managed, IntBufferOp burst, IntDigest digest)
        {
            const int count = 4096;
            var a = (int*)Marshal.AllocHGlobal(sizeof(int) * count);
            var b = (int*)Marshal.AllocHGlobal(sizeof(int) * count);
            try
            {
                FillProbe(a, count);
                FillProbe(b, count);
                managed(a, count);
                burst(b, count);
                Report(dir, name, what, digest(a, count), digest(b, count));
            }
            finally
            {
                Marshal.FreeHGlobal(new IntPtr(a));
                Marshal.FreeHGlobal(new IntPtr(b));
            }
        }

        static void Probe(string dir, string name, string what, FloatBufferOp managed, FloatBufferOp burst, FloatDigest digest)
        {
            const int count = 4096;
            var a = (float*)Marshal.AllocHGlobal(sizeof(float) * count);
            var b = (float*)Marshal.AllocHGlobal(sizeof(float) * count);
            try
            {
                FillProbe(a, count);
                FillProbe(b, count);
                managed(a, count);
                burst(b, count);
                Report(dir, name, what, digest(a, count), digest(b, count));
            }
            finally
            {
                Marshal.FreeHGlobal(new IntPtr(a));
                Marshal.FreeHGlobal(new IntPtr(b));
            }
        }

        static void Report(string dir, string name, string what, ulong digestA, ulong digestB)
        {
            Line("probe " + name + " (" + what + "): managed " + digestA.ToString("X16") + " burst " + digestB.ToString("X16") + " " + (digestA == digestB ? "match" : "MISMATCH"));
            if (digestA != digestB)
            {
                Fail("probe " + name + " mismatch");
            }

            File.WriteAllText(Path.Combine(dir, "probe-" + name + ".digest"), digestA.ToString("X16") + " " + digestB.ToString("X16"));
        }

        static void ProbeCompileOnly(string name, string what, Action compile)
        {
            try
            {
                compile();
                Line("probe " + name + " (" + what + "): compiled by Burst without error");
            }
            catch (Exception e)
            {
                var firstLine = e.Message.Split('\n')[0];
                Line("probe " + name + " (" + what + "): COMPILE BLOCKED: " + firstLine);
            }
        }

        static void FillProbe(int* buffer, int count)
        {
            var state = 0x123456789ABCDEF0ul;
            for (var i = 0; i < count; i++)
            {
                buffer[i] = (int)(Scenario.Next(ref state) & 0x7FFFFFFFul);
            }
        }

        static void FillProbe(float* buffer, int count)
        {
            var state = 0x123456789ABCDEF0ul;
            for (var i = 0; i < count; i++)
            {
                var bits = (uint)(Scenario.Next(ref state) & 0xFFFFFFFFul);
                buffer[i] = *(float*)&bits;
            }
        }

        static ulong DigestInts(int* p, int count)
        {
            var hash = 0xCBF29CE484222325ul;
            for (var i = 0; i < count; i++)
            {
                hash = Scenario.Mix(hash, (uint)p[i]);
            }

            return hash;
        }

        static ulong DigestFloatBits(float* f, int count)
        {
            var hash = 0xCBF29CE484222325ul;
            for (var i = 0; i < count; i++)
            {
                hash = Scenario.Mix(hash, *(uint*)&f[i]);
            }

            return hash;
        }

        static void DumpCode(string dir, string name, IntPtr addr)
        {
            var head = new byte[64];
            for (var hop = 0; hop < 5; hop++)
            {
                Marshal.Copy(addr, head, 0, head.Length);
                var jmp = IndexOf(head, 0xFF, 0xE0);
                var movabs = jmp >= 10 ? LastIndexOf(head, 0x48, 0xB8, jmp - 10, jmp) : -1;
                if (movabs < 0)
                {
                    Line("dump " + name + ": code at 0x" + addr.ToInt64().ToString("X16") + " after " + hop + " thunk hop(s)");
                    DumpWindow(dir, name, addr);
                    var pre = new IntPtr(addr.ToInt64() - 1024);
                    if (ReadableExtent(pre, 2048) >= 2048)
                    {
                        DumpWindow(dir, name + "-pre", pre);
                    }

                    return;
                }

                var target = (long)head[movabs + 2] | (long)head[movabs + 3] << 8 | (long)head[movabs + 4] << 16 | (long)head[movabs + 5] << 24
                    | (long)head[movabs + 6] << 32 | (long)head[movabs + 7] << 40 | (long)head[movabs + 8] << 48 | (long)head[movabs + 9] << 56;
                Line("dump " + name + ": thunk at 0x" + addr.ToInt64().ToString("X16") + " jumps to 0x" + target.ToString("X16"));
                addr = new IntPtr(target);
            }

            Line("dump " + name + ": too many thunk hops");
            Fail("thunk chain too deep for " + name);
        }

        static int IndexOf(byte[] haystack, byte first, byte second)
        {
            for (var i = 0; i + 1 < haystack.Length; i++)
            {
                if (haystack[i] == first && haystack[i + 1] == second)
                {
                    return i;
                }
            }

            return -1;
        }

        static int LastIndexOf(byte[] haystack, byte first, byte second, int from, int to)
        {
            for (var i = Math.Min(to, haystack.Length - 2); i >= Math.Max(0, from); i--)
            {
                if (haystack[i] == first && haystack[i + 1] == second)
                {
                    return i;
                }
            }

            return -1;
        }

        static void DumpWindow(string dir, string name, IntPtr addr)
        {
            var extent = ReadableExtent(addr, 4096);
            if (extent <= 0)
            {
                Line("dump " + name + ": no readable extent found for 0x" + addr.ToInt64().ToString("X16"));
                Fail("could not map code extent for " + name);
                return;
            }

            var bytes = new byte[extent];
            Marshal.Copy(addr, bytes, 0, extent);
            File.WriteAllBytes(Path.Combine(dir, name + ".bin"), bytes);
            File.WriteAllText(Path.Combine(dir, name + ".addr"), "0x" + addr.ToInt64().ToString("X16") + " " + extent);
            Line("dump " + name + ": " + extent + " bytes at 0x" + addr.ToInt64().ToString("X16"));
        }

        static int ReadableExtent(IntPtr addr, int want)
        {
            var target = addr.ToInt64();
            try
            {
                foreach (var line in File.ReadLines("/proc/self/maps"))
                {
                    var dash = line.IndexOf('-');
                    if (dash < 0)
                    {
                        continue;
                    }

                    var space = line.IndexOf(' ', dash);
                    if (space < 0)
                    {
                        continue;
                    }

                    long lo;
                    long hi;
                    if (!TryParseHex(line.Substring(0, dash), out lo) || !TryParseHex(line.Substring(dash + 1, space - dash - 1), out hi))
                    {
                        continue;
                    }

                    if (lo <= target && target < hi)
                    {
                        return (int)Math.Min(hi - target, want);
                    }
                }
            }
            catch (Exception e)
            {
                Line("maps read failed: " + e.Message);
            }

            return 0;
        }

        static bool TryParseHex(string text, out long value)
        {
            try
            {
                value = Convert.ToInt64(text, 16);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        static void Line(string text)
        {
            Log.AppendLine(text);
            Console.WriteLine(text);
        }

        static void Fail(string text)
        {
            failures++;
            Log.AppendLine("FAIL: " + text);
            Console.WriteLine("FAIL: " + text);
        }

        static void WriteReceipt(string dir, ulong digestMono, ulong digestBurst, string status, double monoNanos, double burstNanos)
        {
            var enabled = BurstCompiler.IsEnabled;
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"status\": \"" + status + "\",");
            json.AppendLine("  \"unityVersion\": \"" + Application.unityVersion + "\",");
            json.AppendLine("  \"burstCompilerEnabled\": " + enabled.ToString().ToLowerInvariant() + ",");
            json.AppendLine("  \"digestMono\": \"" + digestMono.ToString("X16") + "\",");
            json.AppendLine("  \"digestBurst\": \"" + digestBurst.ToString("X16") + "\",");
            json.AppendLine("  \"timingManagedNs\": " + monoNanos.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ",");
            json.AppendLine("  \"timingBurstNs\": " + burstNanos.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ",");
            json.AppendLine("  \"failures\": " + failures + ",");
            json.AppendLine("  \"runProgramPtr\": \"0x" + runProgram.Value.ToInt64().ToString("X16") + "\"");
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "burst-receipt.json"), json.ToString());
        }
    }
}
