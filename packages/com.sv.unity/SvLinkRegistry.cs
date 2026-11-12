using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity
{
    public struct SvBound
    {
        public ComponentType Stat;
        public int Constant;
        public bool FromStat;

        public static SvBound Of(int constant) => new SvBound { Constant = constant };

        public static SvBound OfStat<T>() where T : unmanaged, IComponentData => new SvBound { Stat = ComponentType.ReadOnly<T>(), FromStat = true };
    }

    public struct SvConditionSpec
    {
        public ComponentType Gate;
        public bool HasGate;
        public int EnterNumerator;
        public int EnterDenominator;
        public int StayNumerator;
        public int StayDenominator;
        public int Capacity;
    }

    public sealed class SvIntrinsicSpec
    {
        public ushort Column;
        public ushort DeltaColumn;
        public ComponentType Value;
        public ComponentType Delta;
        public SvBound Floor;
        public SvBound Ceiling;
        public bool Remote;
        public bool HasCondition;
        public SvConditionSpec Condition;
    }

    public sealed class SvStatSpec
    {
        public ushort Column;
        public StatId Stat;
        public ComponentType Type;
    }

    public sealed class SvLinkRegistry
    {
        public const int MaxIntrinsics = 4;
        public const int MaxStats = 8;
        public const int MaxStatIndex = 64;

        readonly List<ColumnDef> columns = new List<ColumnDef>();
        readonly List<Link> links = new List<Link>();
        readonly Dictionary<TypeIndex, ushort> columnOf = new Dictionary<TypeIndex, ushort>();
        readonly List<SvIntrinsicSpec> intrinsics = new List<SvIntrinsicSpec>();
        readonly List<SvStatSpec> stats = new List<SvStatSpec>();

        public IReadOnlyList<SvIntrinsicSpec> Intrinsics => intrinsics;

        public IReadOnlyList<SvStatSpec> Stats => stats;

        public ReadOnlySpan<ColumnDef> Columns => columns.ToArray();

        public ReadOnlySpan<Link> Links => links.ToArray();

        public ushort Stat<T>(StatId stat) where T : unmanaged, IComponentData
        {
            Require4<T>();
            if (stats.Count == MaxStats)
            {
                throw new InvalidOperationException($"sv.unity: more than {MaxStats} stat columns");
            }

            if (stat.Index >= MaxStatIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(stat), $"sv.unity: stat index {stat.Index} must be below {MaxStatIndex}");
            }

            var column = Add(ComponentType.ReadWrite<T>(), ColumnRole.StatOutput, (ushort)stat.Index, 0);
            stats.Add(new SvStatSpec { Column = column, Stat = stat, Type = ComponentType.ReadWrite<T>() });
            return column;
        }

        public int Intrinsic<TValue, TDelta>(IntrinsicId intrinsicId, SvBound floor, SvBound ceiling, bool remote = false)
            where TValue : unmanaged, IComponentData
            where TDelta : unmanaged, IComponentData
        {
            Require4<TValue>();
            Require4<TDelta>();
            if (intrinsics.Count == MaxIntrinsics)
            {
                throw new InvalidOperationException($"sv.unity: more than {MaxIntrinsics} dense intrinsics");
            }

            var value = Add(ComponentType.ReadWrite<TValue>(), ColumnRole.Intrinsic, (ushort)intrinsicId.Index, 0);
            var delta = Add(ComponentType.ReadWrite<TDelta>(), ColumnRole.Delta, (ushort)intrinsicId.Index, value);
            if (remote)
            {
                columns.Add(new ColumnDef(NameOf(typeof(TDelta)) ^ 0x1B0Bul, ColumnRole.Inbox, (ushort)intrinsicId.Index, value));
            }

            intrinsics.Add(new SvIntrinsicSpec { Column = value, DeltaColumn = delta, Value = ComponentType.ReadWrite<TValue>(), Delta = ComponentType.ReadWrite<TDelta>(), Floor = floor, Ceiling = ceiling, Remote = remote });
            return intrinsics.Count - 1;
        }

        public ushort Condition<TGate>(int intrinsic, int enterNumerator, int enterDenominator, int stayNumerator, int stayDenominator, int capacity = 65536)
            where TGate : unmanaged, IComponentData, IEnableableComponent
        {
            var spec = intrinsics[intrinsic];
            if (enterDenominator <= 0 || stayDenominator <= 0 || capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(enterDenominator), "sv.unity: condition fractions need positive denominators and a capacity >= 0");
            }

            spec.HasCondition = true;
            spec.Condition = new SvConditionSpec { Gate = ComponentType.ReadWrite<TGate>(), HasGate = true, EnterNumerator = enterNumerator, EnterDenominator = enterDenominator, StayNumerator = stayNumerator, StayDenominator = stayDenominator, Capacity = capacity };
            return Add(ComponentType.ReadWrite<TGate>(), ColumnRole.Condition, spec.Column, spec.Column);
        }

        public void Link<TColumn>(ushort kind, LinkMode mode, LinkScope scope = LinkScope.Self) where TColumn : unmanaged, IComponentData
        {
            if (!columnOf.TryGetValue(ComponentType.ReadWrite<TColumn>().TypeIndex, out var column))
            {
                throw new ArgumentException($"sv.unity: {typeof(TColumn).FullName} is not a registered sv column");
            }

            links.Add(new Link(kind, column, mode, scope));
        }

        public LinkFault Validate() => Sv.Links.Validate(links.ToArray(), columns.ToArray());

        public ulong Hash() => Sv.Links.Hash(links.ToArray(), columns.ToArray());

        public bool TryColumn(ComponentType type, out ushort column) => columnOf.TryGetValue(type.TypeIndex, out column);

        ushort Add(ComponentType type, ColumnRole role, ushort schema, ushort consumer)
        {
            if (columnOf.ContainsKey(type.TypeIndex))
            {
                throw new ArgumentException($"sv.unity: {TypeManager.GetType(type.TypeIndex).FullName} is registered twice");
            }

            var column = (ushort)columns.Count;
            columns.Add(new ColumnDef(NameOf(TypeManager.GetType(type.TypeIndex)), role, schema, consumer));
            columnOf.Add(type.TypeIndex, column);
            return column;
        }

        static void Require4<T>() where T : unmanaged
        {
            if (UnsafeUtility.SizeOf<T>() != 4)
            {
                throw new ArgumentException($"sv.unity: column {typeof(T).FullName} must be one 4-byte field");
            }
        }

        static ulong NameOf(Type type)
        {
            var name = type.FullName ?? type.Name;
            var hash = 0xCBF29CE484222325ul;
            for (var i = 0; i < name.Length; i++)
            {
                hash = (hash ^ name[i]) * 0x100000001B3ul;
            }

            return hash;
        }
    }
}
