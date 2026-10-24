using System;

namespace Sv;

public sealed unsafe partial class Board
{
    internal struct SetUndoFrame
    {
        public int SetCount;

        public int SetCapacity;

        public int BucketMask;

        public int SetFreeHead;

        public int LiveSets;

        public int ContentUsed;

        public int ContentCapacity;

        public long Generation;

        public void* Buckets;

        public void* SetStat;

        public void* SetHash;

        public void* SetComputed;

        public void* SetContentOffset;

        public void* SetContentCount;

        public void* SetContentSpan;

        public void* SetRefCount;

        public void* SetFolded;

        public void* SetFreeNext;

        public void* SetFreedAt;

        public void* ContentArena;
    }

    internal struct BlockUndoFrame
    {
        public int BlockCount;

        public int BlockCapacity;

        public int BucketMask;

        public int BlockFreeHead;

        public void* Buckets;

        public void* BlockSets;

        public void* BlockHash;

        public void* BlockRefCount;

        public void* BlockFreeNext;
    }

    internal struct BoardUndoFrame
    {
        public int LiveCount;

        public int FreeHint;

        public long NextInsertionId;

        public int RecordCount;

        public int RecordCap;

        public int RecordFree;

        public void* Records;

        public int WheelEntryCount;

        public int WheelEntryCap;

        public int WheelEntryFree;

        public int WheelEntryLive;

        public long WheelTick;

        public int WheelOverdue;

        public void* WheelEntries;

        public int DirtyTouchedCount;

        public int DirtyTouchedCap;

        public void* DirtyTouched;

        public int AcquiredTouchedCount;

        public ulong Digest;

        public byte DigestPagesDirty;

        public byte PageDigestsRewritten;

        public SetUndoFrame Set;

        public BlockUndoFrame Block;
    }

    UndoLog? undo;

    BoardUndoFrame[]? undoFrames;

    /// <summary>
    /// Undo frames currently retained by the open window (0 when no window is open). Rolling back
    /// is limited to this many frames.
    /// </summary>
    public int UndoFrames => undo?.Frames ?? 0;

    internal UndoLog? ActiveUndo => undo;

    internal int UndoTop => undo?.Top ?? -1;

    /// <summary>
    /// Opens an undo window retaining the last <paramref name="frames"/> frame marks and returns
    /// the board to O(Δ) rollback: every mutation after the window opens logs its prior bytes, set
    /// reclamation waits out the window, and growth reallocations quarantine their old blocks
    /// instead of freeing them. Call <see cref="BeginUndoFrame"/> once per frame boundary;
    /// <see cref="Rollback(int)"/> restores the state of any boundary still inside the window.
    /// Opening a window discards any previous one. Loading state, renumbering slots, rebinding or
    /// invalidating bindings closes the window — bindings are not state, and a rolled-back write
    /// must never target a host column that moved.
    /// </summary>
    public void BeginUndoWindow(int frames)
    {
        EndUndoWindow();
        undo = new UndoLog(frames);
        undoFrames = new BoardUndoFrame[frames];
        if (table != null)
        {
            table.Undo = undo;
            table.ReclaimHorizon = frames;
        }

        if (blocks != null)
        {
            blocks.Undo = undo;
        }

        BeginUndoFrame();
    }

    /// <summary>
    /// Closes the undo window and releases its log; the current state becomes permanent.
    /// </summary>
    public void EndUndoWindow()
    {
        if (undo == null)
        {
            return;
        }

        SweepUndo();
        undo.Dispose();
        undo = null;
        undoFrames = null;
        if (table != null)
        {
            table.Undo = null;
            table.ReclaimHorizon = 0;
        }

        if (blocks != null)
        {
            blocks.Undo = null;
        }
    }

