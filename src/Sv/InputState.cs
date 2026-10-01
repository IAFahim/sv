using System;
using System.Runtime.InteropServices;

namespace Sv;

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

    InputState(int axes, int buttonWords, int ringTicks)
    {
        Axes = axes;
        ButtonWords = buttonWords;
        RingTicks = ringTicks;
        axisBits = (uint*)NativeMemory.AllocZeroed((nuint)axes * sizeof(uint), sizeof(uint));
        buttons = (ulong*)NativeMemory.AllocZeroed((nuint)buttonWords * sizeof(ulong), sizeof(ulong));
        previous = (ulong*)NativeMemory.AllocZeroed((nuint)buttonWords * sizeof(ulong), sizeof(ulong));
        pressed = (ulong*)NativeMemory.AllocZeroed((nuint)buttonWords * sizeof(ulong), sizeof(ulong));
        released = (ulong*)NativeMemory.AllocZeroed((nuint)buttonWords * sizeof(ulong), sizeof(ulong));
        ringStride = axes * sizeof(uint) + buttonWords * sizeof(ulong);
        ring = (byte*)NativeMemory.AllocZeroed((nuint)ringTicks * (nuint)ringStride, 16);
        ringTickIds = (long*)NativeMemory.Alloc((nuint)ringTicks * sizeof(long), sizeof(long));
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

    public void Capture(uint* axesBits, ulong* buttonWords)
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

    public void BeginReplay(long fromTick)
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
        new Span<ulong>(previous, ButtonWords).Fill(0);
        new Span<ulong>(buttons, ButtonWords).Fill(0);
        replayCursor = slot;
    }

    public bool ReplayStep()
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
        return BitConverter.UInt32BitsToSingle(axisBits[axis]);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        NativeMemory.Free(axisBits);
        NativeMemory.Free(buttons);
        NativeMemory.Free(previous);
        NativeMemory.Free(pressed);
        NativeMemory.Free(released);
        NativeMemory.Free(ring);
        NativeMemory.Free(ringTickIds);
        disposed = true;
    }
}
