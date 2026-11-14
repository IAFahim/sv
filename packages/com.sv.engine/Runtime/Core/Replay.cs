using System;

namespace Sv
{
    public enum LatePolicy : byte
    {
        Clamp = 0,
        Reject = 1,
    }

    public readonly struct OutwardEvent
    {
        public readonly int Slot;
        public readonly int Event;
        public readonly bool Entered;

        public OutwardEvent(int slot, int raised, bool entered)
        {
            Slot = slot;
            Event = raised;
            Entered = entered;
        }

        public ulong Key => ((ulong)(uint)Slot << 32) | ((ulong)(uint)Event << 1) | (Entered ? 1ul : 0ul);

        public static OutwardEvent FromKey(ulong key) => new OutwardEvent((int)(key >> 32), (int)((uint)key >> 1), (key & 1ul) != 0ul);
    }

    public readonly struct EventChange
    {
        public readonly long Frame;
        public readonly ulong Event;
        public readonly bool Added;

        public EventChange(long frame, ulong raised, bool added)
        {
            Frame = frame;
            Event = raised;
            Added = added;
        }
    }

    public unsafe struct EventBuffer
    {
        public ulong* Keys;
        public int Count;
        public int Capacity;

        public void Add(in OutwardEvent raised)
        {
            if (Count == Capacity)
            {
                var grown = Math.Max(64, Capacity * 2);
                Keys = (ulong*)Memory.Realloc(Keys, (nuint)grown * sizeof(ulong));
                Capacity = grown;
            }

            Keys[Count] = raised.Key;
            Count++;
        }

        public void Dispose()
        {
            Memory.Free(Keys);
            this = default;
        }
    }

    public unsafe struct FrameInputs
    {
        public long Frame;
        public LoggedRecord* Records;
        public int RecordCount;
        public StructuralCommand* Commands;
        public int CommandCount;
        public byte* StatOps;
        public int StatOpCount;
        public int StatOpBytes;
    }

    public unsafe interface IFrameStep
    {
        void Step(World world, in FrameInputs inputs, ref EventBuffer published);
    }

    public static unsafe class EventOrder
    {
        public static void Sort(ulong* keys, int count)
        {
            for (var start = count / 2 - 1; start >= 0; start--)
            {
                Sift(keys, start, count);
            }

            for (var end = count - 1; end > 0; end--)
            {
                var top = keys[0];
                keys[0] = keys[end];
                keys[end] = top;
                Sift(keys, 0, end);
            }
        }

        static void Sift(ulong* keys, int root, int count)
        {
            while (true)
            {
                var child = 2 * root + 1;
                if (child >= count)
                {
                    return;
                }

                if (child + 1 < count && keys[child + 1] > keys[child])
                {
                    child++;
                }

                if (keys[root] >= keys[child])
                {
                    return;
                }

                var held = keys[root];
                keys[root] = keys[child];
                keys[child] = held;
                root = child;
            }
        }
    }

    public sealed unsafe class EventLog : IDisposable
    {
        struct FrameEvents
        {
            public long Frame;
            public ulong* Keys;
            public int Count;
            public int Capacity;
        }

        readonly int span;
        FrameEvents* frames;

        public EventLog(int frameSpan)
        {
            if (frameSpan < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(frameSpan));
            }

            span = frameSpan;
            frames = (FrameEvents*)Memory.AllocZeroed((nuint)span, (nuint)sizeof(FrameEvents));
            for (var at = 0; at < span; at++)
            {
                frames[at].Frame = -1L;
            }
        }

        public bool Holds(long frame) => frame >= 0L && SlotOf(frame)->Frame == frame;

        public ulong* Events(long frame, out int count)
        {
            if (!Holds(frame))
            {
                count = 0;
                return null;
            }

            var slot = SlotOf(frame);
            count = slot->Count;
            return slot->Keys;
        }

        public void Record(long frame, ulong* keys, int count)
        {
            if (frame < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }

            var slot = SlotOf(frame);
            if (count > slot->Capacity)
            {
                var grown = Math.Max(count, Math.Max(64, slot->Capacity * 2));
                slot->Keys = (ulong*)Memory.Realloc(slot->Keys, (nuint)grown * sizeof(ulong));
                slot->Capacity = grown;
            }

            if (count > 0)
            {
                Memory.Copy(keys, slot->Keys, (nuint)count * sizeof(ulong));
            }

            EventOrder.Sort(slot->Keys, count);
            slot->Count = count;
            slot->Frame = frame;
        }

        public void Truncate(long after)
        {
            for (var at = 0; at < span; at++)
            {
                if (frames[at].Frame > after)
                {
                    frames[at].Frame = -1L;
                    frames[at].Count = 0;
                }
            }
        }

