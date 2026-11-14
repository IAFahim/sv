using System.Collections.Generic;

namespace Sv.Rules.Reference
{
    public sealed class InterpreterOptions
    {
        public int ArenaCapacity = int.MaxValue;
        public int EventCapacity = 16384;
        public int RetainFrames = 64;
        public int RouteHops = 4;
    }

    public sealed class StepResult
    {
        public RefState State { get; }

        public List<RefRecord> Canonical { get; }

        public List<RefRecord> Next { get; }

        public List<OutwardEvent> Events { get; }

        public StepFaults Faults { get; }

        public StepResult(RefState state, List<RefRecord> canonical, List<RefRecord> next, List<OutwardEvent> events, StepFaults faults)
        {
            State = state;
            Canonical = canonical;
            Next = next;
            Events = events;
            Faults = faults;
        }

        public FrameInputs NextInputs()
        {
            var inputs = new FrameInputs();
            inputs.Records.AddRange(Next);
            return inputs;
        }
    }

    public sealed class Outcome
    {
        public readonly RefRecord Record;
        public readonly List<int> ApplicableGates = new List<int>();
        public bool Admitted;
        public int TakenPerHit;
        public int TakenHits;
        public long AbsorbedAmount;
        public bool Survives;

        public Outcome(RefRecord record)
        {
            Record = record;
            TakenHits = record.Hits;
        }
    }

    public sealed class TargetFrame
    {
        public readonly int Slot;
        public readonly List<Outcome> Records = new List<Outcome>();
        public readonly List<Outcome> Survivors = new List<Outcome>();
        public readonly long[] Totals;
        public readonly long[] Delta;
        public readonly long[] AbsorbUsed;
        public readonly long[] Assigned;

        public TargetFrame(int slot, PackSchema schema)
        {
            Slot = slot;
            Totals = new long[schema.Channels.Length];
            Delta = new long[schema.Intrinsics.Length];
            AbsorbUsed = new long[schema.Intrinsics.Length];
            Assigned = new long[schema.Intrinsics.Length];
        }

