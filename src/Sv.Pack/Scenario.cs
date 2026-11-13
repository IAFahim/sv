using System;
using System.Collections.Generic;
using System.Globalization;
using Sv.Rules;

namespace Sv.Pack
{
    public sealed class ScenarioEntity
    {
        public string Name = string.Empty;
        public int Template;
        public int TemplateIndex;
        public long Frame;
        public int Slot;
    }

    public sealed class ScenarioSend
    {
        public long Frame;
        public int Target;
        public int Source;
        public int Kind;
        public int Channel;
        public int Amount;
        public int Hits = 1;
        public Origin Origin;
    }

    public sealed class ScenarioKill
    {
        public long Frame;
        public int Entity;
    }

    public sealed class ScenarioModifier
    {
        public long Frame;
        public int Entity;
        public int Stat;
        public ShapeKind Shape;
        public int Value;
        public int Source;
        public bool Remove;
        public StatLayer Layer;
    }

    public sealed class ScenarioExpect
    {
        public long Frame;
        public int Entity;
        public ValueClass Class;
        public int Value;
        public string ValueName = string.Empty;
        public int EqualsValue;
    }

    public sealed class Scenario
    {
        public const int SchemaVersion = 1;
        public const int Unset = -1;
        public const string Suffix = ".scenario.json";

        public string Name = string.Empty;
        public long Frames;
        public int Capacity;
        public readonly List<ScenarioEntity> Entities = new List<ScenarioEntity>();
        public readonly List<ScenarioSend> Sends = new List<ScenarioSend>();
        public readonly List<ScenarioKill> Kills = new List<ScenarioKill>();
        public readonly List<ScenarioModifier> Modifiers = new List<ScenarioModifier>();
        public readonly List<ScenarioExpect> Expects = new List<ScenarioExpect>();
        public ulong? Digest;

        public int SlotOf(int entity) => Entities[entity].Slot;
    }

    public sealed class ScenarioError : Exception
    {
        public ScenarioError(string file, int offset, string message)
            : base(file + "(" + offset.ToString(CultureInfo.InvariantCulture) + "): error SV0800: " + message)
        {
        }
    }

    public static class ScenarioReader
    {
        static readonly string[] TopKeys = { "schema", "name", "frames", "capacity", "entities", "sends", "kills", "modifiers", "expect", "digest" };
        static readonly string[] EntityKeys = { "name", "template", "frame" };
        static readonly string[] SendKeys = { "frame", "target", "source", "kind", "channel", "amount", "hits", "origin" };
        static readonly string[] KillKeys = { "frame", "entity" };
        static readonly string[] ModifierKeys = { "frame", "entity", "stat", "shape", "value", "source", "op", "layer" };
        static readonly string[] ExpectKeys = { "frame", "entity", "value", "equals" };

