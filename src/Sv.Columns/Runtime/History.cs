using System;

namespace Sv.Columns
{
    public enum ColumnActionKind
    {
        Spawn = 0,
        Despawn = 1,
        Set = 2,
        Send = 3,
        SendFrom = 4,
    }

    public readonly struct ColumnAction
    {
        ColumnAction(ColumnActionKind kind, int slot, int template, int lane, int value, int source, int channel, long amount, int hits, bool applied)
        {
            Kind = kind;
            Slot = slot;
            Template = template;
            Lane = lane;
            Value = value;
            Source = source;
            Channel = channel;
            Amount = amount;
            Hits = hits;
            Applied = applied;
        }

        public ColumnActionKind Kind { get; }

        public int Slot { get; }

        public int Template { get; }

        public int Lane { get; }

        public int Value { get; }

        public int Source { get; }

        public int Channel { get; }

        public long Amount { get; }

        public int Hits { get; }

        public bool Applied { get; }

        public static ColumnAction Spawn(int template) => new ColumnAction(ColumnActionKind.Spawn, -1, template, 0, 0, -1, 0, 0, 0, false);

        public static ColumnAction Despawn(int slot) => new ColumnAction(ColumnActionKind.Despawn, slot, -1, 0, 0, -1, 0, 0, 0, false);

        public static ColumnAction Set(int slot, int lane, int value) => new ColumnAction(ColumnActionKind.Set, slot, -1, lane, value, -1, 0, 0, 0, false);

        public static ColumnAction Send(int target, int channel, long amount, int hits = 1) => new ColumnAction(ColumnActionKind.Send, target, -1, 0, 0, -1, channel, amount, hits, false);

        public static ColumnAction SendFrom(int source, int target, int channel, long amount, int hits = 1) => new ColumnAction(ColumnActionKind.SendFrom, target, -1, 0, 0, source, channel, amount, hits, false);

        internal static ColumnAction Spawned(int template, int slot) => new ColumnAction(ColumnActionKind.Spawn, slot, template, 0, 0, -1, 0, 0, 0, true);

        internal ColumnAction Done(int slot, bool applied) => new ColumnAction(Kind, slot, Template, Lane, Value, Source, Channel, Amount, Hits, applied);
    }

    public readonly struct FrameInputs
    {
        readonly ColumnWorld world;

        internal FrameInputs(ColumnWorld world, int frame)
        {
            this.world = world;
            Frame = frame;
        }

        public int Frame { get; }

        public int ActionCount => world.ReadLog(Frame).ActionCount;

        public ColumnAction Action(int index) => world.ReadAction(Frame, index);

        public void AddAction(ColumnAction action) => world.InsertAction(Frame, ActionCount, action);

        public void InsertAction(int index, ColumnAction action) => world.InsertAction(Frame, index, action);

        public void ReplaceAction(int index, ColumnAction action) => world.ReplaceAction(Frame, index, action);

        public void RemoveAction(int index) => world.RemoveAction(Frame, index);

        public int RecordCount => world.ReadLog(Frame).RecordCount;

        public ulong Record(int index) => world.ReadRecord(Frame, index);

        public void AddRecord(ulong record) => world.InsertRecord(Frame, record);

        public void ReplaceRecord(int index, ulong record) => world.ReplaceRecord(Frame, index, record);

        public void RemoveRecord(int index) => world.RemoveRecord(Frame, index);
    }

    internal unsafe struct FrameLog
    {
        public ulong* Records;
        public int RecordCount;
        public int RecordCapacity;
        public long Refused;
        public ColumnAction* Actions;
        public int ActionCount;
        public int ActionCapacity;
    }

    internal unsafe struct Checkpoint
    {
        public int* Lanes;
        public SlotGate* Gates;
        public ulong* Alive;
        public int* Templates;
        public int* Open;
        public int OpenCapacity;
        public long* Dropped;
        public int NextChunk;
        public int Count;
    }

    public sealed unsafe partial class ColumnWorld
    {
        public const int HistoryLimit = 1 << 12;
        int historyFrames;
        int ring;
        FrameLog* logs;
        Checkpoint* checkpoints;
        int* writerOffsets;
        int editedFrom = int.MaxValue;
        int resimulateTo;
        bool resimulating;
        bool applying;
        ulong* editedChunks;
        ulong* replayChunks;
        ulong* replayMask;
        bool structureEdited;

        public int History => historyFrames;

