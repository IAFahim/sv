using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Sv.Unity.Tests
{
    public unsafe class CorePinTests
    {
        public const ulong BakedFold = 0x0D945D8FD131DA2Aul;
        public const ulong BakedLinks = 0xA47434070C42E4ACul;
        const int Rows = 4099;
        const int Ticks = 8;

        static ulong Next(ref ulong state)
        {
            unchecked
            {
                var z = state += 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        static T* Alloc<T>(int count) where T : unmanaged
        {
            var p = (T*)UnsafeUtility.Malloc(count * (long)sizeof(T), 64, Allocator.Persistent);
            UnsafeUtility.MemClear(p, count * (long)sizeof(T));
            return p;
        }

        [Test]
        public void TheFoldScenarioOnTheUnitySvCoreBuildMatchesTheDotNetPin()
        {
            var capacity = ((Rows + 63) >> 6) << 6;
            var value = Alloc<int>(capacity);
            var delta = Alloc<int>(capacity);
            var floor = Alloc<int>(capacity);
            var ceiling = Alloc<int>(capacity);
            var changed = Alloc<ulong>(capacity >> 6);
            using var inbox = new InboxBuffer(capacity);
            var state = 0x5EEDF01Dul;
            var digest = 0ul;
            for (var tick = 0; tick < Ticks; tick++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    var a = Next(ref state);
                    if (tick == 0)
                    {
                        value[r] = (int)(a % 2000) - 500;
                        floor[r] = (int)((a >> 16) % 100) - 50;
                        ceiling[r] = (r % 11 == 0) ? floor[r] - 3 : floor[r] + 100 + (int)((a >> 24) % 1500);
                    }

                    delta[r] = (r % 5 == 0) ? 0 : (int)(Next(ref state) % 400) - 200;
                    if (r % 9 == 0)
                    {
                        Inbox.Send(inbox.Pointer, (int)(a % (ulong)Rows), (int)(a >> 40) % 1000);
                    }
                }

                Kernel.Fold(new Column<int>(value, capacity), Window.Range(0, Rows), new Column<int>(delta, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed, capacity >> 6), inbox.Column);
                digest = unchecked(digest * 0x100000001B3ul + CellDigest.Sum(1, value, 0, Rows));
                for (var w = 0; w < capacity >> 6; w++)
                {
                    digest = unchecked((digest ^ changed[w]) * 0x100000001B3ul);
                }
            }

            UnsafeUtility.Free(value, Allocator.Persistent);
            UnsafeUtility.Free(delta, Allocator.Persistent);
            UnsafeUtility.Free(floor, Allocator.Persistent);
            UnsafeUtility.Free(ceiling, Allocator.Persistent);
            UnsafeUtility.Free(changed, Allocator.Persistent);
            Assert.AreEqual(BakedFold, digest);
        }

        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct FoldWordJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public int* Value;
            [NativeDisableUnsafePtrRestriction] public int* Delta;
            [NativeDisableUnsafePtrRestriction] public float* Floor;
            [NativeDisableUnsafePtrRestriction] public float* Ceiling;
            [NativeDisableUnsafePtrRestriction] public long* Inbox;
            [NativeDisableUnsafePtrRestriction] public ulong* Changed;
            [NativeDisableUnsafePtrRestriction] public int* Probe;
            public int Rows;

            public void Execute(int word)
            {
                Mark(Probe);
                var start = word << 6;
                var count = Rows - start < 64 ? Rows - start : 64;
                var fold = new FoldWord { Value = Value + start, Delta = Delta + start, Floor = Floor + start, Ceiling = Ceiling + start };
                Changed[word] = Fold.Word(in fold, Inbox + start, count);
            }

            static void Mark(int* probe)
            {
                *probe = 1;
                Managed(probe);
            }

            [BurstDiscard]
            static void Managed(int* probe) => *probe = 2;
        }

        [Test]
        public void TheFoldScenarioThroughABurstJobMatchesTheDotNetPin()
        {
            var capacity = ((Rows + 63) >> 6) << 6;
            var words = capacity >> 6;
            var value = Alloc<int>(capacity);
            var delta = Alloc<int>(capacity);
            var floor = Alloc<float>(capacity);
            var ceiling = Alloc<float>(capacity);
            var changed = Alloc<ulong>(words);
            var probe = Alloc<int>(1);
            using var inbox = new InboxBuffer(capacity);
            var state = 0x5EEDF01Dul;
            var digest = 0ul;
            for (var tick = 0; tick < Ticks; tick++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    var a = Next(ref state);
                    if (tick == 0)
                    {
                        value[r] = (int)(a % 2000) - 500;
                        var f = (int)((a >> 16) % 100) - 50;
                        floor[r] = f;
                        ceiling[r] = (r % 11 == 0) ? f - 3 : f + 100 + (int)((a >> 24) % 1500);
                    }

                    delta[r] = (r % 5 == 0) ? 0 : (int)(Next(ref state) % 400) - 200;
                    if (r % 9 == 0)
                    {
                        Inbox.Send(inbox.Pointer, (int)(a % (ulong)Rows), (int)(a >> 40) % 1000);
                    }
                }

                new FoldWordJob { Value = value, Delta = delta, Floor = floor, Ceiling = ceiling, Inbox = (long*)inbox.Pointer, Changed = changed, Probe = probe, Rows = Rows }.Schedule(words, 1).Complete();
                digest = unchecked(digest * 0x100000001B3ul + CellDigest.Sum(1, value, 0, Rows));
                for (var w = 0; w < words; w++)
                {
                    digest = unchecked((digest ^ changed[w]) * 0x100000001B3ul);
                }
            }

            var ran = *probe;
            UnsafeUtility.Free(value, Allocator.Persistent);
            UnsafeUtility.Free(delta, Allocator.Persistent);
            UnsafeUtility.Free(floor, Allocator.Persistent);
            UnsafeUtility.Free(ceiling, Allocator.Persistent);
            UnsafeUtility.Free(changed, Allocator.Persistent);
            UnsafeUtility.Free(probe, Allocator.Persistent);
            Assert.AreEqual(1, ran, "the fold job did not run as a Burst job");
            Assert.AreEqual(BakedFold, digest);
        }

        [Test]
        public void TheNorthStar2LinkTableHashMatchesTheDotNetPin()
        {
            var columns = new[]
            {
                new ColumnDef(0x4D41585F4850, ColumnRole.StatOutput, 0, 0),
                new ColumnDef(0x41524D4F52, ColumnRole.StatOutput, 1, 0),
                new ColumnDef(0x4850, ColumnRole.Intrinsic, 0, 0),
                new ColumnDef(0x48504454, ColumnRole.Delta, 0, 2),
                new ColumnDef(0x4850494E, ColumnRole.Inbox, 0, 2),
                new ColumnDef(0x4C4F57, ColumnRole.Condition, 0, 0),
            };
            var table = new[]
            {
                new Link(1, 3, LinkMode.Delta),
                new Link(2, 3, LinkMode.Delta, LinkScope.Remote),
                new Link(2, 4, LinkMode.Delta, LinkScope.Remote),
                new Link(3, 1, LinkMode.Modifier),
                new Link(4, 5, LinkMode.Event),
                new Link(4, 0, LinkMode.Read),
                new Link(4, 2, LinkMode.Check),
            };
            Assert.AreEqual(LinkFault.None, Links.Validate(table, columns));
            Assert.AreEqual(BakedLinks, Links.Hash(table, columns));
        }
    }
}