    /// <summary>
    /// Marks a frame boundary: snapshots the mutating scalars and opens the entry frame that the
    /// mutations until the next mark roll into. When the window is full the oldest mark is
    /// committed — its mutations become permanent and can no longer be rolled back.
    /// </summary>
    public void BeginUndoFrame()
    {
        if (undo == null)
        {
            throw new InvalidOperationException("sv: no undo window is open");
        }

        var previousBottom = undo.Bottom;
        undo.OpenFrame();
        if (undo.Bottom > previousBottom)
        {
            undo.CommitFramesBelow(undo.Bottom);
        }

        CaptureUndoFrame(ref undoFrames![(nint)(undo.Top % undoFrames.Length)]);
        SweepUndo();
    }

    /// <summary>
    /// Rolls the board back to the boundary <paramref name="frames"/> marks ago, bit-exactly:
    /// logged writes replay backwards, frame scalars (counts, free heads, table pointers, digest)
    /// restore from the mark's snapshot, and blocks allocated inside the rolled-back frames free.
    /// Equivalent to loading a snapshot saved at that boundary.
    /// </summary>
    public void Rollback(int frames)
    {
        var log = undo;
        if (log == null)
        {
            throw new InvalidOperationException("sv: no undo window is open");
        }

        if (frames < 1 || frames > log.Frames)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), $"sv: rollback depth must be in [1, {log.Frames}]");
        }

        RollbackTo(log.Top - frames + 1);
    }

    internal void RollbackTo(int target)
    {
        var log = undo!;
        if (target < log.Bottom || target > log.Top)
        {
            throw new ArgumentOutOfRangeException(nameof(target), $"sv: rollback target must be in [{log.Bottom}, {log.Top}]");
        }

        var suspectPages = false;
        for (var ordinal = log.Top; ordinal >= target && !suspectPages; ordinal--)
        {
            suspectPages = undoFrames![(nint)(ordinal % undoFrames.Length)].PageDigestsRewritten != 0;
        }

        log.UndoFrames(log.Top, target);
        RestoreUndoFrame(ref undoFrames![(nint)(target % undoFrames.Length)]);
        if (suspectPages)
        {
            digestPagesDirty = true;
        }

        log.DropFreshFrom(target, arena);
        log.TruncateTo(target);
        MarkAllKeyframesDirty();
        SweepUndo();
    }

    internal void InvalidateUndo()
    {
        EndUndoWindow();
    }

    internal T* UndoGrow<T>(UndoArrayId id, T* old, int oldCount, int newCount) where T : unmanaged
    {
        return undo == null ? arena.Realloc(old, oldCount, newCount) : undo.ReallocTracked(arena, id, old, oldCount, newCount);
    }

    void CaptureUndoFrame(ref BoardUndoFrame frame)
    {
        frame.LiveCount = LiveCount;
        frame.FreeHint = freeHint;
        frame.NextInsertionId = nextInsertionId;
        frame.RecordCount = recordCount;
        frame.RecordCap = recordCap;
        frame.RecordFree = recordFree;
        frame.Records = records;
        frame.WheelEntryCount = wheelEntryCount;
        frame.WheelEntryCap = wheelEntryCap;
        frame.WheelEntryFree = wheelEntryFree;
        frame.WheelEntryLive = wheelEntryLive;
        frame.WheelTick = wheelTick;
        frame.WheelOverdue = wheelOverdue;
        frame.WheelEntries = wheelEntries;
        frame.DirtyTouchedCount = dirtyTouchedCount;
        frame.DirtyTouchedCap = dirtyTouchedCap;
        frame.DirtyTouched = dirtyTouched;
        frame.AcquiredTouchedCount = acquiredTouchedCount;
        frame.Digest = digest;
        frame.DigestPagesDirty = (byte)(digestPagesDirty ? 1 : 0);
        frame.PageDigestsRewritten = 0;
        table?.CaptureUndo(ref frame.Set);
        blocks?.CaptureUndo(ref frame.Block);
    }

    void RestoreUndoFrame(ref BoardUndoFrame frame)
    {
        LiveCount = frame.LiveCount;
        freeHint = frame.FreeHint;
        nextInsertionId = frame.NextInsertionId;
        recordCount = frame.RecordCount;
        recordCap = frame.RecordCap;
        recordFree = frame.RecordFree;
        records = (ModRecord*)frame.Records;
        wheelEntryCount = frame.WheelEntryCount;
        wheelEntryCap = frame.WheelEntryCap;
        wheelEntryFree = frame.WheelEntryFree;
        wheelEntryLive = frame.WheelEntryLive;
        wheelTick = frame.WheelTick;
        wheelOverdue = frame.WheelOverdue;
        wheelEntries = (WheelEntry*)frame.WheelEntries;
        dirtyTouchedCount = frame.DirtyTouchedCount;
        dirtyTouchedCap = frame.DirtyTouchedCap;
        dirtyTouched = (int*)frame.DirtyTouched;
        acquiredTouchedCount = frame.AcquiredTouchedCount;
        digest = frame.Digest;
        digestPagesDirty = frame.DigestPagesDirty != 0;
        table?.RestoreUndo(ref frame.Set);
        blocks?.RestoreUndo(ref frame.Block);
    }

    void SweepUndo()
    {
        var log = undo!;
        for (var i = 0; i < log.QuarantineCount; i++)
        {
            ref var entry = ref log.QuarantineAt(i);
            if (entry.Fresh)
            {
                continue;
            }

            var live = UndoPointerOf(entry.Id) == entry.Pointer;
            if (!live)
            {
                for (var ordinal = log.Bottom; ordinal <= log.Top && !live; ordinal++)
                {
                    live = ordinal >= entry.First && ordinal <= entry.Last;
                }
            }

            if (live)
            {
                continue;
            }

            arena.FreeBytes(entry.Pointer, entry.Bytes);
            log.RemoveQuarantineAt(i);
            i--;
        }
    }

    void* UndoPointerOf(UndoArrayId id)
    {
        if (id == UndoArrayId.Records)
        {
            return records;
        }

        if (id == UndoArrayId.WheelEntries)
        {
            return wheelEntries;
        }

        if (id == UndoArrayId.DirtyTouched)
        {
            return dirtyTouched;
        }

        var pointer = table?.UndoPointerOf(id);
        if (pointer != null)
        {
            return pointer;
        }

        return blocks!.UndoPointerOf(id);
    }

    static void* UndoFramePointer(ref BoardUndoFrame frame, UndoArrayId id)
    {
        if (id == UndoArrayId.Records)
        {
            return frame.Records;
        }

        if (id == UndoArrayId.WheelEntries)
        {
            return frame.WheelEntries;
        }

        if (id == UndoArrayId.DirtyTouched)
        {
            return frame.DirtyTouched;
        }

        if (id >= UndoArrayId.BlockBuckets)
        {
            return id switch
            {
                UndoArrayId.BlockBuckets => frame.Block.Buckets,
                UndoArrayId.BlockSets => frame.Block.BlockSets,
                UndoArrayId.BlockHash => frame.Block.BlockHash,
                UndoArrayId.BlockRefCount => frame.Block.BlockRefCount,
                UndoArrayId.BlockFreeNext => frame.Block.BlockFreeNext,
                _ => null,
            };
        }

        return id switch
        {
            UndoArrayId.SetBuckets => frame.Set.Buckets,
            UndoArrayId.SetStat => frame.Set.SetStat,
            UndoArrayId.SetHash => frame.Set.SetHash,
            UndoArrayId.SetComputed => frame.Set.SetComputed,
            UndoArrayId.SetContentOffset => frame.Set.SetContentOffset,
            UndoArrayId.SetContentCount => frame.Set.SetContentCount,
            UndoArrayId.SetContentSpan => frame.Set.SetContentSpan,
            UndoArrayId.SetRefCount => frame.Set.SetRefCount,
            UndoArrayId.SetFolded => frame.Set.SetFolded,
            UndoArrayId.SetFreeNext => frame.Set.SetFreeNext,
            UndoArrayId.SetFreedAt => frame.Set.SetFreedAt,
            UndoArrayId.ContentArena => frame.Set.ContentArena,
            _ => null,
        };
    }
}