        public int OldestFrame => Math.Max(0, frame - historyFrames);

        public bool Resimulating => resimulating;

        void OpenHistory(int history)
        {
            if (history < 0 || history > HistoryLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(history), "history is 0 to " + HistoryLimit + " frames");
            }

            if (history == 0)
            {
                return;
            }

            historyFrames = history;
            ring = history + 1;
            logs = Native.Zeroed<FrameLog>(ring);
            checkpoints = Native.Zeroed<Checkpoint>(ring);
            writerOffsets = Native.Zeroed<int>(writers.Length);
            editedChunks = Native.Zeroed<ulong>(MaskWords);
            replayChunks = Native.Zeroed<ulong>(MaskWords);
            for (var s = 0; s < ring; s++)
            {
                var ck = checkpoints + s;
                ck->Lanes = Native.Zeroed<int>((long)chunkCount * chunkInts);
                ck->Gates = Native.Zeroed<SlotGate>(Capacity);
                ck->Alive = Native.Zeroed<ulong>(chunkCount);
                ck->Templates = Native.Zeroed<int>(chunkCount);
                ck->OpenCapacity = tables->TemplateCount + chunkCount;
                ck->Open = Native.Zeroed<int>(ck->OpenCapacity);
                ck->Dropped = Native.Zeroed<long>(writers.Length);
            }

            for (var c = 0; c < chunkCount; c++)
            {
                checkpoints->Templates[c] = -1;
            }
        }

        int MaskWords => (chunkCount + 63) >> 6;

        FrameLog* LogOf(int at) => logs + at % ring;

        bool Replays(int chunk) => replayMask == null || ((replayMask[chunk >> 6] >> (chunk & 63)) & 1ul) != 0;

        static void MarkSlot(ulong* mask, int slot, int capacity)
        {
            if ((uint)slot < (uint)capacity)
            {
                var chunk = slot >> 6;
                mask[chunk >> 6] |= 1ul << (chunk & 63);
            }
        }

        void MarkEdited(ColumnAction action)
        {
            if (action.Kind == ColumnActionKind.Spawn || action.Kind == ColumnActionKind.Despawn)
            {
                structureEdited = true;
            }

            MarkSlot(editedChunks, action.Slot, Capacity);
            if (action.Kind == ColumnActionKind.SendFrom)
            {
                MarkSlot(editedChunks, action.Source, Capacity);
            }
        }

        void MarkEdited(ulong record) => MarkSlot(editedChunks, (int)(record >> Record.TargetShift), Capacity);

        void ForgetEdits()
        {
            editedFrom = int.MaxValue;
            structureEdited = false;
            Native.Clear(editedChunks, (long)MaskWords * 8);
        }

        ulong* ReplayMaskFrom(int from)
        {
            if (structureEdited)
            {
                return null;
            }

            var words = MaskWords;
            Buffer.MemoryCopy(editedChunks, replayChunks, (long)words * 8, (long)words * 8);
            for (var at = from; at <= frame; at++)
            {
                var log = LogOf(at);
                for (var i = 0; i < log->ActionCount; i++)
                {
                    var action = log->Actions[i];
                    MarkSlot(replayChunks, action.Slot, Capacity);
                    if (action.Kind == ColumnActionKind.SendFrom)
                    {
                        MarkSlot(replayChunks, action.Source, Capacity);
                    }
                }
            }

            return replayChunks;
        }

        Checkpoint* CheckpointOf(int at) => checkpoints + at % ring;

        void BeforeEdit()
        {
            if (resimulating && !applying)
            {
                throw new InvalidOperationException("the world is resimulating: step it until Resimulating is false before spawn, despawn or set");
            }
        }

        void Note(ColumnAction action)
        {
            if (logs == null || applying)
            {
                return;
            }

            var log = LogOf(frame);
            if (log->ActionCount == log->ActionCapacity)
            {
                GrowActions(log);
            }

            log->Actions[log->ActionCount++] = action;
        }

        static void GrowActions(FrameLog* log)
        {
            var capacity = Math.Max(16, log->ActionCapacity * 2);
            var grown = Native.Zeroed<ColumnAction>(capacity);
            Buffer.MemoryCopy(log->Actions, grown, (long)capacity * sizeof(ColumnAction), (long)log->ActionCount * sizeof(ColumnAction));
            Native.Free(log->Actions);
            log->Actions = grown;
            log->ActionCapacity = capacity;
        }

