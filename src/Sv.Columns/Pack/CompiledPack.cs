using System;
using System.Collections.Generic;

namespace Sv.Columns
{
    public sealed unsafe class CompiledPack : IDisposable
    {
        readonly Dictionary<string, int> laneIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> channelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> kindIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> eventIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> templateIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> cardIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly int[] cardLanes;
        PackTables* tables;

        internal CompiledPack(ColumnPack source, PackTables* tables, string[] lanes, string[] channels, string[] kinds, string[] events, string[] templates, string[] cards)
        {
            Source = source;
            this.tables = tables;
            LaneNames = lanes;
            ChannelNames = channels;
            KindNames = kinds;
            EventNames = events;
            TemplateNames = templates;
            CardNames = cards;
            Index(laneIndex, lanes);
            Index(channelIndex, channels);
            Index(kindIndex, kinds);
            Index(eventIndex, events);
            Index(templateIndex, templates);
            Index(cardIndex, cards);
            cardLanes = new int[cards.Length];
            for (var c = 0; c < cards.Length; c++)
            {
                cardLanes[c] = laneIndex[source.Cards[c].Lane];
            }
        }

        public ColumnPack Source { get; }

        internal bool IsDisposed => tables == null;

        public PackTables* Tables => tables != null ? tables : throw new ObjectDisposedException(nameof(CompiledPack));

        public IReadOnlyList<string> LaneNames { get; }

        public IReadOnlyList<string> ChannelNames { get; }

        public IReadOnlyList<string> KindNames { get; }

        public IReadOnlyList<string> EventNames { get; }

        public IReadOnlyList<string> TemplateNames { get; }

        public IReadOnlyList<string> CardNames { get; }

        public int Lane(string name) => Find(laneIndex, name, "lane");

        public int Channel(string name) => Find(channelIndex, name, "channel");

        public int Kind(string name) => Find(kindIndex, name, "kind");

        public int Event(string name) => Find(eventIndex, name, "event");

        public int Template(string name) => Find(templateIndex, name, "template");

        public int CardLane(string name) => cardLanes[Find(cardIndex, name, "card")];

        static void Index(Dictionary<string, int> into, string[] names)
        {
            for (var i = 0; i < names.Length; i++)
            {
                into[names[i]] = i;
            }
        }

        static int Find(Dictionary<string, int> map, string name, string what) =>
            map.TryGetValue(name, out var index) ? index : throw new KeyNotFoundException("no " + what + " named '" + name + "' in this pack");

        public void Dispose()
        {
            if (tables == null)
            {
                return;
            }

            for (var t = 0; t < tables->TemplateCount; t++)
            {
                var template = tables->Templates + t;
                Native.Free(template->Defaults);
                Native.Free(template->Preds);
                Native.Free(template->Groups);
                Native.Free(template->Terms);
                Native.Free(template->TimesLanes);
            }

            Native.Free(tables->Templates);
            Native.Free(tables->Atoms);
            Native.Free(tables->Conds);
            Native.Free(tables->Denies);
            Native.Free(tables->Masks);
            Native.Free(tables->Scales);
            Native.Free(tables->Factors);
            Native.Free(tables->Converts);
            Native.Free(tables->Absorbs);
            Native.Free(tables->AbsorbChannels);
            Native.Free(tables->Flows);
            Native.Free(tables->Bounds);
            Native.Free(tables->Written);
            Native.Free(tables->Events);
            Native.Free(tables);
            tables = null;
        }
    }
}
