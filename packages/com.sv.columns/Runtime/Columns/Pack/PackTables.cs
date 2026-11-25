using System.Runtime.InteropServices;

namespace Sv.Columns
{
    public static class AtomOp
    {
        public const byte Gt = 0;
        public const byte Ge = 1;
        public const byte Lt = 2;
        public const byte Le = 3;
        public const byte Eq = 4;
        public const byte Ne = 5;
        public const byte Below = 6;
        public const byte Bit = 7;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Atom
    {
        public int Op;
        public int Negated;
        public int Lane;
        public int Other;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CondRange
    {
        public int First;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DenyEntry
    {
        public int Cond;
        public ulong Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MaskEntry
    {
        public int Lane;
        public int ByKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScaleEntry
    {
        public int Cond;
        public int SourceCond;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ConvertEntry
    {
        public int Cond;
        public int From;
        public int To;
        public int Numerator;
        public int Shift;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AbsorbEntry
    {
        public int Budget;
        public int First;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FlowTerm
    {
        public int Lane;
        public int Channel;
        public int Sign;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoundsEntry
    {
        public int Lane;
        public int HasFloor;
        public int FloorLane;
        public int Floor;
        public int HasCeiling;
        public int CeilingLane;
        public int Ceiling;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEntry
    {
        public int Enter;
        public int Stay;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpGroup
    {
        public int Channel;
        public int AdmitShift;
        public int TermFirst;
        public int TermCount;
        public long ConstGain;
        public long ConstLoss;
        public int CondConstEnd;
        public int LaneEnd;
        public long CondBound;
        public long LaneAmount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpTerm
    {
        public int Pred;
        public int Lane;
        public long Amount;
        public long Gain;
        public long Loss;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TemplateTable
    {
        public int* Defaults;
        public int* Preds;
        public int PredCount;
        public OpGroup* Groups;
        public int GroupCount;
        public OpTerm* Terms;
        public int TermCount;
        public int* TimesLanes;
        public int TimesLaneCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PackTables
    {
        public int LaneCount;
        public int ChannelCount;
        public int KindCount;
        public int ClassCount;
        public int EventCount;
        public int EventsLane;
        public int EnteredLane;
        public int KindLane;
        public ulong AllAdmit;
        public Atom* Atoms;
        public CondRange* Conds;
        public int CondCount;
        public DenyEntry* Denies;
        public int DenyCount;
        public MaskEntry* Masks;
        public int MaskCount;
        public ScaleEntry* Scales;
        public int ScaleCount;
        public uint NoSourceFlags;
        public long* Factors;
        public ConvertEntry* Converts;
        public int ConvertCount;
        public AbsorbEntry* Absorbs;
        public int AbsorbCount;
        public int* AbsorbChannels;
        public FlowTerm* Flows;
        public int FlowCount;
        public BoundsEntry* Bounds;
        public int BoundsCount;
        public int* Written;
        public int WrittenCount;
        public EventEntry* Events;
        public TemplateTable* Templates;
        public int TemplateCount;
    }
}
