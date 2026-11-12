using System;
using System.Globalization;

namespace Sv.Rules
{
    public readonly struct PackSource
    {
        public const string ValuesFile = "values.sv.json";
        public const string RulesFile = "rules.sv.json";
        public const string CardsFile = "cards.sv.json";
        public const string TemplatesFile = "templates.sv.json";

        public string Values { get; }

        public string Rules { get; }

        public string Cards { get; }

        public string Templates { get; }

        public PackSource(string values, string rules, string cards, string templates)
        {
            Values = values;
            Rules = rules;
            Cards = cards;
            Templates = templates;
        }
    }

    public static class PackReader
    {
        public const int SchemaVersion = 1;

        public static Checked<ContentPack> Read(in PackSource source)
        {
            var session = new PackReadSession();
            var pack = session.Read(source);
            var diagnostics = session.Diagnostics;
            return new Checked<ContentPack>(diagnostics.Errors == 0 ? pack : null, diagnostics.ToArray());
        }
    }

    internal enum NameHint : byte
    {
        None = 0,
        Kind = 1,
        Channel = 2,
        Origin = 3,
    }

    internal sealed class PackReadSession
    {
        public readonly DiagnosticList Diagnostics = new DiagnosticList();

        string file = string.Empty;
        int ruleId = -1;
        int nextLane;
        ValueDef[] stats = new ValueDef[0];
        ValueDef[] intrinsics = new ValueDef[0];
        ValueDef[] events = new ValueDef[0];
        ValueDef[] channels = new ValueDef[0];
        ValueDef[] kinds = new ValueDef[0];
        CardDef[] cards = new CardDef[0];
        readonly Rows<string> tags = new Rows<string>();
        readonly Rows<int> chainIds = new Rows<int>();
        readonly Rows<string> chainNames = new Rows<string>();

        void Error(DiagnosticCode code, JsonValue? at, string message) =>
            Diagnostics.Error(code, file, at == null ? 0 : at.Offset, ruleId, -1, message);

        void Warning(DiagnosticCode code, JsonValue? at, string message) =>
            Diagnostics.Warning(code, file, at == null ? 0 : at.Offset, ruleId, -1, message);

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        public ContentPack? Read(in PackSource source)
        {
            file = PackSource.ValuesFile;
            var valuesDoc = Document(source.Values, true, out var counter);
            nextLane = counter;
            ReadValues(valuesDoc);
            file = PackSource.CardsFile;
            ReadCards(Document(source.Cards, false, out _));
            file = PackSource.TemplatesFile;
            var templates = ReadTemplates(Document(source.Templates, false, out _));
            file = PackSource.RulesFile;
            var ruleRows = Document(source.Rules, false, out _);
            ScanChains(ruleRows);
            var schema = new PackSchema(nextLane, stats, intrinsics, events, channels, kinds, tags.ToArray(), cards, templates);
            return ReadRules(ruleRows, schema);
        }

        JsonValue[] Document(string text, bool needsCounter, out int counter)
        {
            counter = 0;
            if (string.IsNullOrEmpty(text))
            {
                if (needsCounter)
                {
                    Error(DiagnosticCode.MissingKey, null, "the values document is empty");
                }

                return new JsonValue[0];
            }

            var parse = JsonReader.Parse(text);
            if (!parse.Ok || parse.Value == null)
            {
                Diagnostics.Error(DiagnosticCode.JsonSyntax, file, parse.ErrorOffset, -1, -1, parse.Error);
                return new JsonValue[0];
            }

            var root = parse.Value;
            if (root.Kind == JsonKind.Array && !needsCounter)
            {
                return root.Items;
            }

            if (root.Kind != JsonKind.Object)
            {
                Error(DiagnosticCode.WrongType, root, "a pack document is an object with \"schema\" and \"rows\"");
                return new JsonValue[0];
            }

            CheckKeys(root, needsCounter ? new[] { "schema", "nextLane", "rows" } : new[] { "schema", "rows" });
            var schema = root.Find("schema");
            if (schema == null || !schema.TryInt(out var version) || version != PackReader.SchemaVersion)
            {
                Error(DiagnosticCode.SchemaVersion, schema ?? root, "\"schema\" must be " + Text(PackReader.SchemaVersion));
            }

            if (needsCounter)
            {
                var lane = root.Find("nextLane");
                if (lane == null || !lane.TryInt(out counter) || counter < 0 || counter > LaneNumbers.MaxLanes)
                {
                    Error(DiagnosticCode.LaneCounter, lane ?? root, "\"nextLane\" must be an integer in 0.." + Text(LaneNumbers.MaxLanes));
                    counter = 0;
                }
            }

            var rows = root.Find("rows");
            if (rows == null || rows.Kind != JsonKind.Array)
            {
                Error(DiagnosticCode.MissingKey, root, "\"rows\" must be an array");
                return new JsonValue[0];
            }

            return rows.Items;
        }

        void CheckKeys(JsonValue row, string[] allowed)
        {
            for (var i = 0; i < row.Keys.Length; i++)
            {
                var known = false;
                for (var j = 0; j < allowed.Length; j++)
                {
                    known |= string.CompareOrdinal(row.Keys[i], allowed[j]) == 0;
                }

                if (!known)
                {
                    Error(DiagnosticCode.UnknownKey, row.Items[i], "unknown key \"" + row.Keys[i] + "\"");
                }
            }
        }

        bool RequireInt(JsonValue row, string key, out int value)
        {
            var found = row.Find(key);
            if (found != null && found.TryInt(out value))
            {
                return true;
            }

            value = 0;
            Error(found == null ? DiagnosticCode.MissingKey : DiagnosticCode.WrongType, found ?? row, "\"" + key + "\" must be an integer");
            return false;
        }

        int OptionalInt(JsonValue row, string key, int fallback)
        {
            var found = row.Find(key);
            if (found == null)
            {
                return fallback;
            }

            if (found.TryInt(out var value))
            {
                return value;
            }

            Error(DiagnosticCode.WrongType, found, "\"" + key + "\" must be an integer");
            return fallback;
        }

        bool OptionalBool(JsonValue row, string key, bool fallback)
        {
            var found = row.Find(key);
            if (found == null)
            {
                return fallback;
            }

            if (found.IsBool)
            {
                return found.Kind == JsonKind.True;
            }

            Error(DiagnosticCode.WrongType, found, "\"" + key + "\" must be true or false");
            return fallback;
        }

        string RequireString(JsonValue row, string key)
        {
            var found = row.Find(key);
            if (found != null && found.Kind == JsonKind.String && found.Text.Length > 0)
            {
                return found.Text;
            }

            Error(found == null ? DiagnosticCode.MissingKey : DiagnosticCode.WrongType, found ?? row, "\"" + key + "\" must be a non-empty string");
            return string.Empty;
        }

        float RequireFloat(JsonValue value, string what)
        {
            if (value.TryFloat(out var real))
            {
                return real;
            }

            Error(DiagnosticCode.WrongType, value, what + " must be a finite number");
            return 0f;
        }

        int RequireScaled(JsonValue value, int scale, string what)
        {
            if (value.TryScaled(scale, out var scaled))
            {
                return scaled;
            }

            Error(DiagnosticCode.NotExact, value, what + " must be a decimal that is exact in steps of 1/" + Text(scale) + " and fits an int at that scale");
            return 0;
        }

