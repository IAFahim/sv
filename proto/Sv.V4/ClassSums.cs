using System;
#if NET
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Sv.V4
{
    public unsafe struct ClassSumBlocks
    {
        public const int BlockLongs = SumClass.Count * LaneWorld.Rows;
        public long** BlockOfChunk;
        public long* Pool;
        public int* Touched;
        public int TouchedCount;

        public static ClassSumBlocks* Create(int chunks)
        {
            var blocks = (ClassSumBlocks*)Memory.AllocZeroed(1, (nuint)sizeof(ClassSumBlocks));
            blocks->BlockOfChunk = (long**)Memory.AllocZeroed((nuint)chunks, (nuint)sizeof(long*));
            blocks->Pool = (long*)Memory.Alloc((nuint)((long)chunks * BlockLongs), sizeof(long));
            blocks->Touched = (int*)Memory.Alloc((nuint)chunks, sizeof(int));
            return blocks;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static long* BlockToWrite(ClassSumBlocks* blocks, int chunk)
        {
            var block = blocks->BlockOfChunk[chunk];
            if (block != null)
            {
                return block;
            }

            block = blocks->Pool + (long)blocks->TouchedCount * BlockLongs;
            Memory.Clear(block, BlockLongs * sizeof(long));
            blocks->BlockOfChunk[chunk] = block;
            blocks->Touched[blocks->TouchedCount++] = chunk;
            return block;
        }

        public static void Forget(ClassSumBlocks* blocks)
        {
            for (var at = 0; at < blocks->TouchedCount; at++)
            {
                blocks->BlockOfChunk[blocks->Touched[at]] = null;
            }

            blocks->TouchedCount = 0;
        }

        public static void Free(ClassSumBlocks* blocks)
        {
            Memory.Free(blocks->BlockOfChunk);
            Memory.Free(blocks->Pool);
            Memory.Free(blocks->Touched);
            Memory.Free(blocks);
        }
    }

    public static unsafe class RecordReducer
    {
        public static long ReduceIntoClassSums(in LaneView world, byte* sourceFlagsBySlot, Effect* records, int count, ClassSumBlocks* sums)
        {
            var slotLimit = (uint)world.SlotLimit;
            var unsupported = 0L;
            var lastChunk = -1;
            long* block = null;
            var at = 0;
#if NET
            if (Avx2.IsSupported)
            {
                at = ReduceFourAtATime(slotLimit, sourceFlagsBySlot, records, count, sums, ref lastChunk, ref block);
            }
#endif
            for (; at < count; at++)
            {
                var key = records[at].Key;
                var hits = records[at].Hits;
                var target = (uint)(key >> EffectKey.TargetShift);
                var source = (int)(key >> EffectKey.SourceShift) & EffectKey.SlotMask;
                var channel = (uint)(byte)(key >> EffectKey.ChannelShift);
                var kind = (uint)(byte)(key >> EffectKey.KindShift);
                var offered = ClampTerm((long)records[at].Amount * hits);
                var isTick = channel == Channel.Tick ? 1L : 0L;
                var term = offered + ((hits - offered) & -isTick);
                var isPhysical = channel == Channel.Physical ? 1 : 0;
                var executes = sourceFlagsBySlot[source] & SourceFlag.Executes & isPhysical;
                var loss = (int)((ulong)term >> 63);
                if ((target < slotLimit & channel < Channel.Count & kind < SumClass.Kinds) == false)
                {
                    unsupported++;
                    continue;
                }

                var chunk = (int)(target >> 6);
                if (chunk != lastChunk)
                {
                    block = ClassSumBlocks.BlockToWrite(sums, chunk);
                    lastChunk = chunk;
                }

                block[(SumClass.Of((int)channel, (int)kind, executes, loss) << 6) | (int)(target & 63)] += term;
            }

            return unsupported;
        }

#if NET
        static int ReduceFourAtATime(uint slotLimit, byte* sourceFlagsBySlot, Effect* records, int count, ClassSumBlocks* sums, ref int lastChunk, ref long* block)
        {
            var cells = stackalloc long[4];
            var terms = stackalloc long[4];
            var chunks = stackalloc long[4];
            var termLimit = Vector256.Create(EffectLaw.TermLimit);
            var negativeTermLimit = Vector256.Create(-EffectLaw.TermLimit);
            var byteMask = Vector256.Create(0xFFL);
            var slotMask = Vector256.Create((long)EffectKey.SlotMask);
            var one = Vector256.Create(1L);
            var oneInt = Vector256.Create(1);
            var tick = Vector256.Create((long)Channel.Tick);
            var channelLimit = Vector256.Create((long)Channel.Count);
            var kindLimit = Vector256.Create((long)SumClass.Kinds);
            var targetLimit = Vector256.Create((long)slotLimit);
            var at = 0;
            for (; at + 4 <= count; at += 4)
            {
                var firstPair = Avx.LoadVector256((long*)(records + at));
                var secondPair = Avx.LoadVector256((long*)(records + at + 2));
                var keys = Avx2.UnpackLow(firstPair, secondPair);
                var amountAndHits = Avx2.UnpackHigh(firstPair, secondPair);
                var hitsLow = Avx2.ShiftRightLogical(amountAndHits, 32);
                var hits = Avx2.Multiply(hitsLow.AsInt32(), oneInt);
                var offered = Vector256.Min(Vector256.Max(Avx2.Multiply(amountAndHits.AsInt32(), hitsLow.AsInt32()), negativeTermLimit), termLimit);
                var channel = Avx2.ShiftRightLogical(keys, EffectKey.ChannelShift) & byteMask;
                var kind = Avx2.ShiftRightLogical(keys, EffectKey.KindShift) & byteMask;
                var term = Avx2.BlendVariable(offered, hits, Avx2.CompareEqual(channel, tick));
                var source = Avx2.ShiftRightLogical(keys, EffectKey.SourceShift) & slotMask;
                var flags = Avx2.ConvertToVector256Int64(Avx2.GatherVector128((int*)sourceFlagsBySlot, source, 1));
                var executes = flags & Avx2.CompareEqual(channel, Vector256<long>.Zero) & one;
                var loss = Avx2.ShiftRightLogical(term, 63);
                var target = Avx2.ShiftRightLogical(keys, EffectKey.TargetShift);
                var sumClass = Avx2.ShiftLeftLogical(channel, 4) | Avx2.ShiftLeftLogical(kind, 2) | Avx2.ShiftLeftLogical(executes, 1) | loss;
                var cell = Avx2.ShiftLeftLogical(sumClass, 6) | (target & Vector256.Create(63L));
                var valid = Avx2.CompareGreaterThan(channelLimit, channel) & Avx2.CompareGreaterThan(kindLimit, kind) & Avx2.CompareGreaterThan(targetLimit, target);
                var chunk = Avx2.ShiftRightLogical(target, 6);
                var sameChunk = Avx2.CompareEqual(chunk, Vector256.Create((long)lastChunk));
                if (Avx2.MoveMask((valid & sameChunk).AsByte()) != -1)
                {
                    if (Avx2.MoveMask(valid.AsByte()) != -1)
                    {
                        return at;
                    }

                    cell.Store(cells);
                    term.Store(terms);
                    chunk.Store(chunks);
                    for (var lane = 0; lane < 4; lane++)
                    {
                        if ((int)chunks[lane] != lastChunk)
                        {
                            lastChunk = (int)chunks[lane];
                            block = ClassSumBlocks.BlockToWrite(sums, lastChunk);
                        }

                        block[cells[lane]] += terms[lane];
                    }

                    continue;
                }

                cell.Store(cells);
                term.Store(terms);
                block[cells[0]] += terms[0];
                block[cells[1]] += terms[1];
                block[cells[2]] += terms[2];
                block[cells[3]] += terms[3];
            }

            return at;
        }
#endif

        static long ClampTerm(long product)
        {
            var above = product - EffectLaw.TermLimit;
            product -= above & ~(above >> 63);
            var below = product + EffectLaw.TermLimit;
            return product - (below & (below >> 63));
        }
    }

    public static unsafe class SourceFlag
    {
        public const int Executes = 1;
        public const int SlotSpace = EffectKey.SlotMask + 1;

        public static byte* Build(LaneWorld world)
        {
            var flags = (byte*)Memory.AllocZeroed(SlotSpace + 4, 1);
            Refresh(world, flags);
            return flags;
        }

        public static void Refresh(LaneWorld world, byte* flags)
        {
            for (var slot = 0; slot < world.SlotLimit; slot++)
            {
                flags[slot] = (byte)(world.Read(slot, Lane.Execute) != 0 ? Executes : 0);
            }
        }
    }

    public static class EmitClass
    {
        public const int PoisonGain = 0;
        public const int PoisonLoss = 1;
        public const int Healing = 2;
        public const int Count = 3;
        public const int BlockLongs = Count * LaneWorld.Rows;

        public static int SumClassOf(int emitClass) =>
            emitClass == PoisonGain ? SumClass.PoisonOf(0, 0) : emitClass == PoisonLoss ? SumClass.PoisonOf(0, 1) : SumClass.HealingOf(0);
    }
}
