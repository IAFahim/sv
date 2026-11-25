using System;
using System.Collections.Generic;

namespace Sv.V4
{
    public static class RecordReference
    {
        public static Effect[] StepOneFrame(LaneWorld world, Effect[] sentLastFrame, Effect[] emittedLastFrame)
        {
            var records = new Effect[sentLastFrame.Length + emittedLastFrame.Length];
            sentLastFrame.CopyTo(records, 0);
            emittedLastFrame.CopyTo(records, sentLastFrame.Length);
            Array.Sort(records, (a, b) => EffectLaw.Compare(a, b));
            var emitted = new List<Effect>();
            var at = 0;
            for (var target = 0; target < world.SlotLimit; target++)
            {
                var first = at;
                while (at < records.Length && EffectKey.Target(records[at].Key) == target)
                {
                    at++;
                }

                ResolveTarget(world, target, records, first, at, emitted);
            }

            return emitted.ToArray();
        }

        static void ResolveTarget(LaneWorld world, int target, Effect[] records, int first, int end, List<Effect> emitted)
        {
            var health = world.Read(target, Lane.Health);
            var maxHealth = world.Read(target, Lane.MaxHealth);
            var shield = world.Read(target, Lane.Shield);
            var immuneMask = world.Read(target, Lane.ImmuneMask);
            var kindMask = world.Read(target, Lane.KindMask);
            var fireHealsCard = world.Read(target, Lane.Converter);
            var poisonTicks = world.Read(target, Lane.PoisonTicks);
            var regenCard = world.Read(target, Lane.RegenCard);
            var regenRate = world.Read(target, Lane.RegenRate);
            var belowExecuteLine = (long)health * 100 < (long)maxHealth * Threshold.ExecuteBelowPercent;

            var healingTaken = 0L;
            var tickHits = 0L;
            var damageOffered = 0L;
            var shieldCandidates = new List<long>();
            for (var r = first; r < end; r++)
            {
                var key = records[r].Key;
                var channel = (int)EffectKey.Channel(key);
                var kind = (int)EffectKey.Kind(key);
                if (IsBitSet(immuneMask, channel) || IsBitSet(kindMask, kind))
                {
                    continue;
                }

                var term = channel == Channel.Tick ? records[r].Hits : EffectLaw.Term(records[r].Amount, records[r].Hits);
                if (channel == Channel.Physical && SourceExecutes(world, EffectKey.Source(key)) && belowExecuteLine)
                {
                    term *= 2;
                }

                var landsAs = channel == Channel.Fire && fireHealsCard > 0 ? Channel.Healing : channel;
                if (landsAs == Channel.Healing)
                {
                    healingTaken += term;
                }
                else if (landsAs == Channel.Tick)
                {
                    tickHits += term;
                }
                else
                {
                    damageOffered += term;
                    shieldCandidates.Add(term);
                }
            }

            var absorbed = AbsorbLargestFirst(shieldCandidates, shield);
            var damageTaken = damageOffered - absorbed;
            var ceiling = Math.Max(maxHealth, 0);
            var nextHealth = (int)Math.Clamp((long)health + healingTaken - damageTaken, 0L, ceiling);
            var ticks = EffectLaw.Saturate32(tickHits);
            var nextPoisonTicks = poisonTicks;
            if (poisonTicks > 0)
            {
                emitted.Add(new Effect(EffectKey.Pack(target, EffectKey.NoSource, 0, Channel.Poison, Origin.Triggered, 1), Threshold.PoisonPerTickHit, ticks));
                nextPoisonTicks = (int)Math.Max(0L, EffectLaw.Saturate32((long)poisonTicks - ticks));
            }

            if (regenCard > 0)
            {
                emitted.Add(new Effect(EffectKey.Pack(target, EffectKey.NoSource, 0, Channel.Healing, Origin.Triggered, 1), regenRate, ticks));
            }

            var events = (nextHealth == 0 ? EventBit.Dead : 0) | ((long)nextHealth * 100 < (long)maxHealth * Threshold.LowHealthBelowPercent ? EventBit.LowHealth : 0);
            world.Write(target, Lane.Health, nextHealth);
            world.Write(target, Lane.Shield, (int)(shield - absorbed));
            world.Write(target, Lane.PoisonTicks, nextPoisonTicks);
            world.Write(target, Lane.Events, events);
        }

        static long AbsorbLargestFirst(List<long> candidates, int shield)
        {
            var order = candidates.ToArray();
            var index = new int[order.Length];
            for (var i = 0; i < index.Length; i++)
            {
                index[i] = i;
            }

            Array.Sort(index, (a, b) => order[a] != order[b] ? order[b].CompareTo(order[a]) : a.CompareTo(b));
            var remaining = (long)Math.Max(shield, 0);
            var absorbed = 0L;
            foreach (var i in index)
            {
                if (remaining <= 0L || order[i] <= 0L)
                {
                    break;
                }

                var take = Math.Min(remaining, order[i]);
                absorbed += take;
                remaining -= take;
            }

            return absorbed;
        }

        static bool IsBitSet(int bits, int index) => index < 32 && ((bits >> index) & 1) != 0;

        static bool SourceExecutes(LaneWorld world, int source) => source < world.SlotLimit && world.Read(source, Lane.Execute) != 0;
    }
}