        static void EnsureRecords(FrameLog* log, int count)
        {
            if (count <= log->RecordCapacity)
            {
                return;
            }

            var capacity = Math.Max(1024, log->RecordCapacity);
            while (capacity < count)
            {
                capacity = capacity > int.MaxValue / 2 ? int.MaxValue : capacity * 2;
            }

            var grown = Native.Zeroed<ulong>(capacity);
            Buffer.MemoryCopy(log->Records, grown, (long)capacity * 8, (long)log->RecordCount * 8);
            Native.Free(log->Records);
            log->Records = grown;
            log->RecordCapacity = capacity;
        }

        void LoadLoggedInputs()
        {
            if (!resimulating)
            {
                return;
            }

            var log = LogOf(frame);
            for (var w = 0; w < writers.Length; w++)
            {
                writers[w].Replay(w == 0 ? log->Refused : 0);
            }

            ApplyLoggedActions(log);
        }

        void ApplyLoggedActions(FrameLog* log)
        {
            applying = true;
            try
            {
                for (var i = 0; i < log->ActionCount; i++)
                {
                    log->Actions[i] = Apply(log->Actions[i]);
                }
            }
            finally
            {
                applying = false;
            }
        }

        ColumnAction Apply(ColumnAction action)
        {
            switch (action.Kind)
            {
                case ColumnActionKind.Spawn:
                    if ((uint)action.Template >= (uint)tables->TemplateCount)
                    {
                        return action.Done(-1, false);
                    }

                    try
                    {
                        return action.Done(Spawn(action.Template), true);
                    }
                    catch (InvalidOperationException)
                    {
                        return action.Done(-1, false);
                    }
                case ColumnActionKind.Despawn:
                    if (!IsAlive(action.Slot))
                    {
                        return action.Done(action.Slot, false);
                    }

                    Despawn(action.Slot);
                    return action.Done(action.Slot, true);
                case ColumnActionKind.Set:
                    if (!IsAlive(action.Slot) || (uint)action.Lane >= (uint)(laneCount - 2))
                    {
                        return action.Done(action.Slot, false);
                    }

                    Set(action.Slot, action.Lane, action.Value);
                    return action.Done(action.Slot, true);
                case ColumnActionKind.Send:
                    writers[0].Send(action.Slot, action.Channel, action.Amount, action.Hits);
                    return action.Done(action.Slot, true);
                case ColumnActionKind.SendFrom:
                    writers[0].SendFrom(action.Source, action.Slot, action.Channel, action.Amount, action.Hits);
                    return action.Done(action.Slot, true);
                default:
                    return action.Done(action.Slot, false);
            }
        }

        void OpenRecordLog(int total)
        {
            if (logs == null || resimulating)
            {
                return;
            }

            var log = LogOf(frame);
            EnsureRecords(log, total);
            log->RecordCount = total;
            log->Refused = 0;
            var at = 0;
            for (var w = 0; w < writers.Length; w++)
            {
                writerOffsets[w] = at;
                at += writers[w].Count;
            }
        }

        ulong* LoggedRecords(int writer, out int count)
        {
            var log = LogOf(frame - 1);
            var from = (int)((long)log->RecordCount * writer / writers.Length);
            count = (int)((long)log->RecordCount * (writer + 1) / writers.Length) - from;
            return log->Records + from;
        }

        void LogRecords(int writer, ulong* records, int count, long refused)
        {
            if (logs == null || resimulating)
            {
                return;
            }

            var log = LogOf(frame - 1);
            log->Refused += refused;
            Buffer.MemoryCopy(records, log->Records + writerOffsets[writer], (long)count * 8, (long)count * 8);
        }

        void KeepChunk(int chunk, int* chunkLanes)
        {
            if (logs == null)
            {
                return;
            }

            var ck = CheckpointOf(frame);
            var bytes = (long)chunkInts * 4;
            Buffer.MemoryCopy(chunkLanes, ck->Lanes + (long)chunk * chunkInts, bytes, bytes);
            var gateBytes = (long)Rows * sizeof(SlotGate);
            Buffer.MemoryCopy(gates + (long)chunk * Rows, ck->Gates + (long)chunk * Rows, gateBytes, gateBytes);
        }

