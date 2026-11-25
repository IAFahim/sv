using System;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.NoInlining)]
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

    public unsafe struct SlotTables
    {
        public const int SlotSpace = EffectKey.SlotMask + 1;
        public uint* Contexts;
        public ulong* ExecuteBits;

        public static SlotTables Build(LaneWorld world)
        {
            var tables = new SlotTables
            {
                Contexts = (uint*)Memory.AllocZeroed(SlotSpace, sizeof(uint)),
                ExecuteBits = (ulong*)Memory.AllocZeroed(EffectKey.ChunkLimit, sizeof(ulong)),
            };
            for (var slot = 0; slot < world.SlotLimit; slot++)
            {
                tables.Contexts[slot] = TargetContext.Of(world.Read(slot, Lane.Health), world.Read(slot, Lane.MaxHealth), world.Read(slot, Lane.ImmuneMask), world.Read(slot, Lane.KindMask));
                tables.ExecuteBits[slot >> EffectKey.RowBits] |= (world.Read(slot, Lane.Execute) != 0 ? 1ul : 0ul) << (slot & EffectKey.RowMask);
            }

            return tables;
        }

        public void Free()
        {
            Memory.Free(Contexts);
            Memory.Free(ExecuteBits);
        }
    }

    public unsafe struct ReduceCursor
    {
        public ClassSumBlocks* Sums;
        public long* Block;
        public int Chunk;
        public long Unsupported;
    }

    public static unsafe class RecordReducer
    {
        const int Rows = LaneWorld.Rows;

        public static long ReduceIntoClassSums(uint slotLimit, SlotTables tables, Effect* records, int count, ClassSumBlocks* sums, bool runs)
        {
            var cursor = new ReduceCursor { Sums = sums, Chunk = -1 };
            var at = 0;
#if NET
            if (runs && Avx512F.IsSupported && Vector512.IsHardwareAccelerated)
            {
                at = ReduceTargetRuns(slotLimit, tables, records, count, ref cursor);
            }
#endif
            for (; at < count; at++)
            {
                ReduceOne(slotLimit, tables, records[at], ref cursor);
            }

            return cursor.Unsupported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ReduceOne(uint slotLimit, SlotTables tables, in Effect record, ref ReduceCursor cursor)
        {
            var key = record.Key;
            var hits = record.Hits;
            var target = (uint)(key >> EffectKey.TargetShift);
            var source = (int)(key >> EffectKey.SourceShift) & EffectKey.SlotMask;
            var channel = (uint)(byte)(key >> EffectKey.ChannelShift);
            var kind = (uint)(byte)(key >> EffectKey.KindShift);
            if ((target < slotLimit & channel < Channel.Count & kind < TargetContext.Kinds) == false)
            {
                cursor.Unsupported++;
                return;
            }

            var context = tables.Contexts[target];
            var offered = ClampTerm((long)record.Amount * hits);
            var isTick = channel == Channel.Tick ? 1L : 0L;
            var term = offered + ((hits - offered) & -isTick);
            var admitted = (long)((context >> TargetContext.AdmitShift((int)channel, (int)kind)) & 1u);
            var isPhysical = channel == Channel.Physical ? 1u : 0u;
            var executes = (uint)(tables.ExecuteBits[source >> EffectKey.RowBits] >> (source & EffectKey.RowMask)) & 1u;
            var doubles = (int)((context >> TargetContext.BelowExecuteLine) & executes & isPhysical);
            term = (term << doubles) & -admitted;
            var chunk = (int)(target >> EffectKey.RowBits);
            if (chunk != cursor.Chunk)
            {
                cursor.Block = ClassSumBlocks.BlockToWrite(cursor.Sums, chunk);
                cursor.Chunk = chunk;
            }

            var loss = (int)((ulong)term >> 63);
            cursor.Block[(SumClass.Of((int)channel, loss) << EffectKey.RowBits) | (int)(target & EffectKey.RowMask)] += term;
        }

#if NET
        static int ReduceTargetRuns(uint slotLimit, SlotTables tables, Effect* records, int count, ref ReduceCursor cursor)
        {
            var keyIndex = Vector512.Create(0L, 2, 4, 6, 8, 10, 12, 14);
            var pairIndex = Vector512.Create(1L, 3, 5, 7, 9, 11, 13, 15);
            var termLimit = Vector512.Create(EffectLaw.TermLimit);
            var negativeTermLimit = Vector512.Create(-EffectLaw.TermLimit);
            var byteMask = Vector512.Create(0xFFL);
            var slotMask = Vector512.Create((long)EffectKey.SlotMask);
            var tick = Vector512.Create((long)Channel.Tick);
            var channelLimit = Vector512.Create((ulong)Channel.Count);
            var kindLimit = Vector512.Create((ulong)TargetContext.Kinds);
            var physical = Vector512.Create((long)Channel.Physical);
            var poison = Vector512.Create((long)Channel.Poison);
            var fire = Vector512.Create((long)Channel.Fire);
            var healing = Vector512.Create((long)Channel.Healing);
            var at = 0;
            while (at + 8 <= count)
            {
                var target = (uint)(records[at].Key >> EffectKey.TargetShift);
                if (target >= slotLimit || target != (uint)(records[at + 7].Key >> EffectKey.TargetShift))
                {
                    ReduceMixedBatch(slotLimit, tables, records + at, ref cursor);
                    at += 8;
                    continue;
                }

                var context = tables.Contexts[target];
                var runTarget = Vector512.Create((long)target);
                var runContext = Vector512.Create((long)context);
                var below = (context >> TargetContext.BelowExecuteLine) & 1u;
                var zero = Vector512<long>.Zero;
                Vector512<long> physicalAll = zero, physicalLoss = zero, poisonAll = zero, poisonLoss = zero, fireAll = zero, fireLoss = zero, healingAll = zero, tickAll = zero;
                var batches = 0;
                while (at + 8 <= count)
                {
                    var first = Vector512.Load((long*)(records + at));
                    var second = Vector512.Load((long*)(records + at + 4));
                    var keys = Avx512F.PermuteVar8x64x2(first, keyIndex, second);
                    var channel = Vector512.ShiftRightLogical(keys, EffectKey.ChannelShift) & byteMask;
                    var kind = Vector512.ShiftRightLogical(keys, EffectKey.KindShift) & byteMask;
                    var sameTarget = Vector512.Equals(Vector512.ShiftRightLogical(keys, EffectKey.TargetShift), runTarget);
                    var supported = Vector512.LessThan(channel.AsUInt64(), channelLimit) & Vector512.LessThan(kind.AsUInt64(), kindLimit);
                    if (!Vector512.EqualsAll(sameTarget & supported.AsInt64(), Vector512<long>.AllBitsSet))
                    {
                        break;
                    }

                    var amountAndHits = Avx512F.PermuteVar8x64x2(first, pairIndex, second);
                    var hits = Vector512.ShiftRightArithmetic(amountAndHits, 32);
                    var offered = Vector512.Min(Vector512.Max(Avx512F.Multiply(amountAndHits.AsInt32(), hits.AsInt32()), negativeTermLimit), termLimit);
                    var isPhysical = Vector512.Equals(channel, physical);
                    var term = Vector512.ConditionalSelect(Vector512.Equals(channel, tick), hits, offered);
                    var admitShift = Vector512.ShiftLeft(channel, 2) | kind;
                    var admitted = Avx512F.ShiftRightLogicalVariable(runContext.AsUInt64(), admitShift.AsUInt64()).AsInt64() & Vector512<long>.One;
                    term &= Vector512.Equals(admitted, Vector512<long>.One);
                    if (below != 0u)
                    {
                        var sources = Vector512.ShiftRightLogical(keys, EffectKey.SourceShift) & slotMask;
                        var words = Vector512.ShiftRightLogical(sources, EffectKey.RowBits);
                        var lower = Avx2.GatherVector256((long*)tables.ExecuteBits, words.GetLower(), 8);
                        var upper = Avx2.GatherVector256((long*)tables.ExecuteBits, words.GetUpper(), 8);
                        var executes = Avx512F.ShiftRightLogicalVariable(Vector512.Create(lower, upper).AsUInt64(), (sources & Vector512.Create((long)EffectKey.RowMask)).AsUInt64()).AsInt64() & Vector512<long>.One;
                        term += term & isPhysical & Vector512.Equals(executes, Vector512<long>.One);
                    }

                    var lost = Vector512.Min(term, zero);
                    var onPoison = Vector512.Equals(channel, poison);
                    var onFire = Vector512.Equals(channel, fire);
                    physicalAll += term & isPhysical;
                    physicalLoss += lost & isPhysical;
                    poisonAll += term & onPoison;
                    poisonLoss += lost & onPoison;
                    fireAll += term & onFire;
                    fireLoss += lost & onFire;
                    healingAll += term & Vector512.Equals(channel, healing);
                    tickAll += term & Vector512.Equals(channel, tick);
                    batches++;
                    at += 8;
                }

                if (batches != 0)
                {
                    var chunk = (int)(target >> EffectKey.RowBits);
                    if (chunk != cursor.Chunk)
                    {
                        cursor.Block = ClassSumBlocks.BlockToWrite(cursor.Sums, chunk);
                        cursor.Chunk = chunk;
                    }

                    var row = cursor.Block + (target & EffectKey.RowMask);
                    var physicalLost = Vector512.Sum(physicalLoss);
                    var poisonLost = Vector512.Sum(poisonLoss);
                    var fireLost = Vector512.Sum(fireLoss);
                    row[SumClass.PhysicalGain * Rows] += Vector512.Sum(physicalAll) - physicalLost;
                    row[SumClass.PhysicalLoss * Rows] += physicalLost;
                    row[SumClass.PoisonGain * Rows] += Vector512.Sum(poisonAll) - poisonLost;
                    row[SumClass.PoisonLoss * Rows] += poisonLost;
                    row[SumClass.FireGain * Rows] += Vector512.Sum(fireAll) - fireLost;
                    row[SumClass.FireLoss * Rows] += fireLost;
                    row[SumClass.HealingGain * Rows] += Vector512.Sum(healingAll);
                    row[SumClass.TickGain * Rows] += Vector512.Sum(tickAll);
                    continue;
                }

                ReduceMixedBatch(slotLimit, tables, records + at, ref cursor);
                at += 8;
            }

            return at;
        }

        static void ReduceMixedBatch(uint slotLimit, SlotTables tables, Effect* records, ref ReduceCursor cursor)
        {
            var first = Vector512.Load((long*)records);
            var second = Vector512.Load((long*)(records + 4));
            var keys = Avx512F.PermuteVar8x64x2(first, Vector512.Create(0L, 2, 4, 6, 8, 10, 12, 14), second);
            var channel = Vector512.ShiftRightLogical(keys, EffectKey.ChannelShift) & Vector512.Create(0xFFL);
            var kind = Vector512.ShiftRightLogical(keys, EffectKey.KindShift) & Vector512.Create(0xFFL);
            var target = Vector512.ShiftRightLogical(keys, EffectKey.TargetShift);
            var supported = Vector512.LessThan(channel.AsUInt64(), Vector512.Create((ulong)Channel.Count))
                & Vector512.LessThan(kind.AsUInt64(), Vector512.Create((ulong)TargetContext.Kinds))
                & Vector512.LessThan(target.AsUInt64(), Vector512.Create((ulong)slotLimit));
            if (!Vector512.EqualsAll(supported, Vector512<ulong>.AllBitsSet))
            {
                for (var at = 0; at < 8; at++)
                {
                    ReduceOne(slotLimit, tables, records[at], ref cursor);
                }

                return;
            }

            var zero = Vector512<long>.Zero;
            var one = Vector512<long>.One;
            var amountAndHits = Avx512F.PermuteVar8x64x2(first, Vector512.Create(1L, 3, 5, 7, 9, 11, 13, 15), second);
            var hits = Vector512.ShiftRightArithmetic(amountAndHits, 32);
            var offered = Vector512.Min(Vector512.Max(Avx512F.Multiply(amountAndHits.AsInt32(), hits.AsInt32()), Vector512.Create(-EffectLaw.TermLimit)), Vector512.Create(EffectLaw.TermLimit));
            var term = Vector512.ConditionalSelect(Vector512.Equals(channel, Vector512.Create((long)Channel.Tick)), hits, offered);
            var table = tables.Contexts;
            var contexts = Vector512.Create(
                (long)table[target.GetElement(0)], table[target.GetElement(1)], table[target.GetElement(2)], table[target.GetElement(3)],
                table[target.GetElement(4)], table[target.GetElement(5)], table[target.GetElement(6)], table[target.GetElement(7)]);
            var admitted = Avx512F.ShiftRightLogicalVariable(contexts.AsUInt64(), (Vector512.ShiftLeft(channel, 2) | kind).AsUInt64()).AsInt64() & one;
            term &= Vector512.Equals(admitted, one);
            var doubling = Vector512.Equals(channel, Vector512.Create((long)Channel.Physical)) & Vector512.Equals(Vector512.ShiftRightLogical(contexts, TargetContext.BelowExecuteLine) & one, one);
            if (doubling != zero)
            {
                var sources = Vector512.ShiftRightLogical(keys, EffectKey.SourceShift) & Vector512.Create((long)EffectKey.SlotMask);
                var words = Vector512.ShiftRightLogical(sources, EffectKey.RowBits);
                var executeBits = tables.ExecuteBits;
                var bits = Vector512.Create(
                    executeBits[words.GetElement(0)], executeBits[words.GetElement(1)], executeBits[words.GetElement(2)], executeBits[words.GetElement(3)],
                    executeBits[words.GetElement(4)], executeBits[words.GetElement(5)], executeBits[words.GetElement(6)], executeBits[words.GetElement(7)]);
                var executes = Avx512F.ShiftRightLogicalVariable(bits.AsUInt64(), (sources & Vector512.Create((long)EffectKey.RowMask)).AsUInt64()).AsInt64() & one;
                term += term & doubling & Vector512.Equals(executes, one);
            }

            var loss = Vector512.ShiftRightLogical(term, 63);
            var cell = Vector512.ShiftLeft(Vector512.ShiftLeft(channel, 1) | loss, EffectKey.RowBits) | (target & Vector512.Create((long)EffectKey.RowMask));
            var chunk = Vector512.ShiftRightLogical(target, EffectKey.RowBits);
            if (Vector512.EqualsAll(chunk, Vector512.Create((long)cursor.Chunk)))
            {
                var block = cursor.Block;
                block[cell.GetElement(0)] += term.GetElement(0);
                block[cell.GetElement(1)] += term.GetElement(1);
                block[cell.GetElement(2)] += term.GetElement(2);
                block[cell.GetElement(3)] += term.GetElement(3);
                block[cell.GetElement(4)] += term.GetElement(4);
                block[cell.GetElement(5)] += term.GetElement(5);
                block[cell.GetElement(6)] += term.GetElement(6);
                block[cell.GetElement(7)] += term.GetElement(7);
                return;
            }

            var sums = cursor.Sums;
            AddToChunk(sums, chunk.GetElement(0), cell.GetElement(0), term.GetElement(0));
            AddToChunk(sums, chunk.GetElement(1), cell.GetElement(1), term.GetElement(1));
            AddToChunk(sums, chunk.GetElement(2), cell.GetElement(2), term.GetElement(2));
            AddToChunk(sums, chunk.GetElement(3), cell.GetElement(3), term.GetElement(3));
            AddToChunk(sums, chunk.GetElement(4), cell.GetElement(4), term.GetElement(4));
            AddToChunk(sums, chunk.GetElement(5), cell.GetElement(5), term.GetElement(5));
            AddToChunk(sums, chunk.GetElement(6), cell.GetElement(6), term.GetElement(6));
            AddToChunk(sums, chunk.GetElement(7), cell.GetElement(7), term.GetElement(7));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AddToChunk(ClassSumBlocks* sums, long chunk, long cell, long term)
        {
            var block = sums->BlockOfChunk[chunk];
            if (block == null)
            {
                block = ClassSumBlocks.BlockToWrite(sums, (int)chunk);
            }

            block[cell] += term;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long ClampTerm(long product)
        {
            var above = product - EffectLaw.TermLimit;
            product -= above & ~(above >> 63);
            var below = product + EffectLaw.TermLimit;
            return product - (below & (below >> 63));
        }
    }

    public static class EmitClass
    {
        public const int PoisonGain = 0;
        public const int PoisonLoss = 1;
        public const int Healing = 2;
        public const int Count = 3;
        public const int BlockLongs = Count * LaneWorld.Rows;
    }
}