        public bool AnyTotal
        {
            get
            {
                for (var i = 0; i < Totals.Length; i++)
                {
                    if (Totals[i] != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public sealed class EmitUnit
    {
        public Outcome? Record;
        public List<Outcome> Group = new List<Outcome>();
        public int Source = RefRecord.NoSource;
        public int Depth;
    }

    public sealed class Scope
    {
        public int Self;
        public Outcome? Record;
        public List<Outcome>? Group;
        public List<Outcome>? Frame;
        public int Source = RefRecord.NoSource;
        public long[]? Totals;
        public int VirtualChannel = -1;
        public int RuleId = -1;
    }

    public readonly struct Value
    {
        public readonly long Int;
        public readonly float Real;
        public readonly bool Truth;

        Value(long integer, float real, bool truth)
        {
            Int = integer;
            Real = real;
            Truth = truth;
        }

        public static Value Of(long integer) => new Value(integer, 0f, false);

        public static Value Of(float real) => new Value(0, real, false);

        public static Value Of(bool truth) => new Value(0, 0f, truth);
    }

    public sealed class Interpreter
    {
        public PackPlan Plan { get; }

        public InterpreterOptions Options { get; }

        readonly PackSchema schema;
        readonly ContentPack pack;
        readonly AssignMode[] assignModes;
        readonly int[] statOrder;
        RefState world;
        Trace? trace;
        StepFaults faults;
        long frameIndex;

        public Interpreter(PackPlan plan, InterpreterOptions? options = null)
        {
            Plan = plan;
            Options = options ?? new InterpreterOptions();
            schema = plan.Schema;
            pack = plan.Pack;
            statOrder = plan.StatOrder;
            world = new RefState(schema, 0);
            faults = new StepFaults();
            assignModes = new AssignMode[schema.Intrinsics.Length];
            for (var i = 0; i < assignModes.Length; i++)
            {
                assignModes[i] = schema.AssignModeOf(schema.Intrinsics[i].Id, pack.Flows);
            }
        }

        public RefState NewWorld(int capacity) => new RefState(schema, capacity);

        public StepResult Step(RefState state, FrameInputs inputs, Trace? frameTrace = null)
        {
            world = state.Clone();
            frameIndex = state.Frame;
            world.Frame = state.Frame + 1;
            trace = frameTrace;
            faults = new StepFaults();
            Note("frame " + Trace.Number(world.Frame));

            ReleaseDying();
            ApplyCommands(inputs.Commands);
            var canonical = Seal(inputs.Records);
            var statChanged = Stats(inputs.StatOps);

            var frames = new TargetFrame?[world.Capacity];
            var active = Partition(canonical, frames);
            for (var i = 0; i < active.Count; i++)
            {
                Gate(active[i]);
            }

            for (var i = 0; i < active.Count; i++)
            {
                Remove(active[i]);
            }

            var next = new List<RefRecord>();
            for (var i = 0; i < active.Count; i++)
            {
                EmitToOthers(active[i], next);
            }

            for (var i = 0; i < active.Count; i++)
            {
                Flow(active[i]);
            }

            var changed = Fold(frames, statChanged);
            var edges = Events(frames, statChanged, changed, next);
            next.Sort(RecordOrder.Canonical);
            var published = Publish(edges);
            var result = new StepResult(world, canonical, next, published, faults);
            trace = null;
            return result;
        }

        void Note(string line) => trace?.Add(line);

        int IntrinsicIndex(int id) => schema.IndexOf(ValueClass.Intrinsic, id);

        int ChannelIndex(int id) => schema.IndexOf(ValueClass.Channel, id);

        bool Held(int slot, int card) => card < 0 || world.Intrinsic(slot, card) > 0;

        int KindOf(int slot)
        {
            if (schema.KindIntrinsic < 0)
            {
                return 0;
            }

            var kind = world.Intrinsic(slot, schema.KindIntrinsic);
            return kind < 0 ? 0 : kind > 255 ? 255 : kind;
        }

        void ReleaseDying()
        {
            for (var slot = 0; slot < world.Capacity; slot++)
            {
                var entity = world.Slots[slot];
                if (entity.Status == SlotStatus.Dying && world.Frame - entity.DeathFrame >= Options.RetainFrames)
                {
                    RefState.ResetToDefaults(schema, entity);
                    entity.Status = SlotStatus.Free;
                    Note("release " + Trace.Slot(slot));
                }
            }
        }

        void ApplyCommands(List<StructuralCommand> commands)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (!world.IsSlot(command.Slot))
                {
                    faults.CommandFault++;
                    continue;
                }

                var entity = world.Slots[command.Slot];
                if (command.Kind == CommandKind.Spawn)
                {
                    var template = schema.Template(command.Template);
                    if (entity.Status != SlotStatus.Free || (command.Template >= 0 && template == null))
                    {
                        faults.CommandFault++;
                        continue;
                    }

                    RefState.ResetToDefaults(schema, entity);
                    if (template != null)
                    {
                        ApplyTemplate(entity, template);
                    }

                    FoldDerived(entity);
                    entity.Status = SlotStatus.Live;
                    entity.Template = command.Template;
                    entity.PendingCheck = true;
                    Note("spawn " + Trace.Slot(command.Slot) + " template " + (template == null ? "none" : template.Name));
                }
                else
                {
                    if (entity.Status != SlotStatus.Live)
                    {
                        faults.CommandFault++;
                        continue;
                    }

                    entity.Status = SlotStatus.Dying;
                    entity.DeathFrame = world.Frame;
                    Note("kill " + Trace.Slot(command.Slot));
                }
            }
        }

        void ApplyTemplate(RefEntity entity, TemplateDef template)
        {
            for (var s = 0; s < template.Settings.Length; s++)
            {
                var setting = template.Settings[s];
                var index = schema.IndexOf(setting.Class, setting.Id);
                if (index < 0)
                {
                    continue;
                }

                switch (setting.Class)
                {
                    case ValueClass.Stat:
                        entity.StatBase[index] = setting.Int;
                        entity.Stats[index] = setting.Int;
                        break;
                    case ValueClass.Intrinsic:
                        entity.Intrinsics[index] = setting.Int;
                        break;
                    case ValueClass.Event:
                        entity.Events[index] = setting.Level;
                        break;
                }
            }
        }

        void FoldDerived(RefEntity entity)
        {
            List<ShapeEntry>? core = null;
            for (var o = 0; o < statOrder.Length; o++)
            {
                var s = statOrder[o];
                var target = schema.Stats[s];
                if (target.Derive.Length == 0)
                {
                    continue;
                }

                core ??= new List<ShapeEntry>();
                core.Clear();
                GatherDerived(entity, target, core);
                entity.Stats[s] = ReferenceLaw.StatFold(entity.StatBase[s], core);
            }
        }

        void GatherDerived(RefEntity entity, ValueDef target, List<ShapeEntry> core)
        {
            for (var d = 0; d < target.Derive.Length; d++)
            {
                var source = target.Derive[d];
                var fromIndex = schema.IndexOf(ValueClass.Stat, source.From);
                if (fromIndex < 0)
                {
                    continue;
                }

                var fromValue = entity.Stats[fromIndex];
                var added = source.IsCurve
                    ? source.Curve[ReferenceLaw.CurveBucket(fromValue, source.CurveMin, source.CurveStep, source.Curve.Length)]
                    : ReferenceLaw.Derived(fromValue, source.Coefficient, schema.Stats[fromIndex].Scale, target.Scale);
                core.Add(new ShapeEntry(ShapeKind.Added, added));
            }
        }

        List<RefRecord> Seal(List<RefRecord> raw)
        {
            var accepted = new List<RefRecord>();
            for (var i = 0; i < raw.Count; i++)
            {
                var record = raw[i];
                if (record.Amount == 0)
                {
                    faults.DroppedZero++;
                    continue;
                }

                if (record.Hits <= 0 || record.Kind < 0 || record.Kind > 255 || ChannelIndex(record.Channel) < 0 || (record.Source != RefRecord.NoSource && !world.IsSlot(record.Source)))
                {
                    faults.DroppedMalformed++;
                    continue;
                }

                if (record.Depth < 0 || record.Depth > RuleLimits.WorldDepthLimit)
                {
                    faults.DepthFault++;
                    continue;
                }

                if (!Route(ref record))
                {
                    continue;
                }

                if (!world.IsLive(record.Target))
                {
                    faults.DroppedDead++;
                    Note("seal drop dead " + Trace.Record(schema, record));
                    continue;
                }

                accepted.Add(record);
            }

            var canonical = MergeEqual(accepted);
            canonical = Overflow(canonical);
            for (var i = 0; i < canonical.Count; i++)
            {
                Note("seal " + Trace.Record(schema, canonical[i]));
            }

            return canonical;
        }

        bool Route(ref RefRecord record)
        {
            var hops = 0;
            var visited = new List<int>();
            while (world.IsLive(record.Target))
            {
                var template = schema.Template(world.Slots[record.Target].Template);
                var via = -1;
                if (template != null)
                {
                    for (var f = 0; f < template.Forwards.Length; f++)
                    {
                        if (template.Forwards[f].Channel == record.Channel)
                        {
                            via = template.Forwards[f].Via;
                            break;
                        }
                    }
                }

                if (via < 0)
                {
                    return true;
                }

                visited.Add(record.Target);
                var to = world.Intrinsic(record.Target, via);
                hops++;
                if (hops > Options.RouteHops || visited.Contains(to))
                {
                    faults.RouteFault++;
                    Note("route fault " + Trace.Record(schema, record));
                    return false;
                }

                if (!world.IsSlot(to))
                {
                    faults.DroppedDead++;
                    return false;
                }

                Note("route " + Trace.Slot(record.Target) + " -> " + Trace.Slot(to) + " " + schema.NameOf(ValueClass.Channel, record.Channel));
                record = record.WithTarget(to);
            }

            return true;
        }

        static List<RefRecord> MergeEqual(List<RefRecord> records)
        {
            records.Sort(RecordOrder.Canonical);
            var merged = new List<RefRecord>();
            var at = 0;
            while (at < records.Count)
            {
                var first = records[at];
                var hits = 0L;
                while (at < records.Count && records[at].Key == first.Key && records[at].Amount == first.Amount)
                {
                    hits += records[at].Hits;
                    at++;
                }

                merged.Add(first.WithAmount(first.Amount, ReferenceLaw.Saturate(hits)));
            }

            return merged;
        }

        List<RefRecord> Overflow(List<RefRecord> canonical)
        {
            var level = 0;
            while (canonical.Count > Options.ArenaCapacity && level < 3)
            {
                level++;
                canonical = MergeLevel(canonical, level);
                Note("overflow level " + Trace.Number(level) + " records " + Trace.Number(canonical.Count));
            }

            if (canonical.Count > Options.ArenaCapacity)
            {
                level = 4;
                faults.DroppedOverflow += canonical.Count - Options.ArenaCapacity;
                canonical = canonical.GetRange(0, Options.ArenaCapacity);
                Note("overflow truncated records " + Trace.Number(canonical.Count));
            }

            faults.OverflowLevel = level;
            return canonical;
        }

        static RefRecord Coarse(RefRecord record, int level) =>
            level == 1 ? record
            : level == 2 ? new RefRecord(record.Target, RefRecord.NoSource, record.Kind, record.Channel, RecordOrigin.Direct, 0, 0, 1)
            : new RefRecord(record.Target, RefRecord.NoSource, 0, record.Channel, RecordOrigin.Direct, 0, 0, 1);

        static List<RefRecord> MergeLevel(List<RefRecord> records, int level)
        {
            var keyed = new List<RefRecord>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                keyed.Add(Coarse(records[i], level));
            }

            var order = new int[records.Count];
            var keys = new ulong[records.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
                keys[i] = level == 1 ? keyed[i].Key : keyed[i].Key & ~0xFul;
            }

            System.Array.Sort(keys, order);
            var merged = new List<RefRecord>();
            var at = 0;
            while (at < order.Length)
            {
                var key = keys[at];
                var first = records[order[at]];
                var basis = keyed[order[at]];
                var total = 0L;
                var depth = 0;
                var origin = first.Origin;
                var groupSize = 0;
                while (at < order.Length && keys[at] == key)
                {
                    var record = records[order[at]];
                    total = ReferenceLaw.Add(total, ReferenceLaw.Term(record.Amount, record.Hits));
                    depth = record.Depth > depth ? record.Depth : depth;
                    origin = record.Origin > origin ? record.Origin : origin;
                    groupSize++;
                    at++;
                }

                if (groupSize == 1)
                {
                    merged.Add(new RefRecord(basis.Target, basis.Source, basis.Kind, basis.Channel, first.Origin, first.Depth, first.Amount, first.Hits));
                    continue;
                }

                var amount = ReferenceLaw.Saturate(total);
                if (amount != 0)
                {
                    merged.Add(new RefRecord(basis.Target, basis.Source, basis.Kind, basis.Channel, origin, depth, amount, 1));
                }
            }

            merged.Sort(RecordOrder.Canonical);
            return merged;
        }

        List<bool[]> Stats(List<RefStatOp> ops)
        {
            var sorted = new List<RefStatOp>(ops);
            sorted.Sort(StatOpOrder.Canonical);
            for (var i = 0; i < sorted.Count; i++)
            {
                var op = sorted[i];
                if (!world.IsLive(op.Slot) || schema.IndexOf(ValueClass.Stat, op.Stat) < 0 || op.Layer > StatLayer.External)
                {
                    faults.StatOpFault++;
                    continue;
                }

                var modifiers = world.Slots[op.Slot].Modifiers;
                var at = 0;
                while (at < modifiers.Count && modifiers[at].Handle < op.Handle)
                {
                    at++;
                }

                var present = at < modifiers.Count && modifiers[at].Handle == op.Handle;
                if (op.Kind == StatOpKind.Add)
                {
                    while (at < modifiers.Count && modifiers[at].Handle == op.Handle)
                    {
                        at++;
                    }

                    modifiers.Insert(at, new HeldModifier(op.Handle, op.Stat, op.Shape, op.Value, op.Layer));
                }
                else if (op.Kind == StatOpKind.Remove && present)
                {
                    while (at < modifiers.Count && modifiers[at].Handle == op.Handle)
                    {
                        modifiers.RemoveAt(at);
                    }
                }
                else
                {
                    faults.StatOpFault++;
                }
            }

            var changed = new List<bool[]>(world.Capacity);
            var coreEntries = new List<ShapeEntry>();
            var externalEntries = new List<ShapeEntry>();
            for (var slot = 0; slot < world.Capacity; slot++)
            {
                var flags = new bool[schema.Stats.Length];
                changed.Add(flags);
                var entity = world.Slots[slot];
                if (entity.Status != SlotStatus.Live)
                {
                    continue;
                }

                for (var o = 0; o < statOrder.Length; o++)
                {
                    var s = statOrder[o];
                    var statDef = schema.Stats[s];
                    var stat = statDef.Id;
                    coreEntries.Clear();
                    externalEntries.Clear();
                    if (statDef.Derive.Length > 0)
                    {
                        GatherDerived(entity, statDef, coreEntries);
                    }

                    for (var c = 0; c < schema.Cards.Length; c++)
                    {
                        var card = schema.Cards[c];
                        var count = card.Intrinsic < 0 ? 0 : world.Intrinsic(slot, card.Intrinsic);
                        if (count <= 0)
                        {
                            continue;
                        }

                        for (var m = 0; m < card.Modifiers.Length; m++)
                        {
                            var modifier = card.Modifiers[m];
                            if (modifier.Stat == stat)
                            {
                                var targetList = modifier.Layer == StatLayer.Core ? coreEntries : externalEntries;
                                targetList.Add(new ShapeEntry(modifier.Kind, ReferenceLaw.CardTerm(modifier.Value, count, modifier.PerCount)));
                            }
                        }
                    }

                    for (var m = 0; m < entity.Modifiers.Count; m++)
                    {
                        if (entity.Modifiers[m].Stat == stat)
                        {
                            var targetList = entity.Modifiers[m].Layer == StatLayer.Core ? coreEntries : externalEntries;
                            targetList.Add(new ShapeEntry(entity.Modifiers[m].Shape, entity.Modifiers[m].Value));
                        }
                    }

                    var before = entity.Stats[s];
                    var after = ReferenceLaw.StatFold(entity.StatBase[s], coreEntries, externalEntries);
                    entity.Stats[s] = after;
                    if (before != after)
                    {
                        flags[s] = true;
                        Note("stat " + Trace.Slot(slot) + " " + statDef.Name + " " + Trace.Number(before) + " -> " + Trace.Number(after));
                    }
                }
            }

            return changed;
        }

        List<TargetFrame> Partition(List<RefRecord> canonical, TargetFrame?[] frames)
        {
            var active = new List<TargetFrame>();
            for (var i = 0; i < canonical.Count; i++)
            {
                var record = canonical[i];
                var frame = frames[record.Target];
                if (frame == null)
                {
                    frame = new TargetFrame(record.Target, schema);
                    frames[record.Target] = frame;
                    active.Add(frame);
                }

                frame.Records.Add(new Outcome(record));
            }

            return active;
        }

        Scope RecordScope(TargetFrame target, Outcome outcome) => new Scope
        {
            Self = target.Slot,
            Record = outcome,
            Source = outcome.Record.Source,
        };

        void Gate(TargetFrame target)
        {
            var gates = Plan.GateOrder;
            var entries = new List<ShapeEntry>();
            var groups = new List<int>();
            for (var r = 0; r < target.Records.Count; r++)
            {
                var outcome = target.Records[r];
                var scope = RecordScope(target, outcome);
                GateRule? decider = null;
                for (var g = 0; g < gates.Length; g++)
                {
                    var gate = gates[g];
                    scope.RuleId = gate.Id;
                    if (!Held(target.Slot, gate.Card) || !Truth(gate.When, scope))
                    {
                        continue;
                    }

                    outcome.ApplicableGates.Add(g);
                    if (gate.Admit != AdmitVerdict.None && decider == null)
                    {
                        decider = gate;
                    }
                }

                outcome.Admitted = decider == null || decider.Admit == AdmitVerdict.Allow;
                ShapeEntries(gates, outcome.ApplicableGates, entries, groups, scope);
                outcome.TakenPerHit = outcome.Admitted ? ReferenceLaw.ShapeAmount(outcome.Record.Amount, entries) : 0;
                Note("gate " + Trace.Record(schema, outcome.Record) + " -> " + (outcome.Admitted ? "allow" : "deny")
                    + (decider == null ? " by default" : " by " + Trace.Rule(decider)) + " taken " + Trace.Number(outcome.TakenPerHit) + " x" + Trace.Number(outcome.TakenHits));
            }

            Absorb(target, gates);
        }

        void ShapeEntries(GateRule[] gates, List<int> applicable, List<ShapeEntry> entries, List<int> groups, Scope scope)
        {
            entries.Clear();
            groups.Clear();
            for (var a = 0; a < applicable.Count; a++)
            {
                var gate = gates[applicable[a]];
                if (gate.Shapes.Length == 0)
                {
                    continue;
                }

                if (gate.Group >= 0)
                {
                    if (groups.Contains(gate.Group))
                    {
                        continue;
                    }

                    groups.Add(gate.Group);
                }

                for (var s = 0; s < gate.Shapes.Length; s++)
                {
                    var shape = gate.Shapes[s];
                    entries.Add(new ShapeEntry(shape.Kind, shape.Cell == null ? shape.Value : ScaledCell(scope, shape.Cell, shape.UnitScale)));
                }
            }
        }

        int RawCell(Scope scope, Expression cell)
        {
            var slot = CellSlot(scope, cell);
            return cell.Class == ValueClass.Stat ? world.Stat(slot, cell.Id) : world.Intrinsic(slot, cell.Id);
        }

        int ScaledCell(Scope scope, Expression cell, int unitScale) =>
            ReferenceLaw.Rescale(RawCell(scope, cell), world.ScaleOf(cell.Class, cell.Id), unitScale);

        int CellSlot(Scope scope, Expression e)
        {
            long slot;
            switch (e.Subject)
            {
                case Subject.Self: slot = scope.Self; break;
                case Subject.Source: slot = scope.Source == RefRecord.NoSource ? -1 : scope.Source; break;
                default: slot = world.Intrinsic(scope.Self, e.Via); break;
            }

            return world.IsSlot(slot) ? (int)slot : -1;
        }

        void Absorb(TargetFrame target, GateRule[] gates)
        {
            for (var g = 0; g < gates.Length; g++)
            {
                var gate = gates[g];
                for (var e = 0; e < gate.Absorbs.Length; e++)
                {
                    var effect = gate.Absorbs[e];
                    var budgetIndex = IntrinsicIndex(effect.Budget);
                    if (budgetIndex < 0)
                    {
                        continue;
                    }

                    var candidates = new List<Outcome>();
                    for (var r = 0; r < target.Records.Count; r++)
                    {
                        var outcome = target.Records[r];
                        if (outcome.Admitted && outcome.ApplicableGates.Contains(g))
                        {
                            candidates.Add(outcome);
                        }
                    }

                    if (effect.Order == AbsorbOrder.ByAmountDescending)
                    {
                        candidates = ByAmountDescending(candidates);
                    }

                    for (var c = 0; c < candidates.Count; c++)
                    {
                        var outcome = candidates[c];
                        var budget = (long)world.Intrinsic(target.Slot, effect.Budget);
                        var remaining = (budget < 0 ? 0 : budget) - target.AbsorbUsed[budgetIndex];
                        if (remaining <= 0)
                        {
                            break;
                        }

                        long take;
                        if (effect.Per == AbsorbPer.Hit)
                        {
                            take = remaining < outcome.TakenHits ? remaining : outcome.TakenHits;
                            outcome.TakenHits -= (int)take;
                        }
                        else
                        {
                            var available = ReferenceLaw.Term(outcome.TakenPerHit, outcome.TakenHits) - outcome.AbsorbedAmount;
                            if (available <= 0)
                            {
                                continue;
                            }

                            take = remaining < available ? remaining : available;
                            outcome.AbsorbedAmount += take;
                        }

                        target.AbsorbUsed[budgetIndex] += take;
                        Note("absorb " + Trace.Record(schema, outcome.Record) + " by " + Trace.Rule(gate) + " " + Trace.Number(take) + (effect.Per == AbsorbPer.Hit ? " hits" : " amount"));
                    }
                }

                for (var e = 0; e < gate.Spends.Length; e++)
                {
                    Spend(target, gate, g, gate.Spends[e]);
                }
            }
        }

        void Spend(TargetFrame target, GateRule gate, int order, int budget)
        {
            var budgetIndex = IntrinsicIndex(budget);
            if (budgetIndex < 0)
            {
                return;
            }

            var start = (long)world.Intrinsic(target.Slot, budget);
            for (var r = 0; r < target.Records.Count; r++)
            {
                var outcome = target.Records[r];
                if (!outcome.Admitted || !outcome.ApplicableGates.Contains(order))
                {
                    continue;
                }

                var remaining = (start < 0 ? 0 : start) - target.AbsorbUsed[budgetIndex];
                var take = remaining <= 0 ? 0 : remaining < outcome.TakenHits ? remaining : outcome.TakenHits;
                outcome.TakenHits = (int)take;
                target.AbsorbUsed[budgetIndex] += take;
                Note("spend " + Trace.Record(schema, outcome.Record) + " by " + Trace.Rule(gate) + " admits " + Trace.Number(take) + " hits");
            }
        }

        static List<Outcome> ByAmountDescending(List<Outcome> candidates)
        {
            var keyed = new Outcome[candidates.Count];
            var totals = new long[candidates.Count];
            var positions = new int[candidates.Count];
            for (var i = 0; i < keyed.Length; i++)
            {
                keyed[i] = candidates[i];
                totals[i] = ReferenceLaw.Term(candidates[i].TakenPerHit, candidates[i].TakenHits);
                positions[i] = i;
            }

            for (var i = 1; i < keyed.Length; i++)
            {
                var at = i;
                while (at > 0 && (totals[at - 1] < totals[at] || (totals[at - 1] == totals[at] && positions[at - 1] > positions[at])))
                {
                    Swap(keyed, at);
                    Swap(totals, at);
                    Swap(positions, at);
                    at--;
                }
            }

            return new List<Outcome>(keyed);
        }

        static void Swap<T>(T[] items, int at)
        {
            var held = items[at - 1];
            items[at - 1] = items[at];
            items[at] = held;
        }

        public static long Consumed(Outcome outcome, Measure measure)
        {
            var offered = ReferenceLaw.Term(outcome.Record.Amount, outcome.Record.Hits);
            var taken = ReferenceLaw.Term(outcome.TakenPerHit, outcome.TakenHits) - outcome.AbsorbedAmount;
            switch (measure)
            {
                case Measure.Offered: return offered;
                case Measure.Taken: return taken;
                default: return ReferenceLaw.Add(offered, ReferenceLaw.Negate(taken));
            }
        }

        public static void ConsumedPerHit(Outcome outcome, Measure measure, out int amount, out int hits)
        {
            var whole = outcome.AbsorbedAmount == 0;
            switch (measure)
            {
                case Measure.Offered:
                    amount = outcome.Record.Amount;
                    hits = outcome.Record.Hits;
                    return;
                case Measure.Taken:
                    amount = whole ? outcome.TakenPerHit : ReferenceLaw.Saturate(Consumed(outcome, Measure.Taken));
                    hits = whole ? outcome.TakenHits : 1;
                    return;
                default:
                    if (whole && outcome.TakenHits == outcome.Record.Hits)
                    {
                        amount = ReferenceLaw.Saturate((long)outcome.Record.Amount - outcome.TakenPerHit);
                        hits = outcome.Record.Hits;
                        return;
                    }

                    amount = ReferenceLaw.Saturate(Consumed(outcome, Measure.NotTaken));
                    hits = 1;
                    return;
            }
        }

        bool WantsUntaken(TargetFrame target, Outcome outcome)
        {
            var emits = pack.Emits;
            for (var e = 0; e < emits.Length; e++)
            {
                if (Plan.EmitWantsUntaken[e] && Held(target.Slot, emits[e].Card) && outcome.Record.Depth <= emits[e].DepthLimit)
                {
                    return true;
                }
            }

            return false;
        }

        void Remove(TargetFrame target)
        {
            for (var r = 0; r < target.Records.Count; r++)
            {
                var outcome = target.Records[r];
                outcome.Survives = Consumed(outcome, Measure.Taken) != 0 || WantsUntaken(target, outcome);
                if (outcome.Survives)
                {
                    target.Survivors.Add(outcome);
                }
                else
                {
                    Note("remove " + Trace.Record(schema, outcome.Record));
                }
            }
        }

        public List<EmitUnit> Units(EmitPer per, int depthLimit, TargetFrame target)
        {
            var units = new List<EmitUnit>();
            var eligible = new List<Outcome>();
            for (var r = 0; r < target.Survivors.Count; r++)
            {
                if (target.Survivors[r].Record.Depth <= depthLimit)
                {
                    eligible.Add(target.Survivors[r]);
                }
            }

            if (eligible.Count == 0)
            {
                return units;
            }

            if (per == EmitPer.Frame)
            {
                var whole = new EmitUnit { Group = eligible };
                for (var r = 0; r < eligible.Count; r++)
                {
                    whole.Depth = eligible[r].Record.Depth > whole.Depth ? eligible[r].Record.Depth : whole.Depth;
                }

                units.Add(whole);
                return units;
            }

            var at = 0;
            while (at < eligible.Count)
            {
                var source = eligible[at].Record.Source;
                var group = new List<Outcome>();
                var depth = 0;
                while (at < eligible.Count && eligible[at].Record.Source == source)
                {
                    group.Add(eligible[at]);
                    depth = eligible[at].Record.Depth > depth ? eligible[at].Record.Depth : depth;
                    at++;
                }

                if (per == EmitPer.Source)
                {
                    units.Add(new EmitUnit { Group = group, Source = source, Depth = depth });
                    continue;
                }

                for (var g = 0; g < group.Count; g++)
                {
                    units.Add(new EmitUnit { Record = group[g], Group = group, Source = source, Depth = group[g].Record.Depth });
                }
            }

            return units;
        }

        Scope UnitScope(TargetFrame target, EmitUnit unit, bool withTotals, int ruleId) => new Scope
        {
            Self = target.Slot,
            Record = unit.Record,
            Group = unit.Group,
            Frame = target.Survivors,
            Source = unit.Source,
            Totals = withTotals ? target.Totals : null,
            RuleId = ruleId,
        };

        bool UnitAmount(int self, EmitUnit unit, EmitAction action, EmitPer per, out int channel, out int amount, out int hits)
        {
            channel = action.SameChannel ? (unit.Record == null ? -1 : unit.Record.Record.Channel) : action.Channel;
            amount = 0;
            hits = 1;
            if (channel < 0 || (unit.Record != null && !action.CoversChannel(unit.Record.Record.Channel)))
            {
                return false;
            }

            var measure = action.Amount == AmountKind.Offered ? Measure.Offered : action.Amount == AmountKind.Taken ? Measure.Taken : Measure.NotTaken;
            if (action.Amount == AmountKind.Constant || action.Amount == AmountKind.Cell)
            {
                amount = action.Cell == null ? action.Constant : CellAmount(new Scope { Self = self, Source = unit.Source }, action.Cell);
                hits = per == EmitPer.Hit && unit.Record != null ? unit.Record.Record.Hits : 1;
            }
            else if (unit.Record != null)
            {
                ConsumedPerHit(unit.Record, measure, out amount, out hits);
            }
            else
            {
                var total = 0L;
                for (var g = 0; g < unit.Group.Count; g++)
                {
                    if (action.CoversChannel(unit.Group[g].Record.Channel))
                    {
                        total = ReferenceLaw.Add(total, Consumed(unit.Group[g], measure));
                    }
                }

                amount = ReferenceLaw.Saturate(total);
            }

            if (action.ScaleStat >= 0)
            {
                amount = ReferenceLaw.ScaleBy(amount, world.Stat(self, action.ScaleStat), world.ScaleOf(ValueClass.Stat, action.ScaleStat));
            }
            else if (action.ScaleIntrinsic >= 0)
            {
                amount = ReferenceLaw.ScaleBy(amount, world.Intrinsic(self, action.ScaleIntrinsic), world.ScaleOf(ValueClass.Intrinsic, action.ScaleIntrinsic));
            }

            return amount != 0 && hits != 0;
        }

        int CellAmount(Scope scope, Expression cell)
        {
            var raw = RawCell(scope, cell);
            return cell.Class == ValueClass.Stat ? ReferenceLaw.Rescale(raw, world.ScaleOf(ValueClass.Stat, cell.Id), 1) : raw;
        }

        public static void DeliverNextFrame(List<RefRecord> next, RefRecord record) => next.Add(record);

        void EmitToOthers(TargetFrame target, List<RefRecord> next)
        {
            var emits = pack.Emits;
            for (var e = 0; e < emits.Length; e++)
            {
                var rule = emits[e];
                if (!Held(target.Slot, rule.Card))
                {
                    continue;
                }

                var units = Units(rule.Per, rule.DepthLimit, target);
                for (var u = 0; u < units.Count; u++)
                {
                    var unit = units[u];
                    var hasOther = false;
                    for (var a = 0; a < rule.Actions.Length; a++)
                    {
                        hasOther |= !rule.IsSameFrameSelf(rule.Actions[a]);
                    }

                    if (!hasOther || !Truth(rule.When, UnitScope(target, unit, false, rule.Id)))
                    {
                        continue;
                    }

                    for (var a = 0; a < rule.Actions.Length; a++)
                    {
                        var action = rule.Actions[a];
                        if (rule.IsSameFrameSelf(action) || !UnitAmount(target.Slot, unit, action, rule.Per, out var channel, out var amount, out var hits))
                        {
                            continue;
                        }

                        var to = Destination(target.Slot, unit.Source, action);
                        if (!world.IsSlot(to))
                        {
                            faults.EmitNowhere++;
                            continue;
                        }

                        var record = new RefRecord((int)to, target.Slot, KindOf(target.Slot), channel, action.Origin, unit.Depth + 1, amount, hits);
                        DeliverNextFrame(next, record);
                        Note("emit " + Trace.Rule(rule) + " " + Trace.Record(schema, record));
                    }
                }
            }
        }

        long Destination(int self, int source, EmitAction action)
        {
            switch (action.To)
            {
                case EmitTo.Self: return self;
                case EmitTo.Source: return source == RefRecord.NoSource ? -1 : source;
                default: return world.Intrinsic(self, action.Via);
            }
        }

        public static void FrameTotals(TargetFrame target, PackSchema schema)
        {
            for (var c = 0; c < target.Totals.Length; c++)
            {
                target.Totals[c] = 0;
            }

            for (var r = 0; r < target.Survivors.Count; r++)
            {
                var outcome = target.Survivors[r];
                var index = schema.IndexOf(ValueClass.Channel, outcome.Record.Channel);
                target.Totals[index] = ReferenceLaw.Add(target.Totals[index], Consumed(outcome, Measure.Taken));
            }
        }

        int FrameStartValue(int slot, int intrinsicIndex) => world.Slots[slot].Intrinsics[intrinsicIndex];

        void Assign(TargetFrame target, int intrinsicIndex, long value, string why)
        {
            var mode = assignModes[intrinsicIndex];
            var before = target.Assigned[intrinsicIndex];
            target.Assigned[intrinsicIndex] = AssignModes.Apply(mode, before, value);
            Note("assign " + Trace.Slot(target.Slot) + " " + schema.Intrinsics[intrinsicIndex].Name + " " + AssignModes.NameOf(mode) + " " + Trace.Number(value)
                + " from " + why + " -> " + Trace.Number(target.Assigned[intrinsicIndex]));
        }

        void AssignFromRecords(TargetFrame target)
        {
            for (var i = 0; i < assignModes.Length; i++)
            {
                target.Assigned[i] = assignModes[i] == AssignMode.Add ? 0L : FrameStartValue(target.Slot, i);
            }

            for (var r = 0; r < target.Survivors.Count; r++)
            {
                var outcome = target.Survivors[r];
                var channel = schema.Channels[ChannelIndex(outcome.Record.Channel)];
                if (!channel.Assigns || Consumed(outcome, Measure.Taken) == 0)
                {
                    continue;
                }

                var into = IntrinsicIndex(channel.Into);
                if (into < 0)
                {
                    continue;
                }

                long value;
                if (channel.IntoMode == AssignMode.Source)
                {
                    value = outcome.Record.HasSource ? outcome.Record.Source : -1;
                }
                else
                {
                    ConsumedPerHit(outcome, Measure.Taken, out var amount, out _);
                    value = amount;
                }

                Assign(target, into, value, "record " + Trace.Record(schema, outcome.Record));
            }
        }

        void FinishAssign(TargetFrame target)
        {
            for (var i = 0; i < assignModes.Length; i++)
            {
                if (assignModes[i] == AssignMode.Add)
                {
                    continue;
                }

                var moved = ReferenceLaw.Add(target.Assigned[i], ReferenceLaw.Negate(FrameStartValue(target.Slot, i)));
                target.Delta[i] = ReferenceLaw.Add(target.Delta[i], moved);
            }
        }

        void Flow(TargetFrame target)
        {
            FrameTotals(target, schema);
            AssignFromRecords(target);
            var selfOrder = Plan.SelfOrder;
            for (var i = 0; i < selfOrder.Length; i++)
            {
                var rule = selfOrder[i];
                if (!Held(target.Slot, rule.Card))
                {
                    continue;
                }

                var units = Units(rule.Per, rule.DepthLimit, target);
                for (var u = 0; u < units.Count; u++)
                {
                    var unit = units[u];
                    if (!Truth(rule.When, UnitScope(target, unit, true, rule.Id)))
                    {
                        continue;
                    }

                    for (var a = 0; a < rule.Actions.Length; a++)
                    {
                        var action = rule.Actions[a];
                        if (!rule.IsSameFrameSelf(action) || !UnitAmount(target.Slot, unit, action, rule.Per, out var channel, out var amount, out var hits))
                        {
                            continue;
                        }

                        SelfEmit(target, rule, channel, amount, hits);
                    }
                }
            }

            for (var c = 0; c < schema.Channels.Length; c++)
            {
                if (target.Totals[c] != 0)
                {
                    Note("total " + Trace.Slot(target.Slot) + " " + schema.Channels[c].Name + " " + Trace.Number(target.Totals[c]));
                }
            }

            var scope = new Scope { Self = target.Slot, Totals = target.Totals };
            var flows = pack.Flows;
            for (var f = 0; f < flows.Length; f++)
            {
                var flow = flows[f];
                if (!Held(target.Slot, flow.Card) || !Truth(flow.When, scope))
                {
                    continue;
                }

                for (var t = 0; t < flow.Terms.Length; t++)
                {
                    var term = flow.Terms[t];
                    var into = IntrinsicIndex(term.Intrinsic);
                    var from = ChannelIndex(term.Channel);
                    if (into < 0 || from < 0 || target.Totals[from] == 0)
                    {
                        continue;
                    }

                    if (term.Assigns)
                    {
                        Assign(target, into, target.Totals[from], Trace.Rule(flow) + " total " + schema.Channels[from].Name);
                        continue;
                    }

                    var moved = term.Sign > 0 ? target.Totals[from] : ReferenceLaw.Negate(target.Totals[from]);
                    target.Delta[into] = ReferenceLaw.Add(target.Delta[into], moved);
                    Note("flow " + Trace.Slot(target.Slot) + " " + Trace.Rule(flow) + " " + schema.Intrinsics[into].Name + " " + (moved >= 0 ? "+" : "") + Trace.Number(moved) + " from " + schema.Channels[from].Name);
                }
            }

            for (var c = 0; c < schema.Channels.Length; c++)
            {
                var into = schema.Channels[c].Into >= 0 && !schema.Channels[c].Assigns ? IntrinsicIndex(schema.Channels[c].Into) : -1;
                if (into >= 0 && target.Totals[c] != 0)
                {
                    target.Delta[into] = ReferenceLaw.Add(target.Delta[into], target.Totals[c]);
                    Note("flow " + Trace.Slot(target.Slot) + " into " + schema.Intrinsics[into].Name + " +" + Trace.Number(target.Totals[c]) + " from " + schema.Channels[c].Name);
                }
            }

            for (var i = 0; i < target.AbsorbUsed.Length; i++)
            {
                if (target.AbsorbUsed[i] != 0)
                {
                    target.Delta[i] = ReferenceLaw.Add(target.Delta[i], -target.AbsorbUsed[i]);
                    Note("flow " + Trace.Slot(target.Slot) + " absorb spends " + schema.Intrinsics[i].Name + " -" + Trace.Number(target.AbsorbUsed[i]));
                }
            }

            FinishAssign(target);
        }

        void SelfEmit(TargetFrame target, EmitRule rule, int channel, int amount, int hits)
        {
            var gates = Plan.GateOrder;
            var scope = new Scope { Self = target.Slot, VirtualChannel = channel };
            var applicable = new List<int>();
            GateRule? decider = null;
            for (var g = 0; g < gates.Length; g++)
            {
                var gate = gates[g];
                if (!Plan.GateIsChannelState[g] || !Held(target.Slot, gate.Card) || !Truth(gate.When, scope))
                {
                    continue;
                }

                applicable.Add(g);
                if (gate.Admit != AdmitVerdict.None && decider == null)
                {
                    decider = gate;
                }
            }

            var admitted = decider == null || decider.Admit == AdmitVerdict.Allow;
            var entries = new List<ShapeEntry>();
            ShapeEntries(gates, applicable, entries, new List<int>(), scope);
            var shaped = admitted ? ReferenceLaw.ShapeAmount(amount, entries) : 0;
            var index = ChannelIndex(channel);
            if (index >= 0)
            {
                target.Totals[index] = ReferenceLaw.Add(target.Totals[index], ReferenceLaw.Term(shaped, hits));
                var row = schema.Channels[index];
                var into = row.Assigns ? IntrinsicIndex(row.Into) : -1;
                if (into >= 0 && ReferenceLaw.Term(shaped, hits) != 0)
                {
                    Assign(target, into, row.IntoMode == AssignMode.Source ? target.Slot : shaped, "self " + Trace.Rule(rule));
                }
            }

            Note("self " + Trace.Slot(target.Slot) + " " + Trace.Rule(rule) + " " + schema.NameOf(ValueClass.Channel, channel) + " " + Trace.Number(amount) + " x" + Trace.Number(hits)
                + " -> " + (admitted ? "allow" : "deny") + (decider == null ? " by default" : " by " + Trace.Rule(decider)) + " taken " + Trace.Number(shaped));
        }

        int BoundValue(Bound bound, int slot, int intrinsicScale, int fallback)
        {
            switch (bound.Kind)
            {
                case BoundKind.Constant: return bound.Constant;
                case BoundKind.Stat: return ReferenceLaw.Rescale(world.Stat(slot, bound.Stat), world.ScaleOf(ValueClass.Stat, bound.Stat), intrinsicScale);
                default: return fallback;
            }
        }

        static bool BoundMoved(Bound bound, bool[] statChanged, PackSchema schema)
        {
            if (bound.Kind != BoundKind.Stat)
            {
                return false;
            }

            var index = schema.IndexOf(ValueClass.Stat, bound.Stat);
            return index >= 0 && statChanged[index];
        }

        List<bool> Fold(TargetFrame?[] frames, List<bool[]> statChanged)
        {
            var changed = new List<bool>(world.Capacity);
            for (var slot = 0; slot < world.Capacity; slot++)
            {
                changed.Add(false);
                var entity = world.Slots[slot];
                if (entity.Status != SlotStatus.Live)
                {
                    continue;
                }

                var frame = frames[slot];
                for (var i = 0; i < schema.Intrinsics.Length; i++)
                {
                    var id = schema.Intrinsics[i].Id;
                    var delta = frame == null ? 0L : frame.Delta[i];
                    var fold = Plan.FoldOf(id);
                    var rebound = fold != null && (BoundMoved(fold.Floor, statChanged[slot], schema) || BoundMoved(fold.Ceiling, statChanged[slot], schema));
                    if (delta == 0 && !rebound)
                    {
                        continue;
                    }

                    var floor = fold == null ? int.MinValue : BoundValue(fold.Floor, slot, schema.Intrinsics[i].Scale, int.MinValue);
                    var ceiling = fold == null ? int.MaxValue : BoundValue(fold.Ceiling, slot, schema.Intrinsics[i].Scale, int.MaxValue);
                    var before = entity.Intrinsics[i];
                    var after = ReferenceLaw.Step(before, ReferenceLaw.Saturate(delta), floor, ceiling);
                    entity.Intrinsics[i] = after;
                    changed[slot] |= after != before;
                    Note("fold " + Trace.Slot(slot) + " " + schema.Intrinsics[i].Name + " " + Trace.Number(before) + " -> " + Trace.Number(after)
                        + " delta " + Trace.Number(delta) + " floor " + Trace.Number(floor) + " ceiling " + Trace.Number(ceiling));
                }
            }

            return changed;
        }

        static bool Any(bool[] flags)
        {
            for (var i = 0; i < flags.Length; i++)
            {
                if (flags[i])
                {
                    return true;
                }
            }

            return false;
        }

        List<OutwardEvent> Events(TargetFrame?[] frames, List<bool[]> statChanged, List<bool> intrinsicChanged, List<RefRecord> next)
        {
            var edges = new List<OutwardEvent>();
            var zeros = new long[schema.Channels.Length];
            for (var slot = 0; slot < world.Capacity; slot++)
            {
                var entity = world.Slots[slot];
                if (entity.Status != SlotStatus.Live)
                {
                    continue;
                }

                var frame = frames[slot];
                var anyTotal = frame != null && frame.AnyTotal;
                var dirty = entity.PendingCheck || intrinsicChanged[slot] || Any(statChanged[slot]) || anyTotal || entity.HadTotals;
                entity.HadTotals = anyTotal;
                entity.PendingCheck = false;
                if (!dirty)
                {
                    continue;
                }

                var scope = new Scope { Self = slot, Totals = frame == null ? zeros : frame.Totals };
                for (var r = 0; r < pack.Events.Length; r++)
                {
                    var rule = pack.Events[r];
                    var index = schema.IndexOf(ValueClass.Event, rule.Event);
                    if (index < 0)
                    {
                        continue;
                    }

                    var state = entity.Events[index];
                    var enter = Truth(rule.Enter, scope);
                    var stay = Truth(rule.Stay, scope);
                    var level = ReferenceLaw.NextLevel(state, enter, stay);
                    if (level == state)
                    {
                        continue;
                    }

                    entity.Events[index] = level;
                    edges.Add(new OutwardEvent(rule.Event, slot, level, 0));
                    Note("event " + Trace.Slot(slot) + " " + schema.Events[index].Name + (level ? " enter" : " leave"));
                    EdgeEmits(slot, rule.Event, level, scope, next);
                }
            }

            return edges;
        }

        void EdgeEmits(int slot, int raised, bool rising, Scope scope, List<RefRecord> next)
        {
            for (var e = 0; e < pack.Edges.Length; e++)
            {
                var rule = pack.Edges[e];
                if (rule.Event != raised || rule.Rising != rising || !Held(slot, rule.Card) || !Truth(rule.When, scope))
                {
                    continue;
                }

                for (var a = 0; a < rule.Actions.Length; a++)
                {
                    var action = rule.Actions[a];
                    var amount = action.Constant;
                    if (action.ScaleStat >= 0)
                    {
                        amount = ReferenceLaw.ScaleBy(amount, world.Stat(slot, action.ScaleStat), world.ScaleOf(ValueClass.Stat, action.ScaleStat));
                    }

                    var to = Destination(slot, RefRecord.NoSource, action);
                    if (amount == 0 || action.SameChannel)
                    {
                        continue;
                    }

                    if (!world.IsSlot(to))
                    {
                        faults.EmitNowhere++;
                        continue;
                    }

                    var record = new RefRecord((int)to, slot, KindOf(slot), action.Channel, action.Origin, 0, amount, 1);
                    DeliverNextFrame(next, record);
                    Note("edge " + Trace.Rule(rule) + " " + Trace.Record(schema, record));
                }
            }
        }

        List<OutwardEvent> Publish(List<OutwardEvent> edges)
        {
            var keys = new long[edges.Count];
            var ordered = edges.ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                keys[i] = ((long)ordered[i].Event << 32) | (uint)ordered[i].Slot;
            }

            System.Array.Sort(keys, ordered);
            var published = new List<OutwardEvent>();
            var at = 0;
            while (at < ordered.Length)
            {
                var raised = ordered[at].Event;
                var count = 0;
                while (at < ordered.Length && ordered[at].Event == raised)
                {
                    if (count < Options.EventCapacity)
                    {
                        published.Add(ordered[at]);
                    }

                    count++;
                    at++;
                }

                if (count > Options.EventCapacity)
                {
                    published.Add(new OutwardEvent(raised, -1, false, count - Options.EventCapacity));
                }
            }

            return published;
        }

        bool Truth(Expression e, Scope scope) => Evaluate(e, scope, 0).Truth;

        public Value Evaluate(Expression e, Scope scope, int depth)
        {
            if (depth > 512)
            {
                return Value.Of(false);
            }

            switch (e.Op)
            {
                case Op.BoolConst: return Value.Of(e.Integer != 0);
                case Op.IntConst:
                case Op.KindConst:
                case Op.ChannelConst: return Value.Of(e.Integer);
                case Op.FloatConst: return Value.Of(e.Real);
                case Op.RecordField: return Value.Of(Field(scope, e.Field));
                case Op.Roll:
                {
                    var outcome = scope.Record;
                    return Value.Of(outcome != null
                        && ReferenceLaw.Rolls(schema.Seed, frameIndex, outcome.Record.Key, outcome.Record.Amount, scope.RuleId, 0, e.Integer));
                }
                case Op.Cell: return Cell(scope, e);
                case Op.ChannelTotal:
                {
                    var index = ChannelIndex(e.Id);
                    return Value.Of(scope.Totals == null || index < 0 ? 0L : scope.Totals[index]);
                }

                case Op.Aggregate: return Value.Of(Aggregate(scope, e));
                case Op.Chain:
                {
                    var chain = pack.Chain(e.Id);
                    return Value.Of(chain != null && Evaluate(chain.When, scope, depth + 1).Truth);
                }

                case Op.AnyChain:
                    for (var i = 0; i < pack.Chains.Length; i++)
                    {
                        if (System.Array.IndexOf(pack.Chains[i].Tags, e.Id) >= 0 && Evaluate(pack.Chains[i].When, scope, depth + 1).Truth)
                        {
                            return Value.Of(true);
                        }
                    }

                    return Value.Of(false);
                case Op.And:
                    for (var i = 0; i < e.Args.Length; i++)
                    {
                        if (!Evaluate(e.Args[i], scope, depth + 1).Truth)
                        {
                            return Value.Of(false);
                        }
                    }

                    return Value.Of(true);
                case Op.Or:
                    for (var i = 0; i < e.Args.Length; i++)
                    {
                        if (Evaluate(e.Args[i], scope, depth + 1).Truth)
                        {
                            return Value.Of(true);
                        }
                    }

                    return Value.Of(false);
                case Op.Not: return Value.Of(!Evaluate(e.Args[0], scope, depth + 1).Truth);
                case Op.Eq:
                case Op.Ne:
                case Op.Lt:
                case Op.Le:
                case Op.Gt:
                case Op.Ge:
                    return Value.Of(Compare(e.Op, e.Args[0].Type, Evaluate(e.Args[0], scope, depth + 1), Evaluate(e.Args[1], scope, depth + 1)));
                case Op.InSet: return Value.Of(e.Contains(Evaluate(e.Args[0], scope, depth + 1).Int));
                case Op.Bit:
                {
                    var value = Evaluate(e.Args[0], scope, depth + 1).Int;
                    var index = Evaluate(e.Args[1], scope, depth + 1).Int;
                    return Value.Of(index >= 0 && index < 64 && ((value >> (int)index) & 1L) != 0);
                }

                case Op.Below:
                    return Value.Of(ReferenceLaw.Below(
                        Evaluate(e.Args[0], scope, depth + 1).Int,
                        Evaluate(e.Args[1], scope, depth + 1).Int,
                        Evaluate(e.Args[2], scope, depth + 1).Int,
                        Evaluate(e.Args[3], scope, depth + 1).Int));
                case Op.Add:
                case Op.Sub:
                case Op.Mul:
                case Op.Min:
                case Op.Max:
                    return Arithmetic(e.Op, e.Type, Evaluate(e.Args[0], scope, depth + 1), Evaluate(e.Args[1], scope, depth + 1));
                case Op.Neg:
                {
                    var value = Evaluate(e.Args[0], scope, depth + 1);
                    return e.Type == ExprType.Float ? Value.Of(-value.Real) : Value.Of(ReferenceLaw.Negate(value.Int));
                }

                case Op.ToFloat: return Value.Of((float)Evaluate(e.Args[0], scope, depth + 1).Int);
                case Op.BoundOf: return Value.Of((long)ReferenceLaw.BoundOf(Evaluate(e.Args[0], scope, depth + 1).Real));
                case Op.Select:
                    return Evaluate(e.Args[0], scope, depth + 1).Truth ? Evaluate(e.Args[1], scope, depth + 1) : Evaluate(e.Args[2], scope, depth + 1);
                default:
                    return Value.Of(false);
            }
        }

        static bool Compare(Op op, ExprType type, Value left, Value right)
        {
            switch (type)
            {
                case ExprType.Bool:
                    return op == Op.Eq ? left.Truth == right.Truth : op == Op.Ne && left.Truth != right.Truth;
                case ExprType.Float:
                {
                    var a = left.Real;
                    var b = right.Real;
                    switch (op)
                    {
                        case Op.Eq: return a == b;
                        case Op.Ne: return a < b || a > b;
                        case Op.Lt: return a < b;
                        case Op.Le: return a <= b;
                        case Op.Gt: return a > b;
                        default: return a >= b;
                    }
                }

                default:
                {
                    var a = left.Int;
                    var b = right.Int;
                    switch (op)
                    {
                        case Op.Eq: return a == b;
                        case Op.Ne: return a != b;
                        case Op.Lt: return a < b;
                        case Op.Le: return a <= b;
                        case Op.Gt: return a > b;
                        default: return a >= b;
                    }
                }
            }
        }

        static Value Arithmetic(Op op, ExprType type, Value left, Value right)
        {
            if (type == ExprType.Float)
            {
                var a = left.Real;
                var b = right.Real;
                switch (op)
                {
                    case Op.Add: return Value.Of(ReferenceLaw.FloatAdd(a, b));
                    case Op.Sub: return Value.Of((float)(a - b));
                    case Op.Mul: return Value.Of(ReferenceLaw.FloatMultiply(a, b));
                    case Op.Min: return Value.Of(a < b ? a : b);
                    default: return Value.Of(a > b ? a : b);
                }
            }

            var x = left.Int;
            var y = right.Int;
            switch (op)
            {
                case Op.Add: return Value.Of(ReferenceLaw.Add(x, y));
                case Op.Sub: return Value.Of(ReferenceLaw.Add(x, ReferenceLaw.Negate(y)));
                case Op.Mul: return Value.Of(ReferenceLaw.Multiply(x, y));
                case Op.Min: return Value.Of(x < y ? x : y);
                default: return Value.Of(x > y ? x : y);
            }
        }

        static long Field(Scope scope, RecordField field)
        {
            var outcome = scope.Record;
            if (outcome == null)
            {
                return field == RecordField.Channel && scope.VirtualChannel >= 0 ? scope.VirtualChannel : 0;
            }

            var record = outcome.Record;
            switch (field)
            {
                case RecordField.Kind: return record.Kind;
                case RecordField.Channel: return record.Channel;
                case RecordField.Source: return record.Source;
                case RecordField.Origin: return (long)record.Origin;
                case RecordField.Depth: return record.Depth;
                case RecordField.Amount: return record.Amount;
                case RecordField.Hits: return record.Hits;
                case RecordField.Taken: return Consumed(outcome, Measure.Taken);
                default: return Consumed(outcome, Measure.NotTaken);
            }
        }

        Value Cell(Scope scope, Expression e)
        {
            long slot;
            switch (e.Subject)
            {
                case Subject.Self: slot = scope.Self; break;
                case Subject.Source: slot = scope.Source == RefRecord.NoSource ? -1 : scope.Source; break;
                default: slot = world.Intrinsic(scope.Self, e.Via); break;
            }

            var target = world.IsSlot(slot) ? (int)slot : -1;
            switch (e.Class)
            {
                case ValueClass.Stat: return Value.Of((long)ReferenceLaw.Rescale(world.Stat(target, e.Id), world.ScaleOf(ValueClass.Stat, e.Id), 1));
                case ValueClass.Event: return Value.Of(world.Event(target, e.Id));
                default: return Value.Of((long)world.Intrinsic(target, e.Id));
            }
        }

        static long Aggregate(Scope scope, Expression e)
        {
            var group = e.Group == AggregateGroup.Frame ? scope.Frame : scope.Group;
            if (group == null)
            {
                return 0;
            }

            var total = 0L;
            for (var i = 0; i < group.Count; i++)
            {
                var outcome = group[i];
                if (e.Set.Length == 0 || e.Contains(outcome.Record.Channel))
                {
                    total = ReferenceLaw.Add(total, Consumed(outcome, e.Measure));
                }
            }

            return total;
        }
    }
}