        void KeepFrame()
        {
            if (logs == null)
            {
                return;
            }

            var ck = CheckpointOf(frame);
            Buffer.MemoryCopy(alive, ck->Alive, (long)chunkCount * 8, (long)chunkCount * 8);
            Buffer.MemoryCopy(chunkTemplates, ck->Templates, (long)chunkCount * 4, (long)chunkCount * 4);
            KeepOpenChunks(ck);
            for (var w = 0; w < writers.Length; w++)
            {
                ck->Dropped[w] = dropped[w * 8];
            }

            ck->NextChunk = nextChunk;
            ck->Count = Count;
            if (!resimulating)
            {
                LogOf(frame)->RecordCount = 0;
                LogOf(frame)->ActionCount = 0;
                return;
            }

            if (frame == resimulateTo)
            {
                resimulating = false;
                replayMask = null;
                ApplyLoggedActions(LogOf(frame));
            }
        }

        void KeepOpenChunks(Checkpoint* ck)
        {
            var need = openChunks.Length;
            for (var t = 0; t < openChunks.Length; t++)
            {
                need += openChunks[t].Count;
            }

            if (need > ck->OpenCapacity)
            {
                Native.Free(ck->Open);
                ck->OpenCapacity = Math.Max(need, ck->OpenCapacity * 2);
                ck->Open = Native.Zeroed<int>(ck->OpenCapacity);
            }

            var at = openChunks.Length;
            for (var t = 0; t < openChunks.Length; t++)
            {
                var open = openChunks[t];
                ck->Open[t] = open.Count;
                for (var i = 0; i < open.Count; i++)
                {
                    ck->Open[at++] = open[i];
                }
            }
        }

        void Restore(int at)
        {
            var ck = CheckpointOf(at);
            var bytes = (long)chunkInts * 4;
            var gateBytes = (long)Rows * sizeof(SlotGate);
            for (var c = 0; c < chunkCount; c++)
            {
                var then = ck->Alive[c];
                var now = alive[c];
                if ((then | now) == 0 || !Replays(c))
                {
                    continue;
                }

                if (then != 0)
                {
                    Buffer.MemoryCopy(ck->Lanes + (long)c * chunkInts, ChunkLanes(c), bytes, bytes);
                    Buffer.MemoryCopy(ck->Gates + (long)c * Rows, gates + (long)c * Rows, gateBytes, gateBytes);
                }
                else
                {
                    Native.Clear(ChunkLanes(c), bytes);
                    Native.Clear(gates + (long)c * Rows, gateBytes);
                }
            }

            Buffer.MemoryCopy(ck->Alive, alive, (long)chunkCount * 8, (long)chunkCount * 8);
            Buffer.MemoryCopy(ck->Templates, chunkTemplates, (long)chunkCount * 4, (long)chunkCount * 4);
            Array.Clear(listed, 0, listed.Length);
            var from = openChunks.Length;
            for (var t = 0; t < openChunks.Length; t++)
            {
                var open = openChunks[t];
                open.Clear();
                for (var i = 0; i < ck->Open[t]; i++)
                {
                    var chunk = ck->Open[from++];
                    open.Add(chunk);
                    listed[chunk] = true;
                }
            }

            for (var w = 0; w < writers.Length; w++)
            {
                dropped[w * 8] = ck->Dropped[w];
                writers[w].Clear();
                writers[w].TakeRefused();
            }

            Native.Clear(stamps, (long)writers.Length * chunkCount * 4);
            nextChunk = ck->NextChunk;
            Count = ck->Count;
            frame = at;
        }

        void CheckHistory()
        {
            Live();
            if (logs == null)
            {
                throw new InvalidOperationException("this world keeps no history: open it with history > 0");
            }

            if (resimulating)
            {
                throw new InvalidOperationException("the world is resimulating: step it until Resimulating is false");
            }
        }

        void CheckWindow(int at, int end)
        {
            if (at < OldestFrame || at > end)
            {
                throw new ArgumentOutOfRangeException(nameof(at), "frame " + at + " is outside the history window " + OldestFrame + " to " + end);
            }
        }

        public void Rewind(int frame)
        {
            Editable();
            CheckHistory();
            CheckWindow(frame, this.frame);
            Restore(frame);
            LogOf(frame)->RecordCount = 0;
            LogOf(frame)->ActionCount = 0;
            LogOf(frame)->Refused = 0;
            if (editedFrom >= frame)
            {
                ForgetEdits();
            }
        }

        public FrameInputs Inputs(int frame)
        {
            CheckHistory();
            CheckWindow(frame, this.frame - 1);
            return new FrameInputs(this, frame);
        }

