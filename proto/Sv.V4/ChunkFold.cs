using System;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.V4
{
    public static unsafe class ChunkFold
    {
        const int Rows = LaneWorld.Rows;

        public static void GatherClassSums(long* totals, ClassSumBlocks** writers, int writerCount, int chunk, long* emittedLastFrame)
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

            for (var emitClass = 0; emitClass < EmitClass.Count; emitClass++)
            {
                AddInto(totals + EmitClass.SumClassOf(emitClass) * Rows, emittedLastFrame + emitClass * Rows, Rows);
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

        public static void ResolveChunkScalar(int* lanes, long* totals, long* emitNextFrame)
        {
            for (var row = 0; row < Rows; row++)
            {
                ResolveRowLaw(lanes, totals, emitNextFrame, row);
            }
        }

        static long AllowedMask(int bits, int index) => ((bits >> index) & 1) == 0 ? -1L : 0L;

        static long Min(long a, long b) => a < b ? a : b;

        static long Max(long a, long b) => a > b ? a : b;

        public static void ResolveRowLaw(int* lanes, long* totals, long* emitNextFrame, int row)
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
            var belowExecuteLine = health * 100 < maxHealth * Threshold.ExecuteBelowPercent ? -1L : 0L;

            long physicalGain = 0, physicalLoss = 0, poisonGain = 0, poisonLoss = 0, fireGain = 0, fireLoss = 0, healing = 0, tickHits = 0;
            for (var kind = 0; kind < SumClass.Kinds; kind++)
            {
                var kindAllowed = AllowedMask(kindMask, kind);
                var executedGain = totals[SumClass.PhysicalOf(kind, 1, 0) * Rows + row];
                var executedLoss = totals[SumClass.PhysicalOf(kind, 1, 1) * Rows + row];
                physicalGain += kindAllowed & (totals[SumClass.PhysicalOf(kind, 0, 0) * Rows + row] + executedGain + (executedGain & belowExecuteLine));
                physicalLoss += kindAllowed & (totals[SumClass.PhysicalOf(kind, 0, 1) * Rows + row] + executedLoss + (executedLoss & belowExecuteLine));
                poisonGain += kindAllowed & totals[SumClass.PoisonOf(kind, 0) * Rows + row];
                poisonLoss += kindAllowed & totals[SumClass.PoisonOf(kind, 1) * Rows + row];
                fireGain += kindAllowed & totals[SumClass.FireOf(kind, 0) * Rows + row];
                fireLoss += kindAllowed & totals[SumClass.FireOf(kind, 1) * Rows + row];
                healing += kindAllowed & (totals[SumClass.HealingOf(kind) * Rows + row] + totals[SumClass.HealingLossOf(kind) * Rows + row]);
                tickHits += kindAllowed & (totals[SumClass.TickOf(kind) * Rows + row] + totals[SumClass.TickLossOf(kind) * Rows + row]);
            }

            physicalGain &= AllowedMask(immuneMask, Channel.Physical);
            physicalLoss &= AllowedMask(immuneMask, Channel.Physical);
            poisonGain &= AllowedMask(immuneMask, Channel.Poison);
            poisonLoss &= AllowedMask(immuneMask, Channel.Poison);
            fireGain &= AllowedMask(immuneMask, Channel.Fire);
            fireLoss &= AllowedMask(immuneMask, Channel.Fire);
            healing &= AllowedMask(immuneMask, Channel.Healing);
            tickHits &= AllowedMask(immuneMask, Channel.Tick);

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

        static void StoreNarrow(Vector256<long> value, int* lanes, int lane, int row) =>
            Vector128.Narrow(value.GetLower(), value.GetUpper()).Store(lanes + lane * Rows + row);

        static Vector256<long> AllowedMask(Vector256<long> bits, int index) =>
            Vector256.Equals(Vector256.ShiftRightLogical(bits, index) & Vector256<long>.One, Vector256<long>.Zero);

        static Vector256<long> Sum(long* totals, int sumClass, int row) => Vector256.Load(totals + sumClass * Rows + row);

        static Vector256<long> Saturate32(Vector256<long> value) =>
            Vector256.Min(Vector256.Max(value, Vector256.Create((long)int.MinValue)), Vector256.Create((long)int.MaxValue));

        static Vector256<long> Term(Vector256<long> amount, Vector256<long> hits) =>
            Vector256.Min(Vector256.Max(amount * hits, Vector256.Create(-EffectLaw.TermLimit)), Vector256.Create(EffectLaw.TermLimit));

        public static void ResolveChunkVector(int* lanes, long* totals, long* emitNextFrame)
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
                var belowExecuteLine = Vector256.LessThan(health * 100L, maxHealth * Threshold.ExecuteBelowPercent);

                Vector256<long> physicalGain = zero, physicalLoss = zero, poisonGain = zero, poisonLoss = zero, fireGain = zero, fireLoss = zero, healing = zero, tickHits = zero;
                for (var kind = 0; kind < SumClass.Kinds; kind++)
                {
                    var kindAllowed = AllowedMask(kindMask, kind);
                    var executedGain = Sum(totals, SumClass.PhysicalOf(kind, 1, 0), row);
                    var executedLoss = Sum(totals, SumClass.PhysicalOf(kind, 1, 1), row);
                    physicalGain += kindAllowed & (Sum(totals, SumClass.PhysicalOf(kind, 0, 0), row) + executedGain + (executedGain & belowExecuteLine));
                    physicalLoss += kindAllowed & (Sum(totals, SumClass.PhysicalOf(kind, 0, 1), row) + executedLoss + (executedLoss & belowExecuteLine));
                    poisonGain += kindAllowed & Sum(totals, SumClass.PoisonOf(kind, 0), row);
                    poisonLoss += kindAllowed & Sum(totals, SumClass.PoisonOf(kind, 1), row);
                    fireGain += kindAllowed & Sum(totals, SumClass.FireOf(kind, 0), row);
                    fireLoss += kindAllowed & Sum(totals, SumClass.FireOf(kind, 1), row);
                    healing += kindAllowed & (Sum(totals, SumClass.HealingOf(kind), row) + Sum(totals, SumClass.HealingLossOf(kind), row));
                    tickHits += kindAllowed & (Sum(totals, SumClass.TickOf(kind), row) + Sum(totals, SumClass.TickLossOf(kind), row));
                }

                physicalGain &= AllowedMask(immuneMask, Channel.Physical);
                physicalLoss &= AllowedMask(immuneMask, Channel.Physical);
                poisonGain &= AllowedMask(immuneMask, Channel.Poison);
                poisonLoss &= AllowedMask(immuneMask, Channel.Poison);
                fireGain &= AllowedMask(immuneMask, Channel.Fire);
                fireLoss &= AllowedMask(immuneMask, Channel.Fire);
                healing &= AllowedMask(immuneMask, Channel.Healing);
                tickHits &= AllowedMask(immuneMask, Channel.Tick);

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
                StoreNarrow(nextHealth, lanes, Lane.Health, row);
                StoreNarrow(shield - absorbed, lanes, Lane.Shield, row);
                StoreNarrow(nextPoisonTicks, lanes, Lane.PoisonTicks, row);
                StoreNarrow(dead | low, lanes, Lane.Events, row);
            }
        }
#endif
    }
}
