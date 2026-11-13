using System;

namespace Sv
{
    public sealed unsafe partial class StatMachine
    {
        const int NoCard = -1;

        int* cardStart;
        CardRow* cardRows;
        int cardLaneCount;
        int* cardLanes;
        int* cardOfLane;
        int* cardDependentStart;
        int* cardDependents;

        public int CardLaneCount => cardLaneCount;

        public int CardLane(int card) => (uint)card < (uint)cardLaneCount ? cardLanes[card] : -1;

        public int CardOf(int lane) => (uint)lane < (uint)valueLanes ? cardOfLane[lane] : NoCard;

        static bool ValidCards(ReadOnlySpan<CardTerm> cards, int* declared, WorldLayout layout, int valueLanes)
        {
            for (var at = 0; at < cards.Length; at++)
            {
                var card = cards[at];
                var valid = (uint)card.Stat < (uint)valueLanes && declared[card.Stat] != NoOrdinal
                    && (uint)card.Card < (uint)valueLanes && layout.ClassOf(card.Card) == LaneClass.Intrinsic
                    && card.Kind <= ShapeKind.Cap;
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        void InitCards(ReadOnlySpan<CardTerm> cards)
        {
            cardOfLane = (int*)Memory.Alloc((nuint)Math.Max(1, valueLanes), sizeof(int));
            for (var lane = 0; lane < valueLanes; lane++)
            {
                cardOfLane[lane] = NoCard;
            }

            for (var at = 0; at < cards.Length; at++)
            {
                cardOfLane[cards[at].Card] = 0;
            }

            cardLanes = (int*)Memory.Alloc((nuint)Math.Max(1, cards.Length), sizeof(int));
            cardLaneCount = 0;
            for (var lane = 0; lane < valueLanes; lane++)
            {
                if (cardOfLane[lane] != NoCard)
                {
                    cardOfLane[lane] = cardLaneCount;
                    cardLanes[cardLaneCount++] = lane;
                }
            }

            cardStart = (int*)Memory.AllocZeroed((nuint)(statCount + 1), sizeof(int));
            cardRows = (CardRow*)Memory.Alloc((nuint)Math.Max(1, cards.Length), (nuint)sizeof(CardRow));
            for (var at = 0; at < cards.Length; at++)
            {
                cardStart[ordinalOfLane[cards[at].Stat] + 1]++;
            }

            for (var ordinal = 0; ordinal < statCount; ordinal++)
            {
                cardStart[ordinal + 1] += cardStart[ordinal];
            }

            var fill = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            for (var ordinal = 0; ordinal < statCount; ordinal++)
            {
                fill[ordinal] = cardStart[ordinal];
            }

            for (var at = 0; at < cards.Length; at++)
            {
                var card = cards[at];
                cardRows[fill[ordinalOfLane[card.Stat]]++] = new CardRow { Lane = card.Card, Value = card.Value, Kind = card.Kind, PerCount = card.PerCount };
            }

            Memory.Free(fill);
            cardDependentStart = (int*)Memory.AllocZeroed((nuint)(cardLaneCount + 1), sizeof(int));
            cardDependents = (int*)Memory.Alloc((nuint)Math.Max(1, cards.Length), sizeof(int));
            var seen = (byte*)Memory.Alloc((nuint)Math.Max(1, statCount));
            var placed = 0;
            for (var card = 0; card < cardLaneCount; card++)
            {
                Memory.Clear(seen, (nuint)Math.Max(1, statCount));
                for (var at = 0; at < cards.Length; at++)
                {
                    if (cards[at].Card == cardLanes[card])
                    {
                        seen[ordinalOfLane[cards[at].Stat]] = 1;
                    }
                }

                for (var ordinal = 0; ordinal < statCount; ordinal++)
                {
                    if (seen[ordinal] != 0)
                    {
                        cardDependents[placed++] = ordinal;
                    }
                }

                cardDependentStart[card + 1] = placed;
            }

            Memory.Free(seen);
        }

        void FreeCards()
        {
            Memory.Free(cardOfLane);
            Memory.Free(cardLanes);
            Memory.Free(cardStart);
            Memory.Free(cardRows);
            Memory.Free(cardDependentStart);
            Memory.Free(cardDependents);
        }

        int GatherCards(int ordinal, int slot, int count)
        {
            var from = cardStart[ordinal];
            var to = cardStart[ordinal + 1];
            if (from == to)
            {
                return count;
            }

            EnsureTerms(count + to - from);
            var view = world.View;
            for (var at = from; at < to; at++)
            {
                var row = cardRows[at];
                var held = Lanes.Read(view, slot, row.Lane);
                if (held > 0)
                {
                    scratchTerms[count++] = new ShapeTerm(row.Kind, CardTerm.TermValue(row.Value, held, row.PerCount));
                }
            }

            return count;
        }

        public void CardRowsChanged(int chunk, int card, ulong rows)
        {
            if ((uint)card >= (uint)cardLaneCount)
            {
                return;
            }

            while (rows != 0ul)
            {
                var slot = Slot.Of(chunk, Bits.Tzcnt(rows));
                rows &= rows - 1ul;
                MarkCard(slot, card);
            }
        }

        public void Spawned(int slot)
        {
            if (cardLaneCount == 0 || world.StateOf(slot) != SlotState.Live)
            {
                return;
            }

            var view = world.View;
            for (var card = 0; card < cardLaneCount; card++)
            {
                if (Lanes.Read(view, slot, cardLanes[card]) > 0)
                {
                    MarkCard(slot, card);
                }
            }
        }

        void MarkCard(int slot, int card)
        {
            for (var at = cardDependentStart[card]; at < cardDependentStart[card + 1]; at++)
            {
                MarkDirty(slot, cardDependents[at]);
            }
        }
    }
}