        FrameEvents* SlotOf(long frame) => frames + (int)(frame % span);

        public void Dispose()
        {
            if (frames == null)
            {
                return;
            }

            for (var at = 0; at < span; at++)
            {
                Memory.Free(frames[at].Keys);
            }

            Memory.Free(frames);
            frames = null;
        }
    }

    public sealed unsafe class Replay : IDisposable
    {
        readonly InputLog log;
        readonly EventLog published;
        LoggedRecord* records;
        int recordCapacity;
        EventBuffer replayed;
        EventChange* changes;
        int changeCount;
        int changeCapacity;
        long earliestLate;

        public Replay(InputLog log, EventLog published)
        {
            this.log = log;
            this.published = published;
            earliestLate = long.MaxValue;
        }

        public EventChange* Changes => changes;

        public int ChangeCount => changeCount;

        public long EarliestLate => earliestLate;

        public void ClearChanges() => changeCount = 0;

        public long Late(World world, long frame, LoggedRecord* lateCanonical, int count, LatePolicy policy)
        {
            var oldest = world.Journal.BeginOf(world.OldestEpoch);
            if (frame < oldest)
            {
                if (policy == LatePolicy.Reject)
                {
                    return -1L;
                }

                frame = oldest;
            }

            if (frame >= world.Frame)
            {
                return world.Frame;
            }

            log.Insert(frame, lateCanonical, count);
            earliestLate = Math.Min(earliestLate, frame);
            return frame;
        }

        public bool Resolve<TStep>(World world, ref TStep step) where TStep : struct, IFrameStep
        {
            if (earliestLate >= world.Frame)
            {
                earliestLate = long.MaxValue;
                return false;
            }

            var now = world.Frame;
            var epoch = world.EpochAtOrBefore(earliestLate);
            earliestLate = long.MaxValue;
            if (epoch == 0u || !world.Rewind(epoch))
            {
                return false;
            }

            Run(world, now, ref step);
            return true;
        }

        public void Run<TStep>(World world, long to, ref TStep step) where TStep : struct, IFrameStep
        {
            while (world.Frame < to)
            {
                var frame = world.Frame;
                var inputs = Inputs(frame);
                replayed.Count = 0;
                step.Step(world, inputs, ref replayed);
                EventOrder.Sort(replayed.Keys, replayed.Count);
                var before = published.Events(frame, out var beforeCount);
                Difference(frame, before, beforeCount, replayed.Keys, replayed.Count);
                published.Record(frame, replayed.Keys, replayed.Count);
                world.EndFrame();
            }
        }

        public FrameInputs Inputs(long frame)
        {
            var inputs = default(FrameInputs);
            inputs.Frame = frame;
            var count = log.RecordCount(frame);
            if (count > recordCapacity)
            {
                var grown = Math.Max(count, recordCapacity * 2);
                records = (LoggedRecord*)Memory.Realloc(records, (nuint)grown * (nuint)sizeof(LoggedRecord));
                recordCapacity = grown;
            }

            inputs.Records = records;
            inputs.RecordCount = log.CopyRecords(frame, records, recordCapacity);
            inputs.Commands = log.Commands(frame, out inputs.CommandCount);
            inputs.StatOps = log.StatOps(frame, out inputs.StatOpCount);
            inputs.StatOpBytes = log.StatOpBytes;
            return inputs;
        }

        public static int Apply(World world, in StructuralCommand command) =>
            command.Kind == CommandKind.Spawn ? world.Acquire(command.Template) : world.Release(command.Slot) ? command.Slot : -1;

        void Difference(long frame, ulong* before, int beforeCount, ulong* after, int afterCount)
        {
            var first = 0;
            var second = 0;
            while (first < beforeCount || second < afterCount)
            {
                if (second == afterCount || (first < beforeCount && before[first] < after[second]))
                {
                    Change(new EventChange(frame, before[first], false));
                    first++;
                }
                else if (first == beforeCount || after[second] < before[first])
                {
                    Change(new EventChange(frame, after[second], true));
                    second++;
                }
                else
                {
                    first++;
                    second++;
                }
            }
        }

        void Change(EventChange change)
        {
            if (changeCount == changeCapacity)
            {
                var grown = Math.Max(64, changeCapacity * 2);
                changes = (EventChange*)Memory.Realloc(changes, (nuint)grown * (nuint)sizeof(EventChange));
                changeCapacity = grown;
            }

            changes[changeCount] = change;
            changeCount++;
        }

        public void Dispose()
        {
            Memory.Free(records);
            Memory.Free(changes);
            replayed.Dispose();
            records = null;
            changes = null;
        }
    }
}
