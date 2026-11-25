using System;
using System.Collections;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sv.Columns.Canon;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

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
            Debug.Log("SVCOLUMNS runtime " + RuntimeInformation.FrameworkDescription + " " + RuntimeInformation.ProcessArchitecture + ", kernels " + world.KernelPath + ", Vector<int> " + Vector<int>.Count + (Vector.IsHardwareAccelerated ? " accelerated" : " software") + ", runtime Avx2 " + (supported ?? "absent") + ", job workers " + JobsUtility.JobWorkerCount);
            Debug.Log("SVCOLUMNS defines " + string.Join(" ", UnityEditor.EditorUserBuildSettings.activeScriptCompilationDefines));
            Assert.AreEqual(pack.TemplateNames.Count, 9);
        }

        [TestCase(KernelChoice.Scalar, 1, 1)]
        [TestCase(KernelChoice.Best, 1, 1)]
        [TestCase(KernelChoice.Best, 3, 7)]
        [TestCase(KernelChoice.Vector, 1, 1)]
        [TestCase(KernelChoice.Vector, 2, 5)]
        public void TheCanonRunHasTheDotNetDigest(KernelChoice kernels, int writers, int parts)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(world, world.Step)));
        }

        [TestCase(KernelChoice.Scalar)]
        [TestCase(KernelChoice.Best)]
        [TestCase(KernelChoice.Vector)]
        public void TheCanonRunOnJobsHasTheDotNetDigest(KernelChoice kernels)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var world = new ColumnWorld(pack, CanonRun.Capacity, 3, (JobsUtility.JobWorkerCount + 1) * 4, kernels);
            using var stepper = new ColumnStepper(world);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(world, stepper.Step)));
        }

        [Test]
        public void TheHostRunsTheCanonAndCloses()
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
                host.Close();
                Assert.IsNull(host.World);
                Assert.Throws<InvalidOperationException>(() => host.Step());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator InPlayModeTheHostStepsInLateUpdateAndClosesWithItsObject()
        {
            yield return new EnterPlayMode();
            var host = new GameObject("columns").AddComponent<ColumnHost>();
            host.Open(Arena(), CanonRun.Capacity, 2);
            var stepped = 0;
            host.Stepped += _ => stepped++;
            var spawned = host.World!.Spawn(host.World.Pack.Template("burning"));
            var health = host.World.Pack.Lane("Health");
            for (var f = 0; f < 4; f++)
            {
                yield return null;
            }

            Assert.Greater(stepped, 0);
            Assert.Less(host.World.Get(spawned, health), 100);
            UnityEngine.Object.Destroy(host.gameObject);
            yield return null;
            Assert.IsNull(host.World);
            yield return new ExitPlayMode();
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
            void Frames(int count)
            {
                for (var f = 0; f < count; f++)
                {
                    world.Writer(f & 1).Send(0, fire, 3);
                    world.Step();
                }
            }

            Frames(256);
            var windows = new long[5];
            var start = GC.GetAllocatedBytesForCurrentThread();
            windows[0] = GC.GetAllocatedBytesForCurrentThread() - start;
            for (var w = 1; w < windows.Length; w++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Frames(64);
                windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Debug.Log("SVCOLUMNS warm allocation per 64 frames, after an empty window of " + windows[0] + " B: " + string.Join(", ", windows, 1, windows.Length - 1) + " B");
            Assert.AreEqual(0L, windows[windows.Length - 1] - windows[0]);
            Assert.GreaterOrEqual(world.Get(0, health), 0);
        }
    }
}
