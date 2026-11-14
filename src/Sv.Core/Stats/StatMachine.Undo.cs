namespace Sv
{
    public sealed unsafe partial class StatMachine
    {
        StatUndoEntry* undoLog;
        int undoCount;
        int undoCapacity;
        StatUndoMark* marks;
        int markRing;
        int markOldest;
        int markCount;
        bool undoing;
        ulong** markDirty;
        int* markDirtyCount;
        int* markDirtyCapacity;

        public int UndoEpochs => markRing;

        public int HeldEpochs => markCount;

        void InitUndo(int undoEpochs)
        {
            markRing = undoEpochs;
            if (markRing == 0)
            {
                return;
            }

            marks = (StatUndoMark*)Memory.Alloc((nuint)markRing, (nuint)sizeof(StatUndoMark));
            markDirty = (ulong**)Memory.AllocZeroed((nuint)markRing, (nuint)sizeof(ulong*));
            markDirtyCount = (int*)Memory.AllocZeroed((nuint)markRing, sizeof(int));
            markDirtyCapacity = (int*)Memory.AllocZeroed((nuint)markRing, sizeof(int));
            MarkEpoch(world.Epoch);
        }

        void FreeUndo()
        {
            if (undoLog != null)
            {
                Memory.Free(undoLog);
            }

            if (marks != null)
            {
                Memory.Free(marks);
            }

            if (markDirty != null)
            {
                for (var at = 0; at < markRing; at++)
                {
                    if (markDirty[at] != null)
                    {
                        Memory.Free(markDirty[at]);
                    }
                }

                Memory.Free(markDirty);
                Memory.Free(markDirtyCount);
                Memory.Free(markDirtyCapacity);
            }
        }

        void LogUndo(StatUndoAction action, in ModifierRecord record)
        {
            if (markRing == 0 || undoing)
            {
                return;
            }

            undoLog = Grow(undoLog, undoCount, ref undoCapacity, undoCount + 1);
            undoLog[undoCount].Record = record;
            undoLog[undoCount].Action = action;
            undoCount++;
        }

        StatUndoMark* MarkAt(int age) => marks + (markOldest + age) % markRing;

        public void MarkEpoch(uint epoch)
        {
            if (markRing == 0)
            {
                return;
            }

            if (markCount > 0 && MarkAt(markCount - 1)->Epoch == epoch)
            {
                return;
            }

            if (markCount == markRing)
            {
                markOldest = (markOldest + 1) % markRing;
                markCount--;
                var dropped = MarkAt(0)->LogStart;
                if (dropped > 0)
                {
                    Memory.Copy(undoLog + dropped, undoLog, (nuint)(undoCount - dropped) * (nuint)sizeof(StatUndoEntry));
                    undoCount -= dropped;
                    for (var age = 0; age < markCount; age++)
                    {
                        MarkAt(age)->LogStart -= dropped;
                    }
                }
            }

            var mark = MarkAt(markCount);
            mark->Epoch = epoch;
            mark->LogStart = undoCount;
            mark->NextId = nextId;
            var place = (int)(mark - marks);
            markDirty[place] = Grow(markDirty[place], 0, ref markDirtyCapacity[place], dirtyCount);
            if (dirtyCount > 0)
            {
                Memory.Copy(dirty, markDirty[place], (nuint)dirtyCount * sizeof(ulong));
            }

            markDirtyCount[place] = dirtyCount;
            markCount++;
        }

        public bool Rewind(uint epoch)
        {
            var found = -1;
            for (var age = markCount - 1; age >= 0 && found < 0; age--)
            {
                found = MarkAt(age)->Epoch == epoch ? age : -1;
            }

            if (found < 0)
            {
                return false;
            }

            var place = (int)(MarkAt(found) - marks);
            var mark = marks[place];
            undoing = true;
            for (var at = undoCount - 1; at >= mark.LogStart; at--)
            {
                var entry = undoLog[at];
                if (entry.Action == StatUndoAction.Added)
                {
                    RemoveById(entry.Record.Slot, entry.Record.Id);
                }
                else
                {
                    Insert(entry.Record);
                }
            }

            undoing = false;
            undoCount = mark.LogStart;
            nextId = mark.NextId;
            markCount = found + 1;
            dirtyCount = markDirtyCount[place];
            dirty = Grow(dirty, 0, ref dirtyCapacity, dirtyCount);
            if (dirtyCount > 0)
            {
                Memory.Copy(markDirty[place], dirty, (nuint)dirtyCount * sizeof(ulong));
            }

            changedPairCount = 0;
            changedChunkCount = 0;
            return true;
        }

        void RemoveById(int slot, long id)
        {
            for (var at = HeadPeek(slot); at != NoRecord; at = records[at].Next)
            {
                if (records[at].Id == id)
                {
                    Unlink(at);
                    return;
                }
            }
        }
    }
}