        public static Scenario Read(string file, string text, PackSchema schema)
        {
            var parse = JsonReader.Parse(text);
            if (!parse.Ok || parse.Value == null)
            {
                throw new ScenarioError(file, parse.ErrorOffset, parse.Error);
            }

            var root = parse.Value;
            var reader = new Session(file, schema);
            reader.Keys(root, TopKeys);
            if (reader.Int(root, "schema") != Scenario.SchemaVersion)
            {
                throw new ScenarioError(file, root.Offset, "scenario schema must be 1");
            }

            var scenario = new Scenario
            {
                Name = reader.Text(root, "name"),
                Frames = reader.Int(root, "frames"),
            };
            var capacity = reader.Optional(root, "capacity");
            scenario.Capacity = capacity == null ? 0 : reader.IntOf(capacity);
            foreach (var row in reader.Rows(root, "entities"))
            {
                reader.Keys(row, EntityKeys);
                var name = reader.Text(row, "name");
                if (reader.EntityIndex(scenario, name) >= 0)
                {
                    throw new ScenarioError(file, row.Offset, "entity '" + name + "' is declared twice");
                }

                var template = reader.Template(row, "template", out var templateIndex);
                var frame = reader.Optional(row, "frame");
                scenario.Entities.Add(new ScenarioEntity { Name = name, Template = template, TemplateIndex = templateIndex, Frame = frame == null ? 0 : reader.IntOf(frame) });
            }

            foreach (var row in reader.Rows(root, "sends"))
            {
                reader.Keys(row, SendKeys);
                var kind = reader.Optional(row, "kind");
                var hits = reader.Optional(row, "hits");
                var origin = reader.Optional(row, "origin");
                scenario.Sends.Add(new ScenarioSend
                {
                    Frame = reader.Int(row, "frame"),
                    Target = reader.Entity(scenario, row, "target"),
                    Source = row.Find("source") == null ? Scenario.Unset : reader.Entity(scenario, row, "source"),
                    Kind = kind == null ? Scenario.Unset : reader.Named(kind, ValueClass.Kind),
                    Channel = reader.Named(reader.Required(row, "channel"), ValueClass.Channel),
                    Amount = reader.Int(row, "amount"),
                    Hits = hits == null ? 1 : reader.IntOf(hits),
                    Origin = origin == null ? Origin.Direct : reader.OriginOf(origin),
                });
            }

            foreach (var row in reader.Rows(root, "kills"))
            {
                reader.Keys(row, KillKeys);
                scenario.Kills.Add(new ScenarioKill { Frame = reader.Int(row, "frame"), Entity = reader.Entity(scenario, row, "entity") });
            }

            foreach (var row in reader.Rows(root, "modifiers"))
            {
                reader.Keys(row, ModifierKeys);
                var op = row.Find("op");
                var remove = op != null && reader.TextOf(op) == "remove";
                var shape = row.Find("shape");
                var value = row.Find("value");
                var layer = row.Find("layer");
                scenario.Modifiers.Add(new ScenarioModifier
                {
                    Frame = reader.Int(row, "frame"),
                    Entity = reader.Entity(scenario, row, "entity"),
                    Stat = reader.Named(reader.Required(row, "stat"), ValueClass.Stat),
                    Shape = shape == null ? ShapeKind.Added : reader.ShapeOf(shape),
                    Value = value == null ? 0 : reader.IntOf(value),
                    Source = reader.Int(row, "source"),
                    Remove = remove,
                    Layer = layer == null ? StatLayer.Core : reader.LayerOf(layer),
                });
            }

            foreach (var row in reader.Rows(root, "expect"))
            {
                reader.Keys(row, ExpectKeys);
                var valueName = reader.Text(row, "value");
                var expect = new ScenarioExpect
                {
                    Frame = reader.Int(row, "frame"),
                    Entity = reader.Entity(scenario, row, "entity"),
                    ValueName = valueName,
                    EqualsValue = reader.EqualsOf(reader.Required(row, "equals")),
                };
                reader.Value(row, valueName, out expect.Class, out expect.Value);
                scenario.Expects.Add(expect);
            }

            var digest = reader.Optional(root, "digest");
            if (digest != null)
            {
                if (!ulong.TryParse(reader.TextOf(digest), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pinned))
                {
                    throw new ScenarioError(file, digest.Offset, "digest is 16 hex digits");
                }

                scenario.Digest = pinned;
            }

            return scenario;
        }

        sealed class Session
        {
            readonly string file;
            readonly PackSchema schema;

            public Session(string file, PackSchema schema)
            {
                this.file = file;
                this.schema = schema;
            }

            public void Keys(JsonValue row, string[] allowed)
            {
                if (row.Kind != JsonKind.Object)
                {
                    throw new ScenarioError(file, row.Offset, "expected an object");
                }

                for (var i = 0; i < row.Keys.Length; i++)
                {
                    if (Array.IndexOf(allowed, row.Keys[i]) < 0)
                    {
                        throw new ScenarioError(file, row.Items[i].Offset, "unknown key '" + row.Keys[i] + "'");
                    }
                }
            }

            public JsonValue? Optional(JsonValue row, string key) => row.Find(key);

            public JsonValue Required(JsonValue row, string key) =>
                row.Find(key) ?? throw new ScenarioError(file, row.Offset, "missing key '" + key + "'");

            public JsonValue[] Rows(JsonValue row, string key)
            {
                var found = row.Find(key);
                if (found == null)
                {
                    return new JsonValue[0];
                }

                if (found.Kind != JsonKind.Array)
                {
                    throw new ScenarioError(file, found.Offset, "'" + key + "' is an array");
                }

                return found.Items;
            }

            public int IntOf(JsonValue value) =>
                value.TryInt(out var number) ? number : throw new ScenarioError(file, value.Offset, "expected an integer");

            public int Int(JsonValue row, string key) => IntOf(Required(row, key));

            public string TextOf(JsonValue value) =>
                value.Kind == JsonKind.String ? value.Text : throw new ScenarioError(file, value.Offset, "expected a string");

            public string Text(JsonValue row, string key) => TextOf(Required(row, key));

            public int EqualsOf(JsonValue value)
            {
                if (value.Kind == JsonKind.True || value.Kind == JsonKind.False)
                {
                    return value.Kind == JsonKind.True ? 1 : 0;
                }

                return IntOf(value);
            }

