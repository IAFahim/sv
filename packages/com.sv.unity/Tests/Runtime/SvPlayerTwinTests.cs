using System;
using NUnit.Framework;

namespace Sv.Unity.PlayTests
{
    public sealed unsafe class SvPlayerTwinHost : IDisposable
    {
        const int HpColumn = 1;
        const int ManaColumn = 5;
        const int ManaCeiling = 50;

        readonly Board board;
        readonly int[] hp = new int[512];
        readonly int[] hpDelta = new int[512];
        readonly int[] mana = new int[512];
        readonly int[] manaDelta = new int[512];
        readonly long[] inbox = new long[512];
        readonly float[] maxHp = new float[512];
        readonly StatOp[] ops = new StatOp[256];
        readonly StatOp[] scratch = new StatOp[256];
        readonly StatWrite[] writes = new StatWrite[1024];
        readonly int rows;
        int opCount;
        long tick;

        public SvPlayerTwinHost(Schema schema, int rows)
        {
            this.rows = rows;
            board = Board.Create(schema, 512);
            board.BindSink(schema.Stat("max_hp"));
            for (var i = 0; i < rows; i++)
            {
                Assert.AreEqual(i, board.Acquire());
                hp[i] = 50 + i % 40;
                mana[i] = 10;
                maxHp[i] = 100f;
            }
        }

        public void AddDelta(int row, int intrinsic, int amount)
        {
            if (intrinsic == 0)
            {
                hpDelta[row] += amount;
            }
            else
            {
                manaDelta[row] += amount;
            }
        }

        public void Send(int row, int amount) => inbox[row] += amount;

        public void Enqueue(StatOp op) => ops[opCount++] = op;

        public void Frame()
        {
            board.ExpireModifiers(tick);
            if (opCount > 0)
            {
                StatOps.Canonicalize(new Span<StatOp>(ops, 0, opCount), new Span<StatOp>(scratch, 0, opCount));
                board.Apply(new ReadOnlySpan<StatOp>(ops, 0, opCount));
                opCount = 0;
            }

            board.Recalculate();
            int taken;
            while ((taken = board.TakeBroadcast(writes)) > 0)
            {
                for (var i = 0; i < taken; i++)
                {
                    var bits = writes[i].Bits;
                    maxHp[writes[i].Slot] = *(float*)&bits;
                }
            }

            fixed (int* h = hp, hd = hpDelta, m = mana, md = manaDelta)
            fixed (long* box = inbox)
            fixed (float* max = maxHp)
            {
                for (var start = 0; start < rows; start += 64)
                {
                    var count = Math.Min(64, rows - start);
                    var hpWord = new FoldWord { Value = h + start, Delta = hd + start, Ceiling = max + start, FloorConstant = 0 };
                    Fold.Word(in hpWord, box + start, count);
                    var manaWord = new FoldWord { Value = m + start, Delta = md + start, FloorConstant = 0, CeilingConstant = ManaCeiling };
                    Fold.Word(in manaWord, count);
                }
            }

            tick++;
        }

        public ulong Digest()
        {
            var cells = 0ul;
            unchecked
            {
                for (var i = 0; i < rows; i++)
                {
                    cells += CellDigest.Key(HpColumn, i, (uint)hp[i]);
                    cells += CellDigest.Key(ManaColumn, i, (uint)mana[i]);
                }

                return board.Digest + cells;
            }
        }

        public void Dispose() => board.Dispose();
    }

    public class SvPlayerTwinTests
    {
        public const ulong NetPinned = 0xDA47AD9B5CD74335ul;
        const int Rows = 300;
        const int Ticks = 40;

        static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15ul;
                var z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        static int Range(ref ulong state, int low, int high) => low + (int)(Next(ref state) % (ulong)(high - low));

        static ulong Run(SvPlayerTwinHost host, StatId maxHp)
        {
            var state = 0x7714EEDul;
            var combined = 0xCBF29CE484222325ul;
            host.Frame();
            for (var tick = 0; tick < Ticks; tick++)
            {
                for (var row = 0; row < Rows; row++)
                {
                    var r = Next(ref state);
                    if ((r & 3) == 0)
                    {
                        host.AddDelta(row, 0, Range(ref state, -45, 40));
                    }

                    if ((r & 4) != 0)
                    {
                        host.AddDelta(row, 1, Range(ref state, -30, 31));
                    }
                }

                var sends = Range(ref state, 0, 60);
                for (var s = 0; s < sends; s++)
                {
                    host.Send(Range(ref state, 0, Rows), Range(ref state, -25, 26));
                }

                if (tick % 5 == 0)
                {
                    var row = Range(ref state, 0, Rows);
                    host.Enqueue(StatOp.Add(row, maxHp, ModifierKind.Added, Range(ref state, -60, 60), 1000 + tick));
                }

                if (tick % 7 == 3)
                {
                    host.Enqueue(StatOp.RemoveSource(Range(ref state, 0, Rows), 1000 + tick - 3));
                }

                host.Frame();
                unchecked
                {
                    combined = (combined ^ host.Digest()) * 0x100000001B3ul;
                }
            }

            return combined;
        }

        [Test]
        public void TheManagedSvCoreFoldScenarioOnThisRuntimeEqualsTheCoreClrPin()
        {
            var schema = SchemaBuilder.Create().Stat("max_hp", 100f).Intrinsic("hp").Intrinsic("mana").Build();
            using var host = new SvPlayerTwinHost(schema, Rows);
            var digest = Run(host, schema.Stat("max_hp"));
            var runtime = Type.GetType("Mono.Runtime") != null ? "mono" : System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            UnityEngine.Debug.Log($"svplayertwin runtime={runtime} player={UnityEngine.Application.platform} digest={digest:X16}");
            Assert.AreEqual(NetPinned, digest);
        }
    }
}
