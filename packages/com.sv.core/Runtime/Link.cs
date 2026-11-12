using System;

namespace Sv
{
    public enum LinkMode : byte
    {
        Read = 0,
        Check = 1,
        Accumulate = 2,
        Delta = 3,
        Modifier = 4,
        Event = 5,
    }

    public enum LinkScope : byte
    {
        Self = 0,
        Remote = 1,
    }

    public enum ColumnRole : byte
    {
        StatOutput = 0,
        Intrinsic = 1,
        Delta = 2,
        Inbox = 3,
        Effect = 4,
        Signal = 5,
        Condition = 6,
        Sense = 7,
    }

    public enum LinkFault : byte
    {
        None = 0,
        UnknownColumn = 1,
        ReadOfAccumulate = 2,
        WriteOfStore = 3,
        RemoteFloat = 4,
        SecondConsumer = 5,
        RemoteWithoutInbox = 6,
    }

    public readonly struct ColumnDef
    {
        public readonly ulong Name;
        public readonly ushort Schema;
        public readonly ushort Consumer;
        public readonly ColumnRole Role;

        public ColumnDef(ulong name, ColumnRole role, ushort schema, ushort consumer)
        {
            Name = name;
            Role = role;
            Schema = schema;
            Consumer = consumer;
        }
    }

    public readonly struct Link
    {
        public readonly ushort Kind;
        public readonly ushort Column;
        public readonly LinkMode Mode;
        public readonly LinkScope Scope;

        public Link(ushort kind, ushort column, LinkMode mode, LinkScope scope = LinkScope.Self)
        {
            Kind = kind;
            Column = column;
            Mode = mode;
            Scope = scope;
        }
    }

    public readonly struct StatWrite
    {
        public readonly int Slot;
        public readonly uint Bits;
        public readonly ushort Stat;

        public StatWrite(int slot, ushort stat, uint bits)
        {
            Slot = slot;
            Stat = stat;
            Bits = bits;
        }
    }

    public static class Links
    {
        public static bool IsAccumulate(ColumnRole role) => role == ColumnRole.Delta || role == ColumnRole.Inbox || role == ColumnRole.Effect || role == ColumnRole.Signal;

        public static bool IsStore(ColumnRole role) => !IsAccumulate(role);

        public static LinkFault Validate(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (!IsAccumulate(columns[i].Role))
                {
                    continue;
                }

                for (var j = i + 1; j < columns.Length; j++)
                {
                    if (columns[j].Name == columns[i].Name && IsAccumulate(columns[j].Role))
                    {
                        return LinkFault.SecondConsumer;
                    }

                    if (columns[i].Role == ColumnRole.Inbox && columns[j].Role == ColumnRole.Inbox && columns[i].Consumer == columns[j].Consumer)
                    {
                        return LinkFault.SecondConsumer;
                    }
                }
            }

            for (var i = 0; i < links.Length; i++)
            {
                var link = links[i];
                if (link.Column >= columns.Length || (byte)link.Mode > (byte)LinkMode.Event || (byte)link.Scope > (byte)LinkScope.Remote)
                {
                    return LinkFault.UnknownColumn;
                }

                var column = columns[link.Column];
                var fault = Check(link, column, columns);
                if (fault != LinkFault.None)
                {
                    return fault;
                }
            }

            return LinkFault.None;
        }

        static LinkFault Check(in Link link, in ColumnDef column, ReadOnlySpan<ColumnDef> columns)
        {
            switch (link.Mode)
            {
                case LinkMode.Read:
                case LinkMode.Check:
                    return IsAccumulate(column.Role) ? LinkFault.ReadOfAccumulate : LinkFault.None;
                case LinkMode.Modifier:
                    return column.Role == ColumnRole.StatOutput ? LinkFault.None : LinkFault.WriteOfStore;
                case LinkMode.Event:
                    if (column.Role != ColumnRole.Signal && column.Role != ColumnRole.Condition)
                    {
                        return LinkFault.WriteOfStore;
                    }

                    return link.Scope == LinkScope.Remote ? LinkFault.RemoteFloat : LinkFault.None;
                default:
                    if (IsStore(column.Role))
                    {
                        return LinkFault.WriteOfStore;
                    }

                    if (link.Scope == LinkScope.Self)
                    {
                        return LinkFault.None;
                    }

                    if (column.Role == ColumnRole.Inbox)
                    {
                        return LinkFault.None;
                    }

                    if (column.Role != ColumnRole.Delta)
                    {
                        return LinkFault.RemoteFloat;
                    }

                    for (var i = 0; i < columns.Length; i++)
                    {
                        if (columns[i].Role == ColumnRole.Inbox && columns[i].Consumer == column.Consumer)
                        {
                            return LinkFault.None;
                        }
                    }

                    return LinkFault.RemoteWithoutInbox;
            }
        }

        public static ulong Hash(ReadOnlySpan<Link> links, ReadOnlySpan<ColumnDef> columns)
        {
            var hash = 0x6C696E6B73763231ul;
            hash = Mix(hash, (ulong)columns.Length);
            for (var i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                hash = Mix(hash, c.Name);
                hash = Mix(hash, ((ulong)c.Schema << 24) | ((ulong)c.Consumer << 8) | (byte)c.Role);
            }

            hash = Mix(hash, (ulong)links.Length);
            for (var i = 0; i < links.Length; i++)
            {
                var l = links[i];
                hash = Mix(hash, ((ulong)l.Kind << 32) | ((ulong)l.Column << 16) | ((ulong)(byte)l.Mode << 8) | (byte)l.Scope);
            }

            return hash;
        }

        static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                var key = hash ^ (value * 0x9E3779B97F4A7C15ul);
                key ^= key >> 33;
                key *= 0xFF51AFD7ED558CCDul;
                key ^= key >> 33;
                key *= 0xC4CEB9FE1A85EC53ul;
                return key ^ (key >> 33);
            }
        }
    }
}