        int OptionalScale(JsonValue row)
        {
            var found = row.Find("scale");
            if (found == null)
            {
                return StatLaw.MinScale;
            }

            if (found.TryInt(out var scale) && StatLaw.IsScale(scale))
            {
                return scale;
            }

            Error(DiagnosticCode.BadScale, found, "a scale is 1, 10, 100, 1000, 10000, 100000 or 1000000");
            return StatLaw.MinScale;
        }

        void ReadValues(JsonValue[] rows)
        {
            var statRows = new Rows<ValueDef>();
            var intrinsicRows = new Rows<ValueDef>();
            var eventRows = new Rows<ValueDef>();
            var channelRows = new Rows<ValueDef>();
            var kindRows = new Rows<ValueDef>();
            var intoNames = new Rows<string>();
            var intoRows = new Rows<JsonValue>();
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row.Kind != JsonKind.Object)
                {
                    Error(DiagnosticCode.WrongType, row, "a value row is an object");
                    continue;
                }

                CheckKeys(row, new[] { "id", "lane", "word", "name", "class", "default", "scale", "role", "timeline", "into" });
                var name = RequireString(row, "name");
                var className = RequireString(row, "class");
                switch (className)
                {
                    case "stat":
                    {
                        var id = LaneId(row);
                        var scale = OptionalScale(row);
                        var fallback = row.Find("default");
                        var value = fallback == null ? 0 : RequireScaled(fallback, scale, "a stat default");
                        statRows.Add(new ValueDef(id, name, ValueClass.Stat, IntrinsicRole.Plain, value, scale, false, false, -1, row.Offset));
                        break;
                    }

                    case "intrinsic":
                    {
                        var id = LaneId(row);
                        var scale = OptionalScale(row);
                        var fallback = row.Find("default");
                        var value = fallback == null ? 0 : RequireScaled(fallback, scale, "an intrinsic default");
                        var role = IntrinsicRole.Plain;
                        var roleRow = row.Find("role");
                        if (roleRow != null)
                        {
                            switch (roleRow.Text)
                            {
                                case "plain": role = IntrinsicRole.Plain; break;
                                case "kind": role = IntrinsicRole.Kind; break;
                                case "owner": role = IntrinsicRole.Owner; break;
                                case "reference": role = IntrinsicRole.Reference; break;
                                default: Error(DiagnosticCode.BadValue, roleRow, "role is plain, kind, owner or reference"); break;
                            }
                        }

                        intrinsicRows.Add(new ValueDef(id, name, ValueClass.Intrinsic, role, value, scale, false, false, -1, row.Offset));
                        break;
                    }

                    case "event":
                    {
                        int id;
                        if (row.Find("lane") != null)
                        {
                            RequireInt(row, "lane", out var lane);
                            var word = OptionalInt(row, "word", 0);
                            if (word < 0 || word >= LaneNumbers.EventWords)
                            {
                                Error(DiagnosticCode.IdOutOfRange, row, "an event word is 0..31");
                            }

                            id = LaneNumbers.EventId(lane, word);
                            if (lane < 0 || lane >= nextLane)
                            {
                                Error(DiagnosticCode.IdOutOfRange, row, "event lane " + Text(lane) + " is not below nextLane " + Text(nextLane));
                            }
                        }
                        else
                        {
                            RequireInt(row, "id", out id);
                            if (id < 0 || LaneNumbers.LaneOfEvent(id) >= nextLane)
                            {
                                Error(DiagnosticCode.IdOutOfRange, row, "event id " + Text(id) + " is not below nextLane * 32");
                            }
                        }

                        eventRows.Add(new ValueDef(id, name, ValueClass.Event, IntrinsicRole.Plain, 0, 1, OptionalBool(row, "default", false), false, -1, row.Offset));
                        break;
                    }

                    case "channel":
                    {
                        RequireInt(row, "id", out var id);
                        if (id < 0 || id >= LaneNumbers.MaxChannels)
                        {
                            Error(DiagnosticCode.TooManyChannels, row, "a channel id is 0..255");
                        }

                        var intoRow = row.Find("into");
                        if (intoRow != null)
                        {
                            intoNames.Add(intoRow.Text);
                            intoRows.Add(intoRow);
                        }

                        channelRows.Add(new ValueDef(id, name, ValueClass.Channel, IntrinsicRole.Plain, 0, 1, false, OptionalBool(row, "timeline", false), intoRow == null ? -1 : -2 - (intoNames.Count - 1), row.Offset));
                        break;
                    }

                    case "kind":
                    {
                        RequireInt(row, "id", out var id);
                        if (id < 0 || id >= LaneNumbers.MaxKinds)
                        {
                            Error(DiagnosticCode.TooManyKinds, row, "a kind id is 0..255");
                        }

                        kindRows.Add(new ValueDef(id, name, ValueClass.Kind, IntrinsicRole.Plain, 0, 1, false, false, -1, row.Offset));
                        break;
                    }

                    default:
                        Error(DiagnosticCode.BadValue, row.Find("class"), "class is stat, intrinsic, event, channel or kind");
                        break;
                }
            }

            stats = Sorted(statRows);
            intrinsics = Sorted(intrinsicRows);
            events = Sorted(eventRows);
            kinds = Sorted(kindRows);
            var channelArray = Sorted(channelRows);
            for (var i = 0; i < channelArray.Length; i++)
            {
                var c = channelArray[i];
                if (c.Into <= -2)
                {
                    var index = -2 - c.Into;
                    var target = FindName(intrinsics, intoNames[index]);
                    if (target == null)
                    {
                        Error(DiagnosticCode.UnknownName, intoRows[index], "\"into\" names no intrinsic: " + intoNames[index]);
                    }

                    channelArray[i] = new ValueDef(c.Id, c.Name, c.Class, c.Role, 0, 1, false, c.Timeline, target == null ? -1 : target.Id, c.Offset);
                }
            }

