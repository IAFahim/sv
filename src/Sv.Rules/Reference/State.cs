using System;
using System.Collections.Generic;

namespace Sv.Rules.Reference
{
    public enum SlotStatus : byte
    {
        Free = 0,
        Live = 1,
        Dying = 2,
    }

    public readonly struct HeldModifier
    {
        public long Handle { get; }

        public int Stat { get; }

        public ShapeKind Shape { get; }

        public int Value { get; }

        public HeldModifier(long handle, int stat, ShapeKind shape, int value)
        {
            Handle = handle;
            Stat = stat;
            Shape = shape;
            Value = value;
        }
    }

    public sealed class RefEntity
    {
        public SlotStatus Status;
        public int Template;
        public long DeathFrame;
        public bool PendingCheck;
        public bool HadTotals;
        public readonly int[] Intrinsics;
        public readonly int[] StatBase;
        public readonly int[] Stats;
        public readonly bool[] Events;
        public readonly List<HeldModifier> Modifiers;

        public RefEntity(int intrinsics, int stats, int events)
        {
            Template = -1;
            Intrinsics = new int[intrinsics];
            StatBase = new int[stats];
            Stats = new int[stats];
            Events = new bool[events];
            Modifiers = new List<HeldModifier>();
        }

        public RefEntity Clone()
        {
            var copy = new RefEntity(Intrinsics.Length, Stats.Length, Events.Length)
            {
                Status = Status,
                Template = Template,
                DeathFrame = DeathFrame,
                PendingCheck = PendingCheck,
                HadTotals = HadTotals,
            };
            Array.Copy(Intrinsics, copy.Intrinsics, Intrinsics.Length);
            Array.Copy(StatBase, copy.StatBase, StatBase.Length);
            Array.Copy(Stats, copy.Stats, Stats.Length);
            Array.Copy(Events, copy.Events, Events.Length);
            copy.Modifiers.AddRange(Modifiers);
            return copy;
        }
    }

    public sealed class RefState
    {
        public PackSchema Schema { get; }

        public long Frame;

        public readonly RefEntity[] Slots;

        public RefState(PackSchema schema, int capacity)
        {
            Schema = schema;
            var size = capacity < 0 ? 0 : capacity > RefRecord.NoSource ? RefRecord.NoSource : capacity;
            Slots = new RefEntity[size];
            for (var i = 0; i < size; i++)
            {
                Slots[i] = Defaults(schema);
            }
        }

        RefState(PackSchema schema, long frame, RefEntity[] slots)
        {
            Schema = schema;
            Frame = frame;
            Slots = slots;
        }

        public int Capacity => Slots.Length;

        public RefState Clone()
        {
            var slots = new RefEntity[Slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = Slots[i].Clone();
            }

            return new RefState(Schema, Frame, slots);
        }

        public static RefEntity Defaults(PackSchema schema)
        {
            var entity = new RefEntity(schema.Intrinsics.Length, schema.Stats.Length, schema.Events.Length);
            ResetToDefaults(schema, entity);
            return entity;
        }

        public static void ResetToDefaults(PackSchema schema, RefEntity entity)
        {
            for (var i = 0; i < schema.Intrinsics.Length; i++)
            {
                entity.Intrinsics[i] = schema.Intrinsics[i].IntDefault;
            }

            for (var i = 0; i < schema.Stats.Length; i++)
            {
                entity.StatBase[i] = schema.Stats[i].IntDefault;
                entity.Stats[i] = schema.Stats[i].IntDefault;
            }

            for (var i = 0; i < schema.Events.Length; i++)
            {
                entity.Events[i] = schema.Events[i].EventDefault;
            }

            entity.Modifiers.Clear();
            entity.Template = -1;
            entity.DeathFrame = 0;
            entity.PendingCheck = false;
            entity.HadTotals = false;
        }

        public bool IsSlot(long slot) => slot >= 0 && slot < Slots.Length;

        public bool IsLive(long slot) => IsSlot(slot) && Slots[slot].Status == SlotStatus.Live;

        public int Intrinsic(int slot, int intrinsic)
        {
            var index = Schema.IndexOf(ValueClass.Intrinsic, intrinsic);
            if (index < 0)
            {
                return 0;
            }

            return IsSlot(slot) && Slots[slot].Status != SlotStatus.Free ? Slots[slot].Intrinsics[index] : Schema.Intrinsics[index].IntDefault;
        }

        public int Stat(int slot, int stat)
        {
            var index = Schema.IndexOf(ValueClass.Stat, stat);
            if (index < 0)
            {
                return 0;
            }

            return IsSlot(slot) && Slots[slot].Status != SlotStatus.Free ? Slots[slot].Stats[index] : Schema.Stats[index].IntDefault;
        }

        public int ScaleOf(ValueClass valueClass, int id)
        {
            var row = Schema.Find(valueClass, id);
            return row == null ? 1 : row.Scale;
        }

        public bool Event(int slot, int raised)
        {
            var index = Schema.IndexOf(ValueClass.Event, raised);
            if (index < 0)
            {
                return false;
            }

            return IsSlot(slot) && Slots[slot].Status != SlotStatus.Free ? Slots[slot].Events[index] : Schema.Events[index].EventDefault;
        }

        public int Intrinsic(int slot, string name)
        {
            var found = Schema.FindByName(ValueClass.Intrinsic, name);
            return found == null ? 0 : Intrinsic(slot, found.Id);
        }
    }
}