        public void BeginResimulate()
        {
            Editable();
            CheckHistory();
            for (var w = 0; w < writers.Length; w++)
            {
                if (writers[w].Count > 0)
                {
                    throw new InvalidOperationException("records are waiting for this frame: resimulate before sending them");
                }
            }

            if (editedFrom == int.MaxValue)
            {
                return;
            }

            if (editedFrom < OldestFrame)
            {
                throw new InvalidOperationException("frame " + editedFrom + " was edited but has left the history window (oldest is " + OldestFrame + ")");
            }

            var from = editedFrom;
            replayMask = ReplayMaskFrom(from);
            ForgetEdits();
            resimulateTo = frame;
            Restore(from);
            resimulating = true;
        }

        public void Resimulate()
        {
            BeginResimulate();
            while (resimulating)
            {
                Step();
            }
        }

        internal FrameLog ReadLog(int at)
        {
            CheckHistory();
            CheckWindow(at, frame - 1);
            return *LogOf(at);
        }

        FrameLog* EditLog(int at)
        {
            CheckHistory();
            CheckWindow(at, frame - 1);
            editedFrom = Math.Min(editedFrom, at);
            return LogOf(at);
        }

        internal ColumnAction ReadAction(int at, int index)
        {
            var log = ReadLog(at);
            CheckIndex(index, log.ActionCount);
            return log.Actions[index];
        }

        internal void InsertAction(int at, int index, ColumnAction action)
        {
            var log = EditLog(at);
            CheckIndex(index, log->ActionCount + 1);
            if (log->ActionCount == log->ActionCapacity)
            {
                GrowActions(log);
            }

            for (var i = log->ActionCount; i > index; i--)
            {
                log->Actions[i] = log->Actions[i - 1];
            }

            log->Actions[index] = action;
            log->ActionCount++;
            MarkEdited(action);
        }

        internal void ReplaceAction(int at, int index, ColumnAction action)
        {
            var log = EditLog(at);
            CheckIndex(index, log->ActionCount);
            MarkEdited(log->Actions[index]);
            log->Actions[index] = action;
            MarkEdited(action);
        }

        internal void RemoveAction(int at, int index)
        {
            var log = EditLog(at);
            CheckIndex(index, log->ActionCount);
            MarkEdited(log->Actions[index]);
            log->ActionCount--;
            for (var i = index; i < log->ActionCount; i++)
            {
                log->Actions[i] = log->Actions[i + 1];
            }
        }

        internal ulong ReadRecord(int at, int index)
        {
            var log = ReadLog(at);
            CheckIndex(index, log.RecordCount);
            return log.Records[index];
        }

        internal void InsertRecord(int at, ulong record)
        {
            var log = EditLog(at);
            if (log->RecordCount == int.MaxValue)
            {
                throw new InvalidOperationException("a frame holds at most " + int.MaxValue + " records");
            }

            EnsureRecords(log, log->RecordCount + 1);
            log->Records[log->RecordCount++] = record;
            MarkEdited(record);
        }

        internal void ReplaceRecord(int at, int index, ulong record)
        {
            var log = EditLog(at);
            CheckIndex(index, log->RecordCount);
            MarkEdited(log->Records[index]);
            log->Records[index] = record;
            MarkEdited(record);
        }

        internal void RemoveRecord(int at, int index)
        {
            var log = EditLog(at);
            CheckIndex(index, log->RecordCount);
            MarkEdited(log->Records[index]);
            log->RecordCount--;
            log->Records[index] = log->Records[log->RecordCount];
        }

        static void CheckIndex(int index, int count)
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        void FreeHistory()
        {
            if (logs == null)
            {
                return;
            }

            for (var s = 0; s < ring; s++)
            {
                Native.Free(logs[s].Records);
                Native.Free(logs[s].Actions);
                var ck = checkpoints + s;
                Native.Free(ck->Lanes);
                Native.Free(ck->Gates);
                Native.Free(ck->Alive);
                Native.Free(ck->Templates);
                Native.Free(ck->Open);
                Native.Free(ck->Dropped);
            }

            Native.Free(logs);
            Native.Free(checkpoints);
            Native.Free(writerOffsets);
            Native.Free(editedChunks);
            Native.Free(replayChunks);
            editedChunks = null;
            replayChunks = null;
            replayMask = null;
            logs = null;
            checkpoints = null;
            writerOffsets = null;
        }
    }
}
