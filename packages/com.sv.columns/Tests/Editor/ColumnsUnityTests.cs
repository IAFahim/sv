using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sv.Columns.Canon;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Sv.Columns.Unity.Tests
{
    public sealed class ColumnsUnityTests
    {
        static string Arena() => File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns/Tests/Packs~/arena.json"));

        static string Hex(ulong value) => value.ToString("X16");

        [Test]
        public void TheRuntimeIsReported()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity);
            var avx2 = Type.GetType("System.Runtime.Intrinsics.X86.Avx2, System.Private.CoreLib");
            var supported = avx2?.GetProperty("IsSupported")?.GetValue(null);
            Debug.Log("SVCOLUMNS runtime " + RuntimeInformation.FrameworkDescription + " " + RuntimeInformation.ProcessArchitecture + ", kernels " + (world.Vectorized ? "Vector256" : "scalar") + ", runtime Avx2 " + (supported ?? "absent") + ", job workers " + JobsUtility.JobWorkerCount);
            Debug.Log("SVCOLUMNS defines " + string.Join(" ", UnityEditor.EditorUserBuildSettings.activeScriptCompilationDefines));
            Assert.AreEqual(pack.TemplateNames.Count, 9);
        }

        [TestCase(KernelChoice.Scalar, 1, 1)]
        [TestCase(KernelChoice.Best, 1, 1)]
        [TestCase(KernelChoice.Best, 3, 7)]
        public void TheCanonRunHasTheDotNetDigest(KernelChoice kernels, int writers, int parts)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(world, world.Step)));
        }

        [TestCase(KernelChoice.Scalar)]
        [TestCase(KernelChoice.Best)]
        public void TheCanonRunOnJobsHasTheDotNetDigest(KernelChoice kernels)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity, 3, (JobsUtility.JobWorkerCount + 1) * 4, kernels);
            using var stepper = new ColumnStepper(world);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(world, stepper.Step)));
        }

        [Test]
        public void TheHostRunsTheCanonAndClosesWithItsObject()
        {
            var host = new GameObject("columns").AddComponent<ColumnHost>();
            try
            {
                Assert.IsNull(host.World);
                host.Open(Arena(), CanonRun.Capacity, 2);
                host.StepIn = ColumnHost.StepPhase.Manual;
                var stepped = 0;
                host.Stepped += _ => stepped++;
                Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(host.World!, host.Step)));
                Assert.AreEqual(CanonRun.Frames, stepped);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }

            Assert.IsNull(host.World);
            Assert.IsNull(host.Pack);
        }

        [Test]
        public void TheHostWithoutJobsMatches()
        {
            var host = new GameObject("columns").AddComponent<ColumnHost>();
            try
            {
                host.Open(Arena(), CanonRun.Capacity, 1, 0, false, KernelChoice.Scalar);
                Assert.AreEqual(1, host.World!.ResolveParts);
                Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(host.World, host.Step)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void ABrokenPackSaysWhere()
        {
            var host = new GameObject("columns").AddComponent<ColumnHost>();
            try
            {
                var error = Assert.Throws<PackException>(() => host.Open("{ \"lanes\": [ { \"name\": \"Health\", \"default\": 1.5 } ] }", 64));
                StringAssert.Contains("whole numbers", error!.Message);
                Assert.IsNull(host.World);
                Assert.Throws<InvalidOperationException>(() => host.Step());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void AWarmStepAllocatesNothing()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity, 2, 4);
            CanonRun.Run(world, world.Step);
            var health = pack.Lane("Health");
            var fire = pack.Channel("Fire");
            for (var f = 0; f < 256; f++)
            {
                world.Writer(f & 1).Send(0, fire, 3);
                world.Step();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 16; f++)
            {
                world.Writer(f & 1).Send(0, fire, 3);
                world.Step();
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.GreaterOrEqual(world.Get(0, health), 0);
        }
    }
}