            channels = channelArray;
            CheckValues();
        }

        int LaneId(JsonValue row)
        {
            RequireInt(row, "id", out var id);
            if (id < 0 || id >= nextLane)
            {
                Error(DiagnosticCode.IdOutOfRange, row, "lane " + Text(id) + " is not below nextLane " + Text(nextLane));
            }

            return id;
        }

        static ValueDef[] Sorted(Rows<ValueDef> rows)
        {
            var array = rows.ToArray();
            var ids = new int[array.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = array[i].Id;
            }

            Array.Sort(ids, array);
            return array;
        }

        static ValueDef? FindName(ValueDef[] rows, string name)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                if (string.CompareOrdinal(rows[i].Name, name) == 0)
                {
                    return rows[i];
                }
            }

            return null;
        }

        void ValueError(DiagnosticCode code, ValueDef row, string message) =>
            Diagnostics.Error(code, file, row.Offset, -1, -1, message);

        void CheckValues()
        {
            var laneOwner = new string?[nextLane];
            CheckLanes(stats, laneOwner);
            CheckLanes(intrinsics, laneOwner);
            for (var i = 0; i < events.Length; i++)
            {
                var lane = events[i].Lane;
                if (lane >= 0 && lane < nextLane && laneOwner[lane] != null && laneOwner[lane] != "event")
                {
                    ValueError(DiagnosticCode.LaneCollision, events[i], "event " + events[i].Name + " uses lane " + Text(lane) + " which belongs to " + laneOwner[lane]);
                }

                if (lane >= 0 && lane < nextLane && laneOwner[lane] == null)
                {
                    laneOwner[lane] = "event";
                }

                if (i > 0 && events[i - 1].Id == events[i].Id)
                {
                    ValueError(DiagnosticCode.DuplicateId, events[i], "events " + events[i - 1].Name + " and " + events[i].Name + " share id " + Text(events[i].Id));
                }
            }

            CheckDuplicateIds(channels, "channels");
            CheckDuplicateIds(kinds, "kinds");
            var named = new Rows<ValueDef>();
            AddAll(named, stats);
            AddAll(named, intrinsics);
            AddAll(named, events);
            CheckDuplicateNames(named.ToArray());
            CheckDuplicateNames(channels);
            CheckDuplicateNames(kinds);
            var kindRole = 0;
            var ownerRole = 0;
            for (var i = 0; i < intrinsics.Length; i++)
            {
                kindRole += intrinsics[i].Role == IntrinsicRole.Kind ? 1 : 0;
                ownerRole += intrinsics[i].Role == IntrinsicRole.Owner ? 1 : 0;
            }

            if (kindRole > 1 || ownerRole > 1)
            {
                Diagnostics.Error(DiagnosticCode.RoleConflict, file, 0, -1, -1, "at most one intrinsic has role kind and at most one has role owner");
            }

            if (channels.Length > 0 && channels[channels.Length - 1].Id >= LaneNumbers.ChannelWord)
            {
                Diagnostics.Warning(DiagnosticCode.ChannelsAboveWord, file, channels[channels.Length - 1].Offset, -1, -1,
                    "channel ids reach " + Text(channels[channels.Length - 1].Id) + ": channel sets cannot be one 64-bit word; the emitter must dispatch by switch, never by shift");
            }
        }

        static void AddAll(Rows<ValueDef> rows, ValueDef[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                rows.Add(values[i]);
            }
        }

        void CheckLanes(ValueDef[] rows, string?[] laneOwner)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var lane = rows[i].Id;
                if (lane < 0 || lane >= laneOwner.Length)
                {
                    continue;
                }

                if (laneOwner[lane] != null)
                {
                    ValueError(DiagnosticCode.LaneCollision, rows[i], rows[i].Name + " uses lane " + Text(lane) + " which belongs to " + laneOwner[lane]);
                }
                else
                {
                    laneOwner[lane] = rows[i].Name;
                }
            }
        }

        void CheckDuplicateIds(ValueDef[] rows, string what)
        {
            for (var i = 1; i < rows.Length; i++)
            {
                if (rows[i - 1].Id == rows[i].Id)
                {
                    ValueError(DiagnosticCode.DuplicateId, rows[i], what + " " + rows[i - 1].Name + " and " + rows[i].Name + " share id " + Text(rows[i].Id));
                }
            }
        }

        void CheckDuplicateNames(ValueDef[] rows)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                for (var j = i + 1; j < rows.Length; j++)
                {
                    if (string.CompareOrdinal(rows[i].Name, rows[j].Name) == 0)
                    {
                        ValueError(DiagnosticCode.DuplicateName, rows[j], "the name " + rows[j].Name + " is used twice");
                    }
                }
            }
        }

        void ReadCards(JsonValue[] rows)
        {
            var read = new Rows<CardDef>();
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row.Kind != JsonKind.Object)
                {
                    Error(DiagnosticCode.WrongType, row, "a card row is an object");
                    continue;
                }

                CheckKeys(row, new[] { "id", "name", "value", "modifiers" });
                RequireInt(row, "id", out var id);
                var name = RequireString(row, "name");
                var valueName = RequireString(row, "value");
                var intrinsic = FindName(intrinsics, valueName);
                if (intrinsic == null)
                {
                    Error(DiagnosticCode.UnknownName, row.Find("value"), "card " + name + " counts in no intrinsic named " + valueName);
                }

                var modifiers = new Rows<StatModifier>();
                var list = row.Find("modifiers");
                if (list != null)
                {
                    if (list.Kind != JsonKind.Array)
                    {
                        Error(DiagnosticCode.WrongType, list, "\"modifiers\" is an array");
                    }
                    else
                    {
                        for (var m = 0; m < list.Items.Length; m++)
                        {
                            var item = list.Items[m];
                            CheckKeys(item, new[] { "stat", "added", "additive", "multi", "cap" });
                            var stat = FindName(stats, item.Find("stat")?.Text ?? string.Empty);
                            if (stat == null)
                            {
                                Error(DiagnosticCode.UnknownName, item, "a card modifier names no stat");
                                continue;
                            }

                            if (ShapeOf(item, stat.Scale, out var kind, out var amount))
                            {
                                modifiers.Add(new StatModifier(stat.Id, kind, amount));
                            }
                            else
                            {
                                Error(DiagnosticCode.MissingKey, item, "a modifier has exactly one of added, additive, multi, cap");
                            }
                        }
                    }
                }

                read.Add(new CardDef(id, name, intrinsic == null ? -1 : intrinsic.Id, modifiers.ToArray(), row.Offset));
            }

            var array = read.ToArray();
            var ids = new int[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                ids[i] = array[i].Id;
            }

            Array.Sort(ids, array);
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i - 1].Id == array[i].Id)
                {
                    Diagnostics.Error(DiagnosticCode.DuplicateId, file, array[i].Offset, -1, -1, "cards " + array[i - 1].Name + " and " + array[i].Name + " share id " + Text(array[i].Id));
                }
            }

            cards = array;
        }

        bool ShapeOf(JsonValue item, int scale, out ShapeKind kind, out int amount)
        {
            kind = ShapeKind.Added;
            amount = 0;
            var found = 0;
            var names = new[] { "added", "additive", "multi", "cap" };
            for (var k = 0; k < names.Length; k++)
            {
                var value = item.Find(names[k]);
                if (value != null)
                {
                    kind = (ShapeKind)k;
                    amount = ShapeAmount(value, kind, scale, names[k]);
                    found++;
                }
            }

            return found == 1;
        }

        int ShapeAmount(JsonValue value, ShapeKind kind, int scale, string what) =>
            RequireScaled(value, kind == ShapeKind.Additive || kind == ShapeKind.Multi ? StatLaw.Whole : scale, what);

        TemplateDef[] ReadTemplates(JsonValue[] rows)
        {
            var read = new Rows<TemplateDef>();
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row.Kind != JsonKind.Object)
                {
                    Error(DiagnosticCode.WrongType, row, "a template row is an object");
                    continue;
                }

                CheckKeys(row, new[] { "id", "name", "values", "cards", "forwards" });
                RequireInt(row, "id", out var id);
                var name = RequireString(row, "name");
                var settings = new Rows<ValueSetting>();
                var values = row.Find("values");
                if (values != null)
                {
                    for (var i = 0; i < values.Keys.Length; i++)
                    {
                        ReadSetting(settings, values.Keys[i], values.Items[i]);
                    }
                }

                var held = row.Find("cards");
                if (held != null)
                {
                    for (var i = 0; i < held.Keys.Length; i++)
                    {
                        var card = CardIntrinsic(held.Keys[i]);
                        if (card < 0 || !held.Items[i].TryInt(out var count))
                        {
                            Error(DiagnosticCode.UnknownName, held.Items[i], "template card " + held.Keys[i] + " is not a card with an integer count");
                            continue;
                        }

                        settings.Add(new ValueSetting(ValueClass.Intrinsic, card, count, false));
                    }
                }

                var forwards = new Rows<Forward>();
                var forwardRows = row.Find("forwards");
                if (forwardRows != null)
                {
                    for (var i = 0; i < forwardRows.Items.Length; i++)
                    {
                        var item = forwardRows.Items[i];
                        CheckKeys(item, new[] { "channel", "to" });
                        var channel = FindName(channels, item.Find("channel")?.Text ?? string.Empty);
                        var to = item.Find("to")?.Text ?? string.Empty;
                        var via = string.CompareOrdinal(to, "Owner") == 0 ? OwnerId() : ReferenceId(to);
                        if (channel == null || via < 0)
                        {
                            Error(DiagnosticCode.UnknownName, item, "a forward names a channel and \"to\": Owner or a reference intrinsic");
                            continue;
                        }

                        forwards.Add(new Forward(channel.Id, via));
                    }
                }

                read.Add(new TemplateDef(id, name, SortSettings(settings.ToArray()), forwards.ToArray(), row.Offset));
            }

            var array = read.ToArray();
            var ids = new int[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                ids[i] = array[i].Id;
            }

            Array.Sort(ids, array);
            for (var i = 1; i < array.Length; i++)
            {
                if (array[i - 1].Id == array[i].Id)
                {
                    Diagnostics.Error(DiagnosticCode.DuplicateId, file, array[i].Offset, -1, -1, "templates " + array[i - 1].Name + " and " + array[i].Name + " share id " + Text(array[i].Id));
                }
            }

            return array;
        }

        static ValueSetting[] SortSettings(ValueSetting[] settings)
        {
            var keys = new long[settings.Length];
            for (var i = 0; i < settings.Length; i++)
            {
                keys[i] = ((long)settings[i].Class << 32) | (uint)settings[i].Id;
            }

            Array.Sort(keys, settings);
            return settings;
        }

        void ReadSetting(Rows<ValueSetting> settings, string name, JsonValue value)
        {
            var stat = FindName(stats, name);
            if (stat != null)
            {
                settings.Add(new ValueSetting(ValueClass.Stat, stat.Id, RequireScaled(value, stat.Scale, name), false));
                return;
            }

            var intrinsic = FindName(intrinsics, name);
            if (intrinsic != null)
            {
                settings.Add(new ValueSetting(ValueClass.Intrinsic, intrinsic.Id, RequireScaled(value, intrinsic.Scale, name), false));
                return;
            }

            var raised = FindName(events, name);
            if (raised != null && value.IsBool)
            {
                settings.Add(new ValueSetting(ValueClass.Event, raised.Id, 0, value.Kind == JsonKind.True));
                return;
            }

            Error(DiagnosticCode.UnknownName, value, "template value " + name + " names no stat, intrinsic or event");
        }

        int OwnerId()
        {
            for (var i = 0; i < intrinsics.Length; i++)
            {
                if (intrinsics[i].Role == IntrinsicRole.Owner)
                {
                    return intrinsics[i].Id;
                }
            }

            return -1;
        }

        int ReferenceId(string name)
        {
            var found = FindName(intrinsics, name);
            return found != null && (found.Role == IntrinsicRole.Reference || found.Role == IntrinsicRole.Owner) ? found.Id : -1;
        }

        int CardIntrinsic(string name)
        {
            for (var i = 0; i < cards.Length; i++)
            {
                if (string.CompareOrdinal(cards[i].Name, name) == 0)
                {
                    return cards[i].Intrinsic;
                }
            }

            var found = FindName(intrinsics, name);
            return found == null ? -1 : found.Id;
        }

        void ScanChains(JsonValue[] rows)
        {
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row.Kind != JsonKind.Object || string.CompareOrdinal(row.Find("layer")?.Text ?? string.Empty, "chain") != 0)
                {
                    continue;
                }

                var id = row.Find("id");
                var name = row.Find("name");
                if (id == null || name == null || !id.TryInt(out var chainId))
                {
                    continue;
                }

                chainIds.Add(chainId);
                chainNames.Add(name.Text);
                var tagRows = row.Find("tags");
                if (tagRows == null)
                {
                    continue;
                }

                for (var t = 0; t < tagRows.Items.Length; t++)
                {
                    var tag = tagRows.Items[t].Text;
                    var known = false;
                    for (var k = 0; k < tags.Count; k++)
                    {
                        known |= string.CompareOrdinal(tags[k], tag) == 0;
                    }

                    if (!known && tag.Length > 0)
                    {
                        tags.Add(tag);
                    }
                }
            }
        }

        ContentPack ReadRules(JsonValue[] rows, PackSchema schema)
        {
            var chainRows = new Rows<ChainRule>();
            var gateRows = new Rows<GateRule>();
            var emitRows = new Rows<EmitRule>();
            var edgeRows = new Rows<EdgeRule>();
            var flowRows = new Rows<FlowRule>();
            var foldRows = new Rows<FoldRule>();
            var eventRows = new Rows<EventRule>();
            var seen = new Rows<int>();
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                ruleId = -1;
                if (row.Kind != JsonKind.Object)
                {
                    Error(DiagnosticCode.WrongType, row, "a rule row is an object");
                    continue;
                }

                RequireInt(row, "id", out var id);
                ruleId = id;
                for (var s = 0; s < seen.Count; s++)
                {
                    if (seen[s] == id)
                    {
                        Error(DiagnosticCode.DuplicateId, row, "rule id " + Text(id) + " is used twice");
                    }
                }

                seen.Add(id);
                var name = RequireString(row, "name");
                var layer = RequireString(row, "layer");
                switch (layer)
                {
                    case "chain":
                        CheckKeys(row, new[] { "id", "name", "layer", "tags", "when" });
                        chainRows.Add(new ChainRule(id, name, row.Offset, ChainTags(row, schema), RequireCondition(row, "when")));
                        break;
                    case "gate":
                        CheckKeys(row, new[] { "id", "name", "layer", "card", "priority", "group", "when", "then" });
                        gateRows.Add(ReadGate(row, id, name));
                        break;
                    case "emit":
                        CheckKeys(row, new[] { "id", "name", "layer", "card", "depthLimit", "nextFrame", "when", "then" });
                        emitRows.Add(ReadEmit(row, id, name));
                        break;
                    case "edge":
                        CheckKeys(row, new[] { "id", "name", "layer", "card", "event", "edge", "when", "then" });
                        edgeRows.Add(ReadEdge(row, id, name));
                        break;
                    case "flow":
                        CheckKeys(row, new[] { "id", "name", "layer", "card", "when", "then" });
                        flowRows.Add(ReadFlow(row, id, name));
                        break;
                    case "fold":
                        CheckKeys(row, new[] { "id", "name", "layer", "value", "floor", "ceiling" });
                        foldRows.Add(ReadFold(row, id, name));
                        break;
                    case "event":
                        CheckKeys(row, new[] { "id", "name", "layer", "event", "enter", "stay" });
                        eventRows.Add(ReadEvent(row, id, name));
                        break;
                    default:
                        Error(DiagnosticCode.BadValue, row.Find("layer"), "layer is chain, gate, emit, edge, flow, fold or event");
                        break;
                }
            }

            ruleId = -1;
            return new ContentPack(schema, ById(chainRows), ById(gateRows), ById(emitRows), ById(edgeRows), ById(flowRows), ById(foldRows), ById(eventRows));
        }

        static T[] ById<T>(Rows<T> rows) where T : Rule
        {
            var array = rows.ToArray();
            var ids = new int[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                ids[i] = array[i].Id;
            }

            Array.Sort(ids, array);
            return array;
        }

        int[] ChainTags(JsonValue row, PackSchema schema)
        {
            var list = row.Find("tags");
            if (list == null)
            {
                return new int[0];
            }

            var ids = new Rows<int>();
            for (var i = 0; i < list.Items.Length; i++)
            {
                var tag = schema.TagOf(list.Items[i].Text);
                if (tag < 0)
                {
                    Error(DiagnosticCode.WrongType, list.Items[i], "a tag is a non-empty string");
                    continue;
                }

                ids.Add(tag);
            }

            var array = ids.ToArray();
            Array.Sort(array);
            return array;
        }

        int Card(JsonValue row)
        {
            var found = row.Find("card");
            if (found == null)
            {
                return -1;
            }

            var card = CardIntrinsic(found.Text);
            if (card < 0)
            {
                Error(DiagnosticCode.UnknownName, found, "\"card\" names no card and no intrinsic: " + found.Text);
            }

            return card;
        }

        Expression OptionalCondition(JsonValue row, string key)
        {
            var found = row.Find(key);
            return found == null ? Expression.True : Condition(found);
        }

        Expression RequireCondition(JsonValue row, string key)
        {
            var found = row.Find(key);
            if (found == null)
            {
                Error(DiagnosticCode.MissingKey, row, "\"" + key + "\" is required");
                return Expression.False;
            }

            return Condition(found);
        }

        Expression Condition(JsonValue value)
        {
            var parsed = Parse(value, NameHint.None);
            if (parsed.Type != ExprType.Bool)
            {
                Error(DiagnosticCode.TypeMismatch, value, "a condition must be true or false, not a number");
                return Expression.False;
            }

            return parsed;
        }

        JsonValue[] Then(JsonValue row)
        {
            var then = row.Find("then");
            if (then == null || then.Kind != JsonKind.Array)
            {
                Error(DiagnosticCode.MissingKey, then ?? row, "\"then\" must be an array");
                return new JsonValue[0];
            }

            return then.Items;
        }

        GateRule ReadGate(JsonValue row, int id, string name)
        {
            RequireInt(row, "priority", out var priority);
            var admit = AdmitVerdict.None;
            var shapes = new Rows<ShapeEffect>();
            var absorbs = new Rows<AbsorbEffect>();
            var effects = Then(row);
            for (var i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.Kind != JsonKind.Object || effect.Keys.Length != 1)
                {
                    Error(DiagnosticCode.WrongType, effect, "a gate effect is an object with one key");
                    continue;
                }

                var key = effect.Keys[0];
                var body = effect.Items[0];
                switch (key)
                {
                    case "deny":
                    case "allow":
                        if (admit != AdmitVerdict.None)
                        {
                            Error(DiagnosticCode.BadValue, effect, "a gate rule has at most one admit verdict");
                        }

                        admit = key == "deny" ? AdmitVerdict.Deny : AdmitVerdict.Allow;
                        break;
                    case "added":
                        shapes.Add(new ShapeEffect(ShapeKind.Added, ShapeAmount(body, ShapeKind.Added, StatLaw.MinScale, key)));
                        break;
                    case "additive":
                        shapes.Add(new ShapeEffect(ShapeKind.Additive, ShapeAmount(body, ShapeKind.Additive, StatLaw.MinScale, key)));
                        break;
                    case "multi":
                        shapes.Add(new ShapeEffect(ShapeKind.Multi, ShapeAmount(body, ShapeKind.Multi, StatLaw.MinScale, key)));
                        break;
                    case "cap":
                        shapes.Add(new ShapeEffect(ShapeKind.Cap, ShapeAmount(body, ShapeKind.Cap, StatLaw.MinScale, key)));
                        break;
                    case "absorb":
                        absorbs.Add(ReadAbsorb(body));
                        break;
                    default:
                        Error(DiagnosticCode.UnknownKey, effect, "a gate effect is deny, allow, added, additive, multi, cap or absorb");
                        break;
                }
            }

            return new GateRule(id, name, Card(row), OptionalCondition(row, "when"), row.Offset, priority, admit, shapes.ToArray(), OptionalInt(row, "group", -1), absorbs.ToArray());
        }

        AbsorbEffect ReadAbsorb(JsonValue body)
        {
            CheckKeys(body, new[] { "budget", "per", "order" });
            var budget = FindName(intrinsics, body.Find("budget")?.Text ?? string.Empty);
            if (budget == null)
            {
                Error(DiagnosticCode.UnknownName, body, "\"budget\" names no intrinsic");
            }

            var per = AbsorbPer.Hit;
            switch (body.Find("per")?.Text ?? "Hit")
            {
                case "Hit": per = AbsorbPer.Hit; break;
                case "Amount": per = AbsorbPer.Amount; break;
                default: Error(DiagnosticCode.BadValue, body.Find("per"), "absorb per is Hit or Amount"); break;
            }

            var order = AbsorbOrder.BySource;
            switch (body.Find("order")?.Text ?? "BySource")
            {
                case "BySource": order = AbsorbOrder.BySource; break;
                case "ByAmountDescending": order = AbsorbOrder.ByAmountDescending; break;
                default: Error(DiagnosticCode.BadValue, body.Find("order"), "absorb order is BySource or ByAmountDescending"); break;
            }

            return new AbsorbEffect(budget == null ? -1 : budget.Id, per, order);
        }

        EmitRule ReadEmit(JsonValue row, int id, string name)
        {
            var actions = new Rows<EmitAction>();
            var per = EmitPer.Record;
            var perSet = false;
            var anyOther = false;
            var nextFrame = OptionalBool(row, "nextFrame", false);
            var items = Then(row);
            for (var i = 0; i < items.Length; i++)
            {
                var body = EmitBody(items[i]);
                if (body == null)
                {
                    continue;
                }

                var actionPer = ReadPer(body);
                if (perSet && actionPer != per)
                {
                    Error(DiagnosticCode.BadEmit, body, "every emit of one rule has the same \"per\"");
                }

                per = actionPer;
                perSet = true;
                var action = ReadAction(body, false);
                anyOther |= action.To != EmitTo.Self || nextFrame;
                actions.Add(action);
            }

            var depthLimit = OptionalInt(row, "depthLimit", anyOther ? RuleLimits.ReflectionDepthDefault : RuleLimits.WorldDepthLimit);
            return new EmitRule(id, name, Card(row), OptionalCondition(row, "when"), row.Offset, depthLimit, nextFrame, per, actions.ToArray());
        }

        JsonValue? EmitBody(JsonValue item)
        {
            if (item.Kind != JsonKind.Object || item.Keys.Length != 1 || string.CompareOrdinal(item.Keys[0], "emit") != 0 || item.Items[0].Kind != JsonKind.Object)
            {
                Error(DiagnosticCode.WrongType, item, "an action is { \"emit\": { ... } }");
                return null;
            }

            return item.Items[0];
        }

        EmitPer ReadPer(JsonValue body)
        {
            switch (body.Find("per")?.Text ?? "Record")
            {
                case "Record": return EmitPer.Record;
                case "Hit": return EmitPer.Hit;
                case "Source": return EmitPer.Source;
                case "Frame": return EmitPer.Frame;
                default:
                    Error(DiagnosticCode.BadValue, body.Find("per"), "per is Record, Hit, Source or Frame");
                    return EmitPer.Record;
            }
        }

        EmitAction ReadAction(JsonValue body, bool edge)
        {
            CheckKeys(body, edge ? new[] { "to", "channel", "amount", "scale", "origin" } : new[] { "to", "channel", "amount", "scale", "per", "over", "origin" });
            var toText = RequireString(body, "to");
            var to = EmitTo.Reference;
            var via = -1;
            switch (toText)
            {
                case "Self": to = EmitTo.Self; break;
                case "Source": to = EmitTo.Source; break;
                case "Owner": to = EmitTo.Owner; via = OwnerId(); break;
                default:
                    via = ReferenceId(toText);
                    if (via < 0)
                    {
                        Error(DiagnosticCode.UnknownName, body.Find("to"), "\"to\" is Self, Source, Owner or an intrinsic with role reference");
                    }

                    break;
            }

            if (to == EmitTo.Owner && via < 0)
            {
                Error(DiagnosticCode.UnknownName, body.Find("to"), "\"to\": Owner needs an intrinsic with role owner");
            }

            var channelText = RequireString(body, "channel");
            var channel = -1;
            if (string.CompareOrdinal(channelText, "same") != 0)
            {
                var found = FindName(channels, channelText);
                if (found == null)
                {
                    Error(DiagnosticCode.UnknownName, body.Find("channel"), "no channel named " + channelText);
                }
                else
                {
                    channel = found.Id;
                }
            }

            var amount = AmountKind.Constant;
            var constant = 0;
            var amountRow = body.Find("amount");
            if (amountRow == null)
            {
                Error(DiagnosticCode.MissingKey, body, "\"amount\" is required");
            }
            else if (amountRow.Kind == JsonKind.Number)
            {
                if (!amountRow.TryInt(out constant))
                {
                    Error(DiagnosticCode.FloatAcrossRows, amountRow, "an emitted amount is an integer; a float never crosses rows");
                }
            }
            else
            {
                switch (amountRow.Text)
                {
                    case "Offered": amount = AmountKind.Offered; break;
                    case "Taken": amount = AmountKind.Taken; break;
                    case "NotTaken": amount = AmountKind.NotTaken; break;
                    default: Error(DiagnosticCode.BadValue, amountRow, "amount is an integer, Offered, Taken or NotTaken"); break;
                }
            }

            var scale = -1;
            var scaleRow = body.Find("scale");
            if (scaleRow != null)
            {
                var stat = FindName(stats, scaleRow.Text);
                if (stat == null)
                {
                    Error(DiagnosticCode.UnknownName, scaleRow, "\"scale\" names no stat");
                }
                else
                {
                    scale = stat.Id;
                }
            }

            var over = new Rows<long>();
            var overRow = body.Find("over");
            if (overRow != null)
            {
                for (var i = 0; i < overRow.Items.Length; i++)
                {
                    var found = FindName(channels, overRow.Items[i].Text);
                    if (found == null)
                    {
                        Error(DiagnosticCode.UnknownName, overRow.Items[i], "\"over\" names no channel " + overRow.Items[i].Text);
                        continue;
                    }

                    over.Add(found.Id);
                }
            }

            var origin = to == EmitTo.Source && amount != AmountKind.Constant ? RecordOrigin.Reflected : RecordOrigin.Triggered;
            var originRow = body.Find("origin");
            if (originRow != null)
            {
                switch (originRow.Text)
                {
                    case "Reflected": origin = RecordOrigin.Reflected; break;
                    case "Triggered": origin = RecordOrigin.Triggered; break;
                    default: Error(DiagnosticCode.BadValue, originRow, "an emitted origin is Reflected or Triggered"); break;
                }
            }

            return new EmitAction(to, via, channel, amount, constant, scale, Expression.SortedSet(over.ToArray()), origin);
        }

        EdgeRule ReadEdge(JsonValue row, int id, string name)
        {
            var raised = FindName(events, RequireString(row, "event"));
            if (raised == null)
            {
                Error(DiagnosticCode.UnknownName, row.Find("event"), "\"event\" names no event");
            }

            var edge = row.Find("edge")?.Text ?? "enter";
            if (edge != "enter" && edge != "leave")
            {
                Error(DiagnosticCode.BadValue, row.Find("edge"), "edge is enter or leave");
            }

            var actions = new Rows<EmitAction>();
            var items = Then(row);
            for (var i = 0; i < items.Length; i++)
            {
                var body = EmitBody(items[i]);
                if (body != null)
                {
                    actions.Add(ReadAction(body, true));
                }
            }

            return new EdgeRule(id, name, Card(row), OptionalCondition(row, "when"), row.Offset, raised == null ? -1 : raised.Id, edge == "enter", actions.ToArray());
        }

        FlowRule ReadFlow(JsonValue row, int id, string name)
        {
            var terms = new Rows<FlowTerm>();
            var items = Then(row);
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item.Kind != JsonKind.Object || item.Keys.Length != 1 || item.Items[0].Kind != JsonKind.Array || item.Items[0].Items.Length < 2)
                {
                    Error(DiagnosticCode.WrongType, item, "a flow term is { \"add\" | \"subtract\": [intrinsic, channel, ...] }");
                    continue;
                }

                var sign = item.Keys[0] == "add" ? 1 : item.Keys[0] == "subtract" ? -1 : 0;
                if (sign == 0)
                {
                    Error(DiagnosticCode.UnknownKey, item, "a flow term is add or subtract");
                    continue;
                }

                var list = item.Items[0].Items;
                var target = FindName(intrinsics, list[0].Text);
                if (target == null)
                {
                    Error(DiagnosticCode.UnknownName, list[0], "a flow writes an intrinsic; none is named " + list[0].Text);
                    continue;
                }

                for (var c = 1; c < list.Length; c++)
                {
                    var channel = FindName(channels, list[c].Text);
                    if (channel == null)
                    {
                        Error(DiagnosticCode.UnknownName, list[c], "a flow reads a channel; none is named " + list[c].Text);
                        continue;
                    }

                    terms.Add(new FlowTerm(target.Id, channel.Id, sign));
                }
            }

            return new FlowRule(id, name, Card(row), OptionalCondition(row, "when"), row.Offset, terms.ToArray());
        }

        FoldRule ReadFold(JsonValue row, int id, string name)
        {
            var value = FindName(intrinsics, RequireString(row, "value"));
            if (value == null)
            {
                Error(DiagnosticCode.UnknownName, row.Find("value"), "a fold bounds an intrinsic");
            }

            return new FoldRule(id, name, row.Offset, value == null ? -1 : value.Id, ReadBound(row.Find("floor")), ReadBound(row.Find("ceiling")));
        }

        Bound ReadBound(JsonValue? bound)
        {
            if (bound == null)
            {
                return Bound.None;
            }

            if (bound.TryInt(out var constant))
            {
                return new Bound(BoundKind.Constant, constant, -1);
            }

            var statName = bound.Find("stat");
            var stat = statName == null ? null : FindName(stats, statName.Text);
            if (stat == null)
            {
                Error(DiagnosticCode.BadValue, bound, "a bound is an integer or { \"stat\": name }");
                return Bound.None;
            }

            return new Bound(BoundKind.Stat, 0, stat.Id);
        }

        EventRule ReadEvent(JsonValue row, int id, string name)
        {
            var raised = FindName(events, RequireString(row, "event"));
            if (raised == null)
            {
                Error(DiagnosticCode.UnknownName, row.Find("event"), "\"event\" names no event");
            }

            var enter = RequireCondition(row, "enter");
            var stay = row.Find("stay") == null ? enter : RequireCondition(row, "stay");
            return new EventRule(id, name, row.Offset, raised == null ? -1 : raised.Id, enter, stay);
        }

        static NameHint HintOf(JsonValue value)
        {
            if (value.Kind != JsonKind.String)
            {
                return NameHint.None;
            }

            switch (value.Text)
            {
                case "record.kind": return NameHint.Kind;
                case "record.channel": return NameHint.Channel;
                case "record.origin": return NameHint.Origin;
                default: return NameHint.None;
            }
        }

        Expression Bad(JsonValue at, DiagnosticCode code, string message)
        {
            Error(code, at, message);
            return Expression.Int(0);
        }

        Expression[] Operands(JsonValue body, int count, string op)
        {
            if (body.Kind != JsonKind.Array || (count > 0 && body.Items.Length != count) || (count == 0 && body.Items.Length == 0))
            {
                Error(DiagnosticCode.WrongType, body, "\"" + op + "\" takes " + (count > 0 ? Text(count) + " operands" : "a non-empty array"));
                return new Expression[0];
            }

            var args = new Expression[body.Items.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Parse(body.Items[i], NameHint.None);
            }

            return args;
        }

        Expression Parse(JsonValue value, NameHint hint)
        {
            switch (value.Kind)
            {
                case JsonKind.True: return Expression.True;
                case JsonKind.False: return Expression.False;
                case JsonKind.Number:
                    if (value.IsInteger && long.TryParse(value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    {
                        return Expression.Int(integer);
                    }

                    return Expression.Float(RequireFloat(value, "a number"));
                case JsonKind.String: return Reference(value, hint);
                case JsonKind.Object:
                    if (value.Keys.Length != 1)
                    {
                        return Bad(value, DiagnosticCode.WrongType, "an operator object has exactly one key");
                    }

                    return Operator(value.Keys[0], value.Items[0], value);
                default:
                    return Bad(value, DiagnosticCode.WrongType, "an expression is a value, a name or an operator object");
            }
        }

        Expression Reference(JsonValue value, NameHint hint)
        {
            var text = value.Text;
            var dot = text.IndexOf('.');
            if (dot > 0)
            {
                var head = text.Substring(0, dot);
                var tail = text.Substring(dot + 1);
                if (head == "record")
                {
                    switch (tail)
                    {
                        case "kind": return Expression.Record(RecordField.Kind);
                        case "channel": return Expression.Record(RecordField.Channel);
                        case "source": return Expression.Record(RecordField.Source);
                        case "origin": return Expression.Record(RecordField.Origin);
                        case "depth": return Expression.Record(RecordField.Depth);
                        case "amount": return Expression.Record(RecordField.Amount);
                        case "hits": return Expression.Record(RecordField.Hits);
                        case "taken": return Expression.Record(RecordField.Taken);
                        case "notTaken": return Expression.Record(RecordField.NotTaken);
                        default: return Bad(value, DiagnosticCode.UnknownName, "no record field " + tail);
                    }
                }

                var subject = Subject.Reference;
                var via = -1;
                switch (head)
                {
                    case "self": subject = Subject.Self; break;
                    case "source": subject = Subject.Source; break;
                    case "owner": subject = Subject.Owner; via = OwnerId(); break;
                    default:
                        via = ReferenceId(head);
                        if (via < 0)
                        {
                            return Bad(value, DiagnosticCode.UnknownName, head + " is not self, source, owner or a reference intrinsic");
                        }

                        break;
                }

                if (subject == Subject.Owner && via < 0)
                {
                    return Bad(value, DiagnosticCode.UnknownName, "owner.X needs an intrinsic with role owner");
                }

                return Cell(value, subject, via, tail);
            }

            switch (hint)
            {
                case NameHint.Kind:
                {
                    var kind = FindName(kinds, text);
                    return kind == null ? Bad(value, DiagnosticCode.UnknownName, "no kind named " + text) : Expression.KindConst(kind.Id);
                }

                case NameHint.Channel:
                {
                    var channel = FindName(channels, text);
                    return channel == null ? Bad(value, DiagnosticCode.UnknownName, "no channel named " + text) : Expression.ChannelConst(channel.Id);
                }

                case NameHint.Origin:
                    switch (text)
                    {
                        case "Direct": return Expression.Int((long)RecordOrigin.Direct);
                        case "Timeline": return Expression.Int((long)RecordOrigin.Timeline);
                        case "Reflected": return Expression.Int((long)RecordOrigin.Reflected);
                        case "Triggered": return Expression.Int((long)RecordOrigin.Triggered);
                        default: return Bad(value, DiagnosticCode.UnknownName, "no origin named " + text);
                    }
            }

            for (var i = 0; i < chainNames.Count; i++)
            {
                if (string.CompareOrdinal(chainNames[i], text) == 0)
                {
                    return Expression.Chain(chainIds[i]);
                }
            }

            return Cell(value, Subject.Self, -1, text);
        }

        Expression Cell(JsonValue at, Subject subject, int via, string name)
        {
            var stat = FindName(stats, name);
            if (stat != null)
            {
                return Expression.Cell(subject, via, ValueClass.Stat, stat.Id);
            }

            var intrinsic = FindName(intrinsics, name);
            if (intrinsic != null)
            {
                return Expression.Cell(subject, via, ValueClass.Intrinsic, intrinsic.Id);
            }

            var raised = FindName(events, name);
            if (raised != null)
            {
                return Expression.Cell(subject, via, ValueClass.Event, raised.Id);
            }

            return Bad(at, DiagnosticCode.UnknownName, "no stat, intrinsic, event or chain named " + name);
        }

        Expression Numeric(Expression value, JsonValue at)
        {
            if (value.Type == ExprType.Bool)
            {
                Error(DiagnosticCode.TypeMismatch, at, "a number is needed, not a condition");
                return Expression.Int(0);
            }

            return value;
        }

        Expression AsInt(Expression value, JsonValue at)
        {
            var number = Numeric(value, at);
            return number.Type == ExprType.Float ? Expression.BoundOf(number) : number;
        }

        Expression AsBool(Expression value, JsonValue at)
        {
            if (value.Type != ExprType.Bool)
            {
                Error(DiagnosticCode.TypeMismatch, at, "a condition is needed, not a number");
                return Expression.False;
            }

            return value;
        }

        void Unify(ref Expression left, ref Expression right, JsonValue at)
        {
            if (left.Type == right.Type)
            {
                return;
            }

            if (left.Type == ExprType.Bool || right.Type == ExprType.Bool)
            {
                Error(DiagnosticCode.TypeMismatch, at, "a condition cannot be compared with a number");
                left = Expression.Int(0);
                right = Expression.Int(0);
                return;
            }

            if (left.Type == ExprType.Int)
            {
                left = left.Op == Op.IntConst ? Expression.Float(left.Integer) : Expression.ToFloat(left);
            }
            else
            {
                right = right.Op == Op.IntConst ? Expression.Float(right.Integer) : Expression.ToFloat(right);
            }
        }

        long[] ChannelList(JsonValue body)
        {
            var list = new Rows<long>();
            if (body.Kind != JsonKind.Array)
            {
                Error(DiagnosticCode.WrongType, body, "a channel list is an array of channel names; [] means every channel");
                return list.ToArray();
            }

            for (var i = 0; i < body.Items.Length; i++)
            {
                var channel = FindName(channels, body.Items[i].Text);
                if (channel == null)
                {
                    Error(DiagnosticCode.UnknownName, body.Items[i], "no channel named " + body.Items[i].Text);
                    continue;
                }

                list.Add(channel.Id);
            }

            return list.ToArray();
        }

        Expression Operator(string op, JsonValue body, JsonValue at)
        {
            switch (op)
            {
                case "and":
                case "or":
                {
                    var args = Operands(body, 0, op);
                    for (var i = 0; i < args.Length; i++)
                    {
                        args[i] = AsBool(args[i], body.Items[i]);
                    }

                    return op == "and" ? Expression.And(args) : Expression.Or(args);
                }

                case "not":
                    return Expression.Not(AsBool(Parse(body, NameHint.None), body));
                case "eq":
                case "ne":
                case "lt":
                case "le":
                case "gt":
                case "ge":
                {
                    if (body.Kind != JsonKind.Array || body.Items.Length != 2)
                    {
                        return Bad(at, DiagnosticCode.WrongType, "\"" + op + "\" takes two operands");
                    }

                    var left = Parse(body.Items[0], HintOf(body.Items[1]));
                    var right = Parse(body.Items[1], HintOf(body.Items[0]));
                    Unify(ref left, ref right, at);
                    var code = op == "eq" ? Op.Eq : op == "ne" ? Op.Ne : op == "lt" ? Op.Lt : op == "le" ? Op.Le : op == "gt" ? Op.Gt : Op.Ge;
                    if (left.Type == ExprType.Bool && code != Op.Eq && code != Op.Ne)
                    {
                        return Bad(at, DiagnosticCode.TypeMismatch, "conditions are compared only by eq and ne");
                    }

                    return Expression.Compare(code, left, right);
                }

                case "in":
                {
                    if (body.Kind != JsonKind.Array || body.Items.Length != 2 || body.Items[1].Kind != JsonKind.Array)
                    {
                        return Bad(at, DiagnosticCode.WrongType, "\"in\" takes [value, [members]]");
                    }

                    var value = AsInt(Parse(body.Items[0], NameHint.None), body.Items[0]);
                    var hint = HintOf(body.Items[0]);
                    var members = new long[body.Items[1].Items.Length];
                    for (var i = 0; i < members.Length; i++)
                    {
                        var member = Parse(body.Items[1].Items[i], hint);
                        if (member.Op != Op.IntConst && member.Op != Op.KindConst && member.Op != Op.ChannelConst)
                        {
                            Error(DiagnosticCode.TypeMismatch, body.Items[1].Items[i], "members of \"in\" are integer constants or names");
                        }

                        members[i] = member.Integer;
                    }

                    return Expression.InSet(value, members);
                }

                case "bit":
                {
                    var args = Operands(body, 2, op);
                    return args.Length == 2 ? Expression.Bit(AsInt(args[0], body.Items[0]), AsInt(args[1], body.Items[1])) : Expression.False;
                }

                case "below":
                {
                    var args = Operands(body, 4, op);
                    if (args.Length != 4)
                    {
                        return Expression.False;
                    }

                    return Expression.Below(AsInt(args[0], body.Items[0]), AsInt(args[1], body.Items[1]), AsInt(args[2], body.Items[2]), AsInt(args[3], body.Items[3]));
                }

                case "add":
                case "sub":
                case "mul":
                case "min":
                case "max":
                {
                    var args = Operands(body, 2, op);
                    if (args.Length != 2)
                    {
                        return Expression.Int(0);
                    }

                    var left = Numeric(args[0], body.Items[0]);
                    var right = Numeric(args[1], body.Items[1]);
                    Unify(ref left, ref right, at);
                    var code = op == "add" ? Op.Add : op == "sub" ? Op.Sub : op == "mul" ? Op.Mul : op == "min" ? Op.Min : Op.Max;
                    return Expression.Arithmetic(code, left, right);
                }

                case "neg":
                    return Expression.Neg(Numeric(Parse(body, NameHint.None), body));
                case "float":
                {
                    var value = Numeric(Parse(body, NameHint.None), body);
                    return value.Type == ExprType.Float ? value : Expression.ToFloat(value);
                }

                case "bound":
                    return AsInt(Parse(body, NameHint.None), body);
                case "if":
                {
                    var args = Operands(body, 3, op);
                    if (args.Length != 3)
                    {
                        return Expression.Int(0);
                    }

                    var whenTrue = args[1];
                    var whenFalse = args[2];
                    if (whenTrue.Type != ExprType.Bool || whenFalse.Type != ExprType.Bool)
                    {
                        Unify(ref whenTrue, ref whenFalse, at);
                    }
                    else if (whenTrue.Type != whenFalse.Type)
                    {
                        return Bad(at, DiagnosticCode.TypeMismatch, "both branches of \"if\" have one type");
                    }

                    return Expression.Select(AsBool(args[0], body.Items[0]), whenTrue, whenFalse);
                }

                case "offered": return Expression.Aggregate(Measure.Offered, AggregateGroup.Current, ChannelList(body));
                case "taken": return Expression.Aggregate(Measure.Taken, AggregateGroup.Current, ChannelList(body));
                case "notTaken": return Expression.Aggregate(Measure.NotTaken, AggregateGroup.Current, ChannelList(body));
                case "frameOffered": return Expression.Aggregate(Measure.Offered, AggregateGroup.Frame, ChannelList(body));
                case "frameTaken": return Expression.Aggregate(Measure.Taken, AggregateGroup.Frame, ChannelList(body));
                case "frameNotTaken": return Expression.Aggregate(Measure.NotTaken, AggregateGroup.Frame, ChannelList(body));
                case "total":
                {
                    var channel = FindName(channels, body.Text);
                    return channel == null ? Bad(body, DiagnosticCode.UnknownName, "no channel named " + body.Text) : Expression.Total(channel.Id);
                }

                case "kind":
                {
                    var kind = FindName(kinds, body.Text);
                    return kind == null ? Bad(body, DiagnosticCode.UnknownName, "no kind named " + body.Text) : Expression.KindConst(kind.Id);
                }

                case "channel":
                {
                    var channel = FindName(channels, body.Text);
                    return channel == null ? Bad(body, DiagnosticCode.UnknownName, "no channel named " + body.Text) : Expression.ChannelConst(channel.Id);
                }

                case "chain":
                {
                    for (var i = 0; i < chainNames.Count; i++)
                    {
                        if (string.CompareOrdinal(chainNames[i], body.Text) == 0)
                        {
                            return Expression.Chain(chainIds[i]);
                        }
                    }

                    Error(DiagnosticCode.UnknownName, body, "no chain named " + body.Text);
                    return Expression.False;
                }

                case "any":
                {
                    for (var i = 0; i < tags.Count; i++)
                    {
                        if (string.CompareOrdinal(tags[i], body.Text) == 0)
                        {
                            return Expression.AnyChain(i);
                        }
                    }

                    Error(DiagnosticCode.UnknownName, body, "no chain carries the tag " + body.Text);
                    return Expression.False;
                }

                case "held":
                {
                    var card = CardIntrinsic(body.Text);
                    if (card < 0)
                    {
                        Error(DiagnosticCode.UnknownName, body, "no card named " + body.Text);
                        return Expression.False;
                    }

                    return Expression.Compare(Op.Gt, Expression.Cell(Subject.Self, -1, ValueClass.Intrinsic, card), Expression.Int(0));
                }

                default:
                    return Bad(at, DiagnosticCode.UnknownKey, "unknown operator \"" + op + "\"");
            }
        }
    }
}