            public int Named(JsonValue value, ValueClass valueClass)
            {
                var name = TextOf(value);
                var found = schema.FindByName(valueClass, name);
                return found == null ? throw new ScenarioError(file, value.Offset, "unknown " + valueClass.ToString().ToLowerInvariant() + " '" + name + "'") : found.Id;
            }

            public int Template(JsonValue row, string key, out int index)
            {
                var value = Required(row, key);
                var name = TextOf(value);
                for (var t = 0; t < schema.Templates.Length; t++)
                {
                    if (string.CompareOrdinal(schema.Templates[t].Name, name) == 0)
                    {
                        index = t;
                        return schema.Templates[t].Id;
                    }
                }

                throw new ScenarioError(file, value.Offset, "unknown template '" + name + "'");
            }

            public int EntityIndex(Scenario scenario, string name)
            {
                for (var i = 0; i < scenario.Entities.Count; i++)
                {
                    if (string.CompareOrdinal(scenario.Entities[i].Name, name) == 0)
                    {
                        return i;
                    }
                }

                return -1;
            }

            public int Entity(Scenario scenario, JsonValue row, string key)
            {
                var value = Required(row, key);
                var index = EntityIndex(scenario, TextOf(value));
                return index >= 0 ? index : throw new ScenarioError(file, value.Offset, "unknown entity '" + value.Text + "'");
            }

            public Origin OriginOf(JsonValue value)
            {
                switch (TextOf(value))
                {
                    case "Direct": return Origin.Direct;
                    case "Timeline": return Origin.Timeline;
                    default: throw new ScenarioError(file, value.Offset, "a scenario sends Direct or Timeline records");
                }
            }

            public ShapeKind ShapeOf(JsonValue value)
            {
                switch (TextOf(value))
                {
                    case "Added": return ShapeKind.Added;
                    case "Additive": return ShapeKind.Additive;
                    case "Multi": return ShapeKind.Multi;
                    case "Cap": return ShapeKind.Cap;
                    default: throw new ScenarioError(file, value.Offset, "shape is Added, Additive, Multi or Cap");
                }
            }

            public StatLayer LayerOf(JsonValue value)
            {
                switch (TextOf(value))
                {
                    case "core": return StatLayer.Core;
                    case "external": return StatLayer.External;
                    default: throw new ScenarioError(file, value.Offset, "layer is core or external");
                }
            }

            public void Value(JsonValue row, string name, out ValueClass valueClass, out int id)
            {
                var classes = new[] { ValueClass.Intrinsic, ValueClass.Stat, ValueClass.Event };
                for (var c = 0; c < classes.Length; c++)
                {
                    var found = schema.FindByName(classes[c], name);
                    if (found != null)
                    {
                        valueClass = classes[c];
                        id = found.Id;
                        return;
                    }
                }

                throw new ScenarioError(file, row.Offset, "unknown value '" + name + "'");
            }
        }
    }

    public static class SlotPlanner
    {
        public static void Plan(Scenario scenario)
        {
            var chunkTemplate = new List<int>();
            var used = new List<ulong>();
            var order = new List<int>();
            for (var i = 0; i < scenario.Entities.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((left, right) =>
            {
                var byFrame = scenario.Entities[left].Frame.CompareTo(scenario.Entities[right].Frame);
                return byFrame != 0 ? byFrame : left.CompareTo(right);
            });
            for (var at = 0; at < order.Count; at++)
            {
                var entity = scenario.Entities[order[at]];
                var chunk = -1;
                for (var c = 0; c < chunkTemplate.Count && chunk < 0; c++)
                {
                    chunk = chunkTemplate[c] == entity.TemplateIndex && ~(used[c] | Sv.Slot.Reserved(c)) != 0ul ? c : -1;
                }

                if (chunk < 0)
                {
                    chunk = chunkTemplate.Count;
                    chunkTemplate.Add(entity.TemplateIndex);
                    used.Add(0ul);
                }

                var row = Bits.Tzcnt(~(used[chunk] | Sv.Slot.Reserved(chunk)));
                used[chunk] |= 1ul << row;
                entity.Slot = Sv.Slot.Of(chunk, row);
            }

            var highest = 0;
            for (var i = 0; i < scenario.Entities.Count; i++)
            {
                highest = Math.Max(highest, scenario.Entities[i].Slot + 1);
            }

            scenario.Capacity = Math.Max(scenario.Capacity, Math.Max(Sv.Slot.Rows, highest));
        }
    }
}
