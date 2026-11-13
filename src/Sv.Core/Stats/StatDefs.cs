using System.Runtime.InteropServices;

namespace Sv
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct StatDef
    {
        public readonly int Lane;
        public readonly int Base;
        public readonly int Scale;

        public StatDef(int lane, int @base, int scale)
        {
            Lane = lane;
            Base = @base;
            Scale = scale;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct StatSource
    {
        public const int NoCurve = -1;

        public readonly int Stat;
        public readonly int From;
        public readonly int Coefficient;
        public readonly int CurveStart;
        public readonly int CurveLength;
        public readonly int CurveMin;
        public readonly int CurveStep;

        public StatSource(int stat, int from, int coefficient, int curveStart, int curveLength, int curveMin, int curveStep)
        {
            Stat = stat;
            From = from;
            Coefficient = coefficient;
            CurveStart = curveStart;
            CurveLength = curveLength;
            CurveMin = curveMin;
            CurveStep = curveStep;
        }

        public static StatSource Linear(int stat, int from, int coefficient) => new StatSource(stat, from, coefficient, NoCurve, 0, 0, 1);

        public static StatSource Curve(int stat, int from, int curveStart, int curveLength, int curveMin, int curveStep) =>
            new StatSource(stat, from, 0, curveStart, curveLength, curveMin, curveStep);
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct CardTerm
    {
        public readonly int Stat;
        public readonly int Card;
        public readonly ShapeKind Kind;
        public readonly int Value;
        public readonly bool PerCount;

        public CardTerm(int stat, int card, ShapeKind kind, int value)
            : this(stat, card, kind, value, false)
        {
        }

        public CardTerm(int stat, int card, ShapeKind kind, int value, bool perCount)
        {
            Stat = stat;
            Card = card;
            Kind = kind;
            Value = value;
            PerCount = perCount;
        }

        public static int TermValue(int value, int count, bool perCount) => perCount ? StatLaw.Saturate((long)value * count) : value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CardRow
    {
        public int Lane;
        public int Value;
        public ShapeKind Kind;
        public bool PerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModifierRecord
    {
        public long Id;
        public long Expiry;
        public int Value;
        public int Source;
        public int Slot;
        public int Next;
        public int Previous;
        public int Heap;
        public ushort Stat;
        public ShapeKind Kind;
    }

    internal enum StatUndoAction : int
    {
        Added = 0,
        Removed = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StatUndoEntry
    {
        public ModifierRecord Record;
        public StatUndoAction Action;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StatUndoMark
    {
        public uint Epoch;
        public int LogStart;
        public long NextId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DerivedTerm
    {
        public int From;
        public int Coefficient;
        public int FromScale;
        public int CurveStart;
        public int CurveLength;
        public int CurveMin;
        public int CurveStep;
    }
}
