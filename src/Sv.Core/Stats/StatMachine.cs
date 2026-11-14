using System;

namespace Sv
{
    public sealed unsafe partial class StatMachine : IDisposable
    {
        const int HeadPageShift = 12;
        const int HeadPageSlots = 1 << HeadPageShift;
        const int NoRecord = -1;
        const int NoOrdinal = -1;

        readonly World world;
        readonly int statCount;
        readonly int valueLanes;
        readonly int* ordinalOfLane;
        readonly int* laneOf;
        readonly int* baseOf;
        readonly int* scaleOf;
        readonly int* termStart;
        readonly DerivedTerm* terms;
        readonly int* curves;
        readonly int curveCount;
        readonly int* dependentStart;
        readonly int* dependents;
        readonly int headPageCount;
        readonly int** headPages;
        int* templateBase;
        int templateCapacity;
        LaneWriter writer;
        bool disposed;

        public StatMachine(World world, ReadOnlySpan<StatDef> stats, ReadOnlySpan<StatSource> sources, ReadOnlySpan<int> curveValues, ReadOnlySpan<CardTerm> cards, int undoEpochs)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (stats.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(stats), "sv: too many stats");
            }

            if (undoEpochs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(undoEpochs), "sv: undo epochs is 0 (off) or more");
            }

            var layout = world.Layout;
            valueLanes = layout.ValueLanes;
            statCount = stats.Length;
            ordinalOfLane = (int*)Memory.Alloc((nuint)Math.Max(1, valueLanes), sizeof(int));
            for (var lane = 0; lane < valueLanes; lane++)
            {
                ordinalOfLane[lane] = NoOrdinal;
            }

            var declared = (int*)Memory.Alloc((nuint)Math.Max(1, valueLanes), sizeof(int));
            for (var lane = 0; lane < valueLanes; lane++)
            {
                declared[lane] = NoOrdinal;
            }

            for (var at = 0; at < statCount; at++)
            {
                var stat = stats[at];
                if ((uint)stat.Lane >= (uint)valueLanes || layout.ClassOf(stat.Lane) != LaneClass.Stat || declared[stat.Lane] != NoOrdinal)
                {
                    Memory.Free(declared);
                    Memory.Free(ordinalOfLane);
                    throw new ArgumentException("sv: every stat names its own Stat lane once", nameof(stats));
                }

                if (!StatLaw.IsScale(stat.Scale))
                {
                    Memory.Free(declared);
                    Memory.Free(ordinalOfLane);
                    throw new ArgumentException("sv: a stat scale is a power of ten from 1 to 1,000,000", nameof(stats));
                }

                declared[stat.Lane] = at;
            }

            for (var at = 0; at < sources.Length; at++)
            {
                var source = sources[at];
                var valid = (uint)source.Stat < (uint)valueLanes && declared[source.Stat] != NoOrdinal
                    && (uint)source.From < (uint)valueLanes && declared[source.From] != NoOrdinal
                    && (source.CurveStart == StatSource.NoCurve
                        || (source.CurveStart >= 0 && source.CurveLength > 0 && source.CurveStep > 0 && (long)source.CurveStart + source.CurveLength <= curveValues.Length));
                if (!valid)
                {
                    Memory.Free(declared);
                    Memory.Free(ordinalOfLane);
                    throw new ArgumentException("sv: a derived source names two declared stats and a curve inside the curve table", nameof(sources));
                }
            }

            if (!ValidCards(cards, declared, layout, valueLanes))
            {
                Memory.Free(declared);
                Memory.Free(ordinalOfLane);
                throw new ArgumentException("sv: a card term names a declared stat, an Intrinsic lane and a shape kind", nameof(cards));
            }

            var rank = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            if (!Rank(stats, sources, declared, rank))
            {
                Memory.Free(rank);
                Memory.Free(declared);
                Memory.Free(ordinalOfLane);
                throw new ArgumentException("sv: derived stats form a cycle", nameof(sources));
            }

            laneOf = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            baseOf = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            scaleOf = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            for (var at = 0; at < statCount; at++)
            {
                var ordinal = rank[at];
                laneOf[ordinal] = stats[at].Lane;
                baseOf[ordinal] = stats[at].Base;
                scaleOf[ordinal] = stats[at].Scale;
                ordinalOfLane[stats[at].Lane] = ordinal;
            }

            curveCount = curveValues.Length;
            curves = (int*)Memory.Alloc((nuint)Math.Max(1, curveCount), sizeof(int));
            for (var at = 0; at < curveCount; at++)
            {
                curves[at] = curveValues[at];
            }

            termStart = (int*)Memory.AllocZeroed((nuint)(statCount + 1), sizeof(int));
            terms = (DerivedTerm*)Memory.Alloc((nuint)Math.Max(1, sources.Length), (nuint)sizeof(DerivedTerm));
            dependentStart = (int*)Memory.AllocZeroed((nuint)(statCount + 1), sizeof(int));
            dependents = (int*)Memory.Alloc((nuint)Math.Max(1, sources.Length), sizeof(int));
            for (var at = 0; at < sources.Length; at++)
            {
                termStart[ordinalOfLane[sources[at].Stat] + 1]++;
                dependentStart[ordinalOfLane[sources[at].From] + 1]++;
            }

            for (var ordinal = 0; ordinal < statCount; ordinal++)
            {
                termStart[ordinal + 1] += termStart[ordinal];
                dependentStart[ordinal + 1] += dependentStart[ordinal];
            }

            var termFill = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            var dependentFill = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            for (var ordinal = 0; ordinal < statCount; ordinal++)
            {
                termFill[ordinal] = termStart[ordinal];
                dependentFill[ordinal] = dependentStart[ordinal];
            }

            for (var at = 0; at < sources.Length; at++)
            {
                var source = sources[at];
                var target = ordinalOfLane[source.Stat];
                var from = ordinalOfLane[source.From];
                terms[termFill[target]++] = new DerivedTerm
                {
                    From = from,
                    Coefficient = source.Coefficient,
                    FromScale = scaleOf[from],
                    CurveStart = source.CurveStart,
                    CurveLength = source.CurveLength,
                    CurveMin = source.CurveMin,
                    CurveStep = source.CurveStep,
                };
                dependents[dependentFill[from]++] = target;
            }

            Memory.Free(termFill);
            Memory.Free(dependentFill);
            Memory.Free(rank);
            Memory.Free(declared);

            var entityCapacity = world.ChunkCapacity << Slot.RowBits;
            headPageCount = (entityCapacity + HeadPageSlots - 1) >> HeadPageShift;
            headPages = (int**)Memory.AllocZeroed((nuint)headPageCount, (nuint)sizeof(int*));
            templateCapacity = 0;
            templateBase = null;
            writer = world.CreateWriter(64);
            InitCards(cards);
            InitModifiers();
            InitRecalculate();
            InitUndo(undoEpochs);
        }

        bool Rank(ReadOnlySpan<StatDef> stats, ReadOnlySpan<StatSource> sources, int* declared, int* rank)
        {
            var count = stats.Length;
            var waiting = (int*)Memory.AllocZeroed((nuint)Math.Max(1, count), sizeof(int));
            var placed = (byte*)Memory.AllocZeroed((nuint)Math.Max(1, count));
            for (var at = 0; at < sources.Length; at++)
            {
                waiting[declared[sources[at].Stat]]++;
            }

            var next = 0;
            var progressed = true;
            while (next < count && progressed)
            {
                progressed = false;
                var chosen = -1;
                for (var lane = 0; lane < valueLanes && chosen < 0; lane++)
                {
                    var at = declared[lane];
                    if (at != NoOrdinal && placed[at] == 0 && waiting[at] == 0)
                    {
                        chosen = at;
                    }
                }

                if (chosen < 0)
                {
                    break;
                }

                placed[chosen] = 1;
                rank[chosen] = next++;
                progressed = true;
                for (var at = 0; at < sources.Length; at++)
                {
                    if (declared[sources[at].From] == chosen)
                    {
                        waiting[declared[sources[at].Stat]]--;
                    }
                }
            }

            Memory.Free(waiting);
            Memory.Free(placed);
            return next == count;
        }

        public World World => world;

        public int StatCount => statCount;

        public int OrdinalOf(int lane) => (uint)lane < (uint)valueLanes ? ordinalOfLane[lane] : NoOrdinal;

        public int LaneOf(int ordinal) => (uint)ordinal < (uint)statCount ? laneOf[ordinal] : -1;

        public int ScaleOf(int lane)
        {
            var ordinal = OrdinalOf(lane);
            return ordinal < 0 ? StatLaw.MinScale : scaleOf[ordinal];
        }

        public void SetTemplateBase(int template, int lane, int value)
        {
            var ordinal = OrdinalOf(lane);
            if (template < 0 || ordinal < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lane), "sv: a template base names a template and a declared stat");
            }

            EnsureTemplate(template);
            templateBase[(long)template * statCount + ordinal] = value;
        }

        public int TemplateBase(int template, int lane)
        {
            var ordinal = OrdinalOf(lane);
            if (ordinal < 0)
            {
                return 0;
            }

            return (uint)template < (uint)templateCapacity ? templateBase[(long)template * statCount + ordinal] : baseOf[ordinal];
        }

        public void FoldTemplate(int template, Span<int> valueByLane)
        {
            if (valueByLane.Length < valueLanes)
            {
                throw new ArgumentException("sv: one value per pack lane", nameof(valueByLane));
            }

            var resolved = (int*)Memory.Alloc((nuint)Math.Max(1, statCount), sizeof(int));
            for (var ordinal = 0; ordinal < statCount; ordinal++)
            {
                var count = GatherDerived(ordinal, resolved, 0);
                StatLaw.Canonicalize(scratchTerms, count);
                resolved[ordinal] = StatLaw.Fold(TemplateBaseOf(template, ordinal), scratchTerms, count);
                valueByLane[laneOf[ordinal]] = resolved[ordinal];
            }

            Memory.Free(resolved);
        }

        void EnsureTemplate(int template)
        {
            if (template < templateCapacity)
            {
                return;
            }

            var grown = Math.Max(4, templateCapacity * 2);
            while (grown <= template)
            {
                grown *= 2;
            }

            var bases = (int*)Memory.Alloc((nuint)((long)grown * Math.Max(1, statCount)), sizeof(int));
            if (templateBase != null)
            {
                Memory.Copy(templateBase, bases, (nuint)((long)templateCapacity * statCount * sizeof(int)));
                Memory.Free(templateBase);
            }

            for (var at = templateCapacity; at < grown; at++)
            {
                for (var ordinal = 0; ordinal < statCount; ordinal++)
                {
                    bases[(long)at * statCount + ordinal] = baseOf[ordinal];
                }
            }

            templateBase = bases;
            templateCapacity = grown;
        }

        int TemplateBaseOf(int template, int ordinal) =>
            (uint)template < (uint)templateCapacity ? templateBase[(long)template * statCount + ordinal] : baseOf[ordinal];

        int GatherDerived(int ordinal, int* resolved, int slot)
        {
            EnsureTerms(termStart[ordinal + 1] - termStart[ordinal]);
            var count = 0;
            var view = world.View;
            for (var at = termStart[ordinal]; at < termStart[ordinal + 1]; at++)
            {
                var term = terms[at];
                var source = resolved != null ? resolved[term.From] : Lanes.Read(view, slot, laneOf[term.From]);
                var added = term.CurveStart == StatSource.NoCurve
                    ? StatLaw.Derived(source, term.Coefficient, term.FromScale, scaleOf[ordinal])
                    : curves[term.CurveStart + StatLaw.CurveBucket(source, term.CurveMin, term.CurveStep, term.CurveLength)];
                scratchTerms[count++] = new ShapeTerm(ShapeKind.Added, added);
            }

            return count;
        }

        static T* Grow<T>(T* block, int used, ref int capacity, int needed) where T : unmanaged
        {
            if (needed <= capacity)
            {
                return block;
            }

            var grown = Math.Max(16, capacity * 2);
            while (grown < needed)
            {
                grown *= 2;
            }

            var fresh = (T*)Memory.Alloc((nuint)grown, (nuint)sizeof(T));
            if (block != null)
            {
                Memory.Copy(block, fresh, (nuint)used * (nuint)sizeof(T));
                Memory.Free(block);
            }

            capacity = grown;
            return fresh;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            world.DisposeWriter(ref writer);
            for (var page = 0; page < headPageCount; page++)
            {
                if (headPages[page] != null)
                {
                    Memory.Free(headPages[page]);
                }
            }

            Memory.Free(headPages);
            Memory.Free(ordinalOfLane);
            Memory.Free(laneOf);
            Memory.Free(baseOf);
            Memory.Free(scaleOf);
            Memory.Free(termStart);
            Memory.Free(terms);
            Memory.Free(curves);
            Memory.Free(dependentStart);
            Memory.Free(dependents);
            if (templateBase != null)
            {
                Memory.Free(templateBase);
            }

            FreeCards();
            FreeModifiers();
            FreeRecalculate();
            FreeUndo();
        }
    }
}
