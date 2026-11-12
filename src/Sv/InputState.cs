using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public sealed unsafe class InputState : IDisposable
    {
        public int Axes { get; }

        public int ButtonWords { get; }

        public int RingTicks { get; }

        private readonly uint* axisBits;

        private readonly ulong* buttons;

        private readonly ulong* previous;

        private readonly ulong* pressed;

        private readonly ulong* released;

        private readonly byte* ring;

        private readonly int ringStride;

        private readonly long* ringTickIds;

        private int ringHead;

        private long tickCounter;

        private int replayCursor;

        private bool disposed;

        internal UndoLog? Undo;

        int[]? undoRingHeads;

        long[]? undoTickCounters;

        int[]? undoReplayCursors;

        InputState(int axes, int buttonWords, int ringTicks)
        {
            Axes = axes;
            ButtonWords = buttonWords;
            RingTicks = ringTicks;
            axisBits = (uint*)Memory.AllocZeroed((nuint)axes * sizeof(uint));
            buttons = (ulong*)Memory.AllocZeroed((nuint)buttonWords * sizeof(ulong));
            previous = (ulong*)Memory.AllocZeroed((nuint)buttonWords * sizeof(ulong));
            pressed = (ulong*)Memory.AllocZeroed((nuint)buttonWords * sizeof(ulong));
            released = (ulong*)Memory.AllocZeroed((nuint)buttonWords * sizeof(ulong));
            ringStride = axes * sizeof(uint) + buttonWords * sizeof(ulong);
            ring = (byte*)Memory.AllocZeroed((nuint)ringTicks * (nuint)ringStride);
            ringTickIds = (long*)Memory.Alloc((nuint)ringTicks * sizeof(long));
            for (var i = 0; i < ringTicks; i++)
            {
                ringTickIds[i] = -1;
            }

            ringHead = 0;
            tickCounter = 0;
            replayCursor = -1;
        }

        public static InputState Create(int axes, int buttonWords, int ringTicks)
        {
            if (axes < 1 || buttonWords < 1 || ringTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(axes), "sv: axes, buttonWords and ringTicks must be >= 1");
            }

            return new InputState(axes, buttonWords, ringTicks);
        }

        public long Tick => tickCounter;

        public long OldestRingTick
        {
            get
            {
                var oldest = ringTickIds[0];
                for (var i = 1; i < RingTicks; i++)
                {
                    if (ringTickIds[i] >= 0 && (oldest < 0 || ringTickIds[i] < oldest))
                    {
                        oldest = ringTickIds[i];
                    }
                }

                return oldest;
            }
        }

        public unsafe void Capture(uint* axesBits, ulong* buttonWords)
        {
            if (axesBits == null)
            {
                throw new ArgumentNullException(nameof(axesBits));
            }

            if (buttonWords == null)
            {
                throw new ArgumentNullException(nameof(buttonWords));
            }

            var ax = (uint*)RingToAxes(ringHead);
            var bt = RingToButtons(ringHead);
            var undo = Undo;
            if (undo != null)
            {
                undo.NoteRange(axisBits, Axes);
                undo.Note(ax, ringStride);
                undo.Note(bt, ButtonWords * sizeof(ulong));
                undo.NoteRange(buttons, ButtonWords);
                undo.NoteRange(previous, ButtonWords);
                undo.NoteRange(pressed, ButtonWords);
                undo.NoteRange(released, ButtonWords);
                undo.Note(ringTickIds + ringHead, sizeof(long));
            }

            for (var i = 0; i < Axes; i++)
            {
                var bits = axesBits[i];
                ax[i] = bits;
                axisBits[i] = bits;
            }

            for (var w = 0; w < ButtonWords; w++)
            {
                bt[w] = buttonWords[w];
            }

            Simd.ApplyButtonWords(buttonWords, buttons, previous, pressed, released, ButtonWords);
            ringTickIds[ringHead] = tickCounter;
            ringHead = ringHead + 1 == RingTicks ? 0 : ringHead + 1;
            tickCounter++;
        }

        uint* RingToAxes(int slot)
        {
            return (uint*)(ring + (nint)slot * ringStride);
        }

        ulong* RingToButtons(int slot)
        {
            return (ulong*)(ring + (nint)slot * ringStride + Axes * sizeof(uint));
        }

        /// <summary>
        /// Positions replay at <paramref name="fromTick"/> so the next <see cref="ReplayStep"/>
        /// re-derives that tick. Button state is seeded from the ring entry of tick fromTick − 1 when
        /// it is still resident, so buttons held across the replay boundary replay as held, not
        /// freshly pressed; when no prior tick exists or it was evicted, both button words are zeroed
        /// and every held button reports as pressed on the first replayed tick.
        /// </summary>
        public unsafe void BeginReplay(long fromTick)
        {
            if (fromTick < 0 || fromTick >= tickCounter)
            {
                throw new ArgumentOutOfRangeException(nameof(fromTick), "sv: replay tick out of captured range");
            }

            if (fromTick < OldestRingTick)
            {
                throw new ArgumentOutOfRangeException(nameof(fromTick), $"sv: tick {fromTick} evicted from ring (oldest {OldestRingTick})");
            }

            var slot = (int)(fromTick % RingTicks);
            var prior = fromTick - 1;
            var priorSlot = (int)(prior % RingTicks);
            var undo = Undo;
            if (undo != null)
            {
                undo.NoteRange(buttons, ButtonWords);
                undo.NoteRange(previous, ButtonWords);
            }

            if (prior >= 0 && ringTickIds[priorSlot] == prior)
            {
                var priorButtons = RingToButtons(priorSlot);
                for (var w = 0; w < ButtonWords; w++)
                {
                    buttons[w] = priorButtons[w];
                    previous[w] = priorButtons[w];
                }
            }
            else
            {
                new Span<ulong>(previous, ButtonWords).Fill(0);
                new Span<ulong>(buttons, ButtonWords).Fill(0);
            }

            replayCursor = slot;
        }

        public unsafe bool ReplayStep()
        {
            if (replayCursor < 0)
            {
                return false;
            }

            var id = ringTickIds[replayCursor];
            if (id < 0)
            {
                replayCursor = -1;
                return false;
            }

            var ax = RingToAxes(replayCursor);
            var bt = RingToButtons(replayCursor);
            var undo = Undo;
            if (undo != null)
            {
                undo.NoteRange(axisBits, Axes);
                undo.NoteRange(buttons, ButtonWords);
                undo.NoteRange(previous, ButtonWords);
                undo.NoteRange(pressed, ButtonWords);
                undo.NoteRange(released, ButtonWords);
            }

            for (var i = 0; i < Axes; i++)
            {
                axisBits[i] = ax[i];
            }

            Simd.ApplyButtonWords(bt, buttons, previous, pressed, released, ButtonWords);
            var next = replayCursor + 1 == RingTicks ? 0 : replayCursor + 1;
            if (ringTickIds[next] <= id)
            {
                replayCursor = -1;
            }
            else
            {
                replayCursor = next;
            }

            return true;
        }

        public bool Down(int button)
        {
            Guard.Button(button, ButtonWords);
            return (buttons[button >> 6] & (1ul << (button & 63))) != 0;
        }

        public bool Pressed(int button)
        {
            Guard.Button(button, ButtonWords);
            return (pressed[button >> 6] & (1ul << (button & 63))) != 0;
        }

        public bool Released(int button)
        {
            Guard.Button(button, ButtonWords);
            return (released[button >> 6] & (1ul << (button & 63))) != 0;
        }

        public uint AxisBits(int axis)
        {
            Guard.Axis(axis, Axes);
            return axisBits[axis];
        }

        public ulong ButtonBits(int word)
        {
            Guard.ButtonWord(word, ButtonWords);
            return buttons[word];
        }

        public float AxisF(int axis)
        {
            Guard.Axis(axis, Axes);
            return Bits.ToSingle(axisBits[axis]);
        }

        const uint StateMagic = 0x4E495653;

        long StateShape => ((long)Axes << 40) ^ ((long)ButtonWords << 20) ^ RingTicks;

        /// <summary>
        /// Saves current axes, button words and edges, the replay ring and its cursors. Warm saves are
        /// managed-allocation-free.
        /// </summary>
        public void SaveState(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            VisitState(ref codec);
        }

        public void LoadState(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            VisitState(ref codec);
            state.EndRead();
        }

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            if (codec.Reading)
            {
                EndUndoWindow();
            }

            codec.Header(StateMagic, StateShape, "input state");
            codec.Bytes(axisBits, (long)Axes * sizeof(uint));
            codec.Bytes(buttons, (long)ButtonWords * sizeof(ulong));
            codec.Bytes(previous, (long)ButtonWords * sizeof(ulong));
            codec.Bytes(pressed, (long)ButtonWords * sizeof(ulong));
            codec.Bytes(released, (long)ButtonWords * sizeof(ulong));
            codec.Bytes(ring, (long)RingTicks * ringStride);
            codec.Bytes(ringTickIds, (long)RingTicks * sizeof(long));
            codec.Value(ref ringHead);
            codec.Value(ref tickCounter);
            codec.Value(ref replayCursor);
        }

        internal void BeginUndoWindow(int frames)
        {
            EndUndoWindow();
            Undo = new UndoLog(frames);
            undoRingHeads = new int[frames];
            undoTickCounters = new long[frames];
            undoReplayCursors = new int[frames];
            Undo.OpenFrame();
            CaptureUndoScalars();
        }

        internal void BeginUndoFrame()
        {
            var undo = Undo;
            if (undo == null)
            {
                return;
            }

            undo.OpenFrame();
            CaptureUndoScalars();
        }

        void CaptureUndoScalars()
        {
            var undo = Undo!;
            undoRingHeads![undo.Top % undo.Window] = ringHead;
            undoTickCounters![undo.Top % undo.Window] = tickCounter;
            undoReplayCursors![undo.Top % undo.Window] = replayCursor;
        }

        internal void EndUndoWindow()
        {
            Undo?.Dispose();
            Undo = null;
            undoRingHeads = null;
            undoTickCounters = null;
            undoReplayCursors = null;
        }

        internal void RollbackTo(int target)
        {
            var undo = Undo;
            if (undo == null)
            {
                throw new InvalidOperationException("sv: no undo window is open");
            }

            undo.UndoFrames(undo.Top, target);
            undo.TruncateTo(target);
            ringHead = undoRingHeads![target % undo.Window];
            tickCounter = undoTickCounters![target % undo.Window];
            replayCursor = undoReplayCursors![target % undo.Window];
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EndUndoWindow();
            Memory.Free(axisBits);
            Memory.Free(buttons);
            Memory.Free(previous);
            Memory.Free(pressed);
            Memory.Free(released);
            Memory.Free(ring);
            Memory.Free(ringTickIds);
            disposed = true;
        }
    }
}
