using System;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.V4
{
    public static unsafe class RecordFold
    {
        const int Rows = LaneWorld.Rows;

        public static void GatherClassSums(long* totals, ClassSumBlocks** writers, int writerCount, int chunk)
        {
            Memory.Clear(totals, ClassSumBlocks.BlockLongs * sizeof(long));
            for (var w = 0; w < writerCount; w++)
            {
                var block = writers[w]->BlockOfChunk[chunk];
                if (block != null)
                {
                    AddInto(totals, block, ClassSumBlocks.BlockLongs);
                }
            }
        }

        static void AddInto(long* into, long* from, int count)
        {
            var at = 0;
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (; at + 4 <= count; at += 4)
                {
                    (Vector256.Load(into + at) + Vector256.Load(from + at)).Store(into + at);
                }
            }
#endif
            for (; at < count; at++)
            {
                into[at] += from[at];
            }
        }

        public static void ResolveChunkScalar(int* lanes, long* totals, long* emittedLastFrame, long* emitNextFrame, SlotTables tables, int firstSlot)
        {
            for (var row = 0; row < Rows; row++)
            {
                ResolveRowLaw(lanes, totals, emittedLastFrame, emitNextFrame, row);
                RefreshSlot(lanes, tables, firstSlot, row);
            }
        }

        static void RefreshSlot(int* lanes, SlotTables tables, int firstSlot, int row)
        {
            tables.Contexts[firstSlot + row] = TargetContext.Of(lanes[Lane.Health * Rows + row], lanes[Lane.MaxHealth * Rows + row], lanes[Lane.ImmuneMask * Rows + row], lanes[Lane.KindMask * Rows + row]);
            var bit = 1ul << row;
            var word = tables.ExecuteBits + (firstSlot >> EffectKey.RowBits);
            *word = (*word & ~bit) | (lanes[Lane.Execute * Rows + row] != 0 ? bit : 0ul);
        }

        static long AllowedMask(int bits, int index) => ((bits >> index) & 1) == 0 ? -1L : 0L;

        static long Min(long a, long b) => a < b ? a : b;

        static long Max(long a, long b) => a > b ? a : b;

        static long Sum(long* totals, int sumClass, int row) => totals[sumClass * Rows + row];

        public static void ResolveRowLaw(int* lanes, long* totals, long* emittedLastFrame, long* emitNextFrame, int row)
        {
            var health = (long)lanes[Lane.Health * Rows + row];
            var maxHealth = (long)lanes[Lane.MaxHealth * Rows + row];
            var shield = (long)lanes[Lane.Shield * Rows + row];
            var immuneMask = lanes[Lane.ImmuneMask * Rows + row];
            var kindMask = lanes[Lane.KindMask * Rows + row];
            var fireHeals = lanes[Lane.Converter * Rows + row] > 0 ? -1L : 0L;
            var poisonTicks = (long)lanes[Lane.PoisonTicks * Rows + row];
            var regenCard = lanes[Lane.RegenCard * Rows + row];
            var regenRate = (long)lanes[Lane.RegenRate * Rows + row];
            var emitKindAllowed = AllowedMask(kindMask, 0);
            var poisonEmitAllowed = emitKindAllowed & AllowedMask(immuneMask, Channel.Poison);
            var healingEmitAllowed = emitKindAllowed & AllowedMask(immuneMask, Channel.Healing);

            var physicalGain = Sum(totals, SumClass.PhysicalGain, row);
            var physicalLoss = Sum(totals, SumClass.PhysicalLoss, row);
            var poisonGain = Sum(totals, SumClass.PoisonGain, row) + (emittedLastFrame[EmitClass.PoisonGain * Rows + row] & poisonEmitAllowed);
            var poisonLoss = Sum(totals, SumClass.PoisonLoss, row) + (emittedLastFrame[EmitClass.PoisonLoss * Rows + row] & poisonEmitAllowed);
            var fireGain = Sum(totals, SumClass.FireGain, row);
            var fireLoss = Sum(totals, SumClass.FireLoss, row);
            var healing = Sum(totals, SumClass.HealingGain, row) + Sum(totals, SumClass.HealingLoss, row) + (emittedLastFrame[EmitClass.Healing * Rows + row] & healingEmitAllowed);
            var tickHits = Sum(totals, SumClass.TickGain, row) + Sum(totals, SumClass.TickLoss, row);

            healing += (fireGain + fireLoss) & fireHeals;
            fireGain &= ~fireHeals;
            fireLoss &= ~fireHeals;

            var damageGain = physicalGain + poisonGain + fireGain;
            var damageLoss = physicalLoss + poisonLoss + fireLoss;
            var absorbed = Min(Max(shield, 0), damageGain);
            var damageTaken = damageGain + damageLoss - absorbed;
            var nextHealth = Min(Max(health + healing - damageTaken, 0), Max(maxHealth, 0));

            var ticks = (long)EffectLaw.Saturate32(tickHits);
            var poisoned = poisonTicks > 0 ? -1L : 0L;
            var poisonEmit = poisoned & EffectLaw.Term(Threshold.PoisonPerTickHit, (int)ticks);
            emitNextFrame[EmitClass.PoisonGain * Rows + row] = poisonEmit > 0 ? poisonEmit : 0;
            emitNextFrame[EmitClass.PoisonLoss * Rows + row] = poisonEmit < 0 ? poisonEmit : 0;
            emitNextFrame[EmitClass.Healing * Rows + row] = regenCard > 0 ? EffectLaw.Term((int)regenRate, (int)ticks) : 0;
            var nextPoisonTicks = poisoned != 0 ? Max(0, EffectLaw.Saturate32(poisonTicks - ticks)) : poisonTicks;

            var events = (nextHealth == 0 ? EventBit.Dead : 0) | (nextHealth * 100 < maxHealth * Threshold.LowHealthBelowPercent ? EventBit.LowHealth : 0);
            lanes[Lane.Health * Rows + row] = (int)nextHealth;
            lanes[Lane.Shield * Rows + row] = (int)(shield - absorbed);
            lanes[Lane.PoisonTicks * Rows + row] = (int)nextPoisonTicks;
            lanes[Lane.Events * Rows + row] = events;
        }

#if NET
        static Vector256<long> LoadWide(int* lanes, int lane, int row)
        {
            var four = Vector128.Load(lanes + lane * Rows + row);
            return Vector256.Create(Vector128.WidenLower(four), Vector128.WidenUpper(four));
        }

        static void StoreNarrow(Vector256<long> value, int* into) =>
            Vector128.Narrow(value.GetLower(), value.GetUpper()).Store(into);

        static Vector256<long> AllowedMask(Vector256<long> bits, int index) =>
            Vector256.Equals(Vector256.ShiftRightLogical(bits, index) & Vector256<long>.One, Vector256<long>.Zero);

        static Vector256<long> Sums(long* totals, int sumClass, int row) => Vector256.Load(totals + sumClass * Rows + row);

        static Vector256<long> Emitted(long* emitted, int emitClass, int row) => Vector256.Load(emitted + emitClass * Rows + row);

        static Vector256<long> Saturate32(Vector256<long> value) =>
            Vector256.Min(Vector256.Max(value, Vector256.Create((long)int.MinValue)), Vector256.Create((long)int.MaxValue));

        static Vector256<long> Term(Vector256<long> amount, Vector256<long> hits) =>
            Vector256.Min(Vector256.Max(amount * hits, Vector256.Create(-EffectLaw.TermLimit)), Vector256.Create(EffectLaw.TermLimit));

        static Vector256<long> Admits(Vector256<long> immuneMask, Vector256<long> kindMask)
        {
            var channels = Vector256.AndNot(Vector256.Create(31L), immuneMask);
            var kinds = Vector256.AndNot(Vector256.Create(15L), kindMask);
            var spread = (channels & Vector256.Create(1L))
                | Vector256.ShiftLeft(channels & Vector256.Create(2L), 3)
                | Vector256.ShiftLeft(channels & Vector256.Create(4L), 6)
                | Vector256.ShiftLeft(channels & Vector256.Create(8L), 9)
                | Vector256.ShiftLeft(channels & Vector256.Create(16L), 12);
            return kinds * spread;
        }

        public static void ResolveChunkVector(int* lanes, long* totals, long* emittedLastFrame, long* emitNextFrame, SlotTables tables, int firstSlot)
        {
            var zero = Vector256<long>.Zero;
            for (var row = 0; row < Rows; row += 4)
            {
                var health = LoadWide(lanes, Lane.Health, row);
                var maxHealth = LoadWide(lanes, Lane.MaxHealth, row);
                var shield = LoadWide(lanes, Lane.Shield, row);
                var immuneMask = LoadWide(lanes, Lane.ImmuneMask, row);
                var kindMask = LoadWide(lanes, Lane.KindMask, row);
                var fireHeals = Vector256.GreaterThan(LoadWide(lanes, Lane.Converter, row), zero);
                var poisonTicks = LoadWide(lanes, Lane.PoisonTicks, row);
                var regenCard = Vector256.GreaterThan(LoadWide(lanes, Lane.RegenCard, row), zero);
                var regenRate = LoadWide(lanes, Lane.RegenRate, row);
                var emitKindAllowed = AllowedMask(kindMask, 0);
                var poisonEmitAllowed = emitKindAllowed & AllowedMask(immuneMask, Channel.Poison);
                var healingEmitAllowed = emitKindAllowed & AllowedMask(immuneMask, Channel.Healing);

                var physicalGain = Sums(totals, SumClass.PhysicalGain, row);
                var physicalLoss = Sums(totals, SumClass.PhysicalLoss, row);
                var poisonGain = Sums(totals, SumClass.PoisonGain, row) + (Emitted(emittedLastFrame, EmitClass.PoisonGain, row) & poisonEmitAllowed);
                var poisonLoss = Sums(totals, SumClass.PoisonLoss, row) + (Emitted(emittedLastFrame, EmitClass.PoisonLoss, row) & poisonEmitAllowed);
                var fireGain = Sums(totals, SumClass.FireGain, row);
                var fireLoss = Sums(totals, SumClass.FireLoss, row);
                var healing = Sums(totals, SumClass.HealingGain, row) + Sums(totals, SumClass.HealingLoss, row) + (Emitted(emittedLastFrame, EmitClass.Healing, row) & healingEmitAllowed);
                var tickHits = Sums(totals, SumClass.TickGain, row) + Sums(totals, SumClass.TickLoss, row);

                healing += (fireGain + fireLoss) & fireHeals;
                fireGain = Vector256.AndNot(fireGain, fireHeals);
                fireLoss = Vector256.AndNot(fireLoss, fireHeals);

                var damageGain = physicalGain + poisonGain + fireGain;
                var damageLoss = physicalLoss + poisonLoss + fireLoss;
                var absorbed = Vector256.Min(Vector256.Max(shield, zero), damageGain);
                var damageTaken = damageGain + damageLoss - absorbed;
                var nextHealth = Vector256.Min(Vector256.Max(health + healing - damageTaken, zero), Vector256.Max(maxHealth, zero));

                var ticks = Saturate32(tickHits);
                var poisoned = Vector256.GreaterThan(poisonTicks, zero);
                var poisonEmit = poisoned & Term(Vector256.Create((long)Threshold.PoisonPerTickHit), ticks);
                Vector256.Max(poisonEmit, zero).Store(emitNextFrame + EmitClass.PoisonGain * Rows + row);
                Vector256.Min(poisonEmit, zero).Store(emitNextFrame + EmitClass.PoisonLoss * Rows + row);
                (regenCard & Term(regenRate, ticks)).Store(emitNextFrame + EmitClass.Healing * Rows + row);
                var nextPoisonTicks = Vector256.ConditionalSelect(poisoned, Vector256.Max(Saturate32(poisonTicks - ticks), zero), poisonTicks);

                var dead = Vector256.Equals(nextHealth, zero) & Vector256.Create((long)EventBit.Dead);
                var low = Vector256.LessThan(nextHealth * 100L, maxHealth * Threshold.LowHealthBelowPercent) & Vector256.Create((long)EventBit.LowHealth);
                var belowExecuteLine = Vector256.LessThan(nextHealth * 100L, maxHealth * Threshold.ExecuteBelowPercent) & Vector256.Create(1L << TargetContext.BelowExecuteLine);
                StoreNarrow(nextHealth, lanes + Lane.Health * Rows + row);
                StoreNarrow(shield - absorbed, lanes + Lane.Shield * Rows + row);
                StoreNarrow(nextPoisonTicks, lanes + Lane.PoisonTicks * Rows + row);
                StoreNarrow(dead | low, lanes + Lane.Events * Rows + row);
                StoreNarrow(Admits(immuneMask, kindMask) | belowExecuteLine, (int*)tables.Contexts + firstSlot + row);
            }

            var executeBits = 0ul;
            for (var row = 0; row < Rows; row += 8)
            {
                executeBits |= (ulong)Vector256.GreaterThan(Vector256.Load(lanes + Lane.Execute * Rows + row), Vector256<int>.Zero).ExtractMostSignificantBits() << row;
            }

            tables.ExecuteBits[firstSlot >> EffectKey.RowBits] = executeBits;
        }
#endif
    }
}
