using System;
using System.Collections.Generic;
using Sv.Columns;
using Sv.Rules.Emit;

namespace Sv.Pack
{
    public static class ColumnsGlue
    {
        public const string FileName = TlGlueEmitter.FileName;
        public const string EntitiesFileName = TlGlueEmitter.EntitiesFileName;
        public const string CardSuffix = "Card";
        public const string OutSuffix = TlGlueEmitter.OutSuffix;
        public const string FeedSuffix = TlGlueEmitter.FeedSuffix;
        public const string ClipSuffix = TlGlueEmitter.ClipSuffix;
        public const string TrackSuffix = TlGlueEmitter.TrackSuffix;

        public static bool IsColumnPack(string path) =>
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".sv.json", StringComparison.OrdinalIgnoreCase);

        public static string CardCell(string card) => Pascal(card) + CardSuffix;

        public static string OutboxCell(string channel) => Pascal(channel) + OutSuffix;

        public static string Emit(ColumnPack pack, string rootNamespace, bool entities)
        {
            var code = new CodeWriter();
            var glue = pack.Timelines;
            code.Line("using Tl;");
            code.Blank();
            code.Open("namespace " + rootNamespace + ".Glue");
            var first = true;
            foreach (var card in glue.Cards)
            {
                if (!first)
                {
                    code.Blank();
                }

                first = false;
                var cell = CardCell(card);
                var feed = cell + FeedSuffix;
                var clip = cell + ClipSuffix;
                State(code, cell, entities);
                code.Blank();
                code.Line("[TlKind]");
                code.Open("public struct " + feed);
                code.Line("public static void Combine(ref int total, in int next) => total = next > total ? next : total;");
                code.Blank();
                code.Line("public static void Apply(in int total, ref " + cell + " card) => card.Value = total > card.Value ? total : card.Value;");
                code.Close();
                code.Blank();
                code.Line("[TlClip(\"" + rootNamespace + "/" + card + " card\")]");
                code.Line("public struct " + clip + " { public int Level; }");
                code.Blank();
                code.Open("public struct " + cell + TrackSuffix + " : ITlTrack<" + clip + ">");
                code.Line("[Feeds(typeof(" + feed + "))]");
                code.Line("public static int Tick(in TlFrame<" + clip + "> frame) => frame.Has(FrameFlags.ClipStart) && !frame.IsBackward ? frame.Clip.Level : 0;");
                code.Close();
            }

            foreach (var outbox in glue.Outbox)
            {
                if (!first)
                {
                    code.Blank();
                }

                first = false;
                var name = Pascal(outbox.Channel);
                var cell = OutboxCell(outbox.Channel);
                State(code, cell, entities);
                code.Blank();
                code.Line("[TlKind]");
                code.Open("public struct " + name + FeedSuffix);
                code.Line("public static void Combine(ref int total, in int next) => total += next;");
                code.Blank();
                code.Line("public static void Apply(in int total, ref " + cell + " outbox) => outbox.Value += total;");
                code.Close();
                code.Blank();
                code.Line("[TlClip(\"" + rootNamespace + "/" + outbox.Channel + "\")]");
                code.Line("public struct " + name + ClipSuffix + " { public int Amount; }");
                code.Blank();
                code.Open("public struct " + name + TrackSuffix + " : ITlTrack<" + name + ClipSuffix + ">");
                code.Line("[Feeds(typeof(" + name + FeedSuffix + "))]");
                code.Line("public static int Tick(in TlFrame<" + name + ClipSuffix + "> frame) => frame.Direction * frame.Clip.Amount;");
                code.Close();
            }

            code.Close();
            return code.ToString();
        }

        public static string EmitEntities(ColumnPack pack, string rootNamespace)
        {
            var code = new CodeWriter();
            var glue = pack.Timelines;
            var prefix = "global::" + rootNamespace + ".Glue.";
            foreach (var card in glue.Cards)
            {
                code.Line("[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsCardSystem<" + prefix + CardCell(card) + ">))]");
            }

            foreach (var outbox in glue.Outbox)
            {
                code.Line("[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsOutboxSystem<" + prefix + OutboxCell(outbox.Channel) + ">))]");
            }

            code.Blank();
            code.Open("namespace " + rootNamespace + ".Glue");
            var first = true;
            foreach (var card in glue.Cards)
            {
                if (!first)
                {
                    code.Blank();
                }

                first = false;
                code.Open("public partial struct " + CardCell(card) + " : global::Unity.Entities.IComponentData, global::Sv.Columns.Entities.IColumnsCardCell");
                code.Line("public int Value;");
                code.Blank();
                code.Line("public string Card => " + Literal(card) + ";");
                code.Blank();
                Property(code, "Level");
                code.Close();
            }

            foreach (var outbox in glue.Outbox)
            {
                if (!first)
                {
                    code.Blank();
                }

                first = false;
                code.Open("public partial struct " + OutboxCell(outbox.Channel) + " : global::Unity.Entities.IComponentData, global::Sv.Columns.Entities.IColumnsOutboxCell");
                code.Line("public int Value;");
                code.Blank();
                code.Line("public string Channel => " + Literal(outbox.Channel) + ";");
                code.Blank();
                code.Line("public string TargetLane => " + Literal(outbox.Target ?? string.Empty) + ";");
                code.Blank();
                Property(code, "Amount");
                code.Close();
            }

            if (glue.Starts.Count > 0)
            {
                if (!first)
                {
                    code.Blank();
                }

                code.Open("public static class TimelineRules");
                code.Open("public static void AddTo(global::Unity.Entities.DynamicBuffer<global::Sv.Columns.Entities.ColumnsTimelineRule> rules, global::Sv.Columns.CompiledPack pack, global::System.Func<string, ushort> findTimeline)");
                foreach (var start in glue.Starts)
                {
                    code.Line("rules.Add(new global::Sv.Columns.Entities.ColumnsTimelineRule { Event = pack.Event(" + Literal(start.Event) + "), Timeline = findTimeline(" + Literal(start.Timeline) + ") });");
                }

                code.Close();
                code.Close();
            }

            code.Close();
            return code.ToString();
        }

        public static List<EmittedFile> Files(ColumnPack pack, string rootNamespace, bool unity)
        {
            var files = new List<EmittedFile> { new EmittedFile(FileName, Emit(pack, rootNamespace, unity)) };
            if (unity)
            {
                files.Add(new EmittedFile(EntitiesFileName, EmitEntities(pack, rootNamespace)));
            }

            return files;
        }

        static void State(CodeWriter code, string cell, bool entities)
        {
            code.Line("[TlState]");
            code.Line(entities ? "public partial struct " + cell + " { }" : "public partial struct " + cell + " { public int Value; }");
        }

        static void Property(CodeWriter code, string name)
        {
            code.Open("public int " + name);
            code.Line("get => Value;");
            code.Line("set => Value = value;");
            code.Close();
        }

        static string Pascal(string name)
        {
            var identifier = Names.Identifier(name).TrimStart('@');
            return identifier[0] >= 'a' && identifier[0] <= 'z' ? char.ToUpperInvariant(identifier[0]) + identifier.Substring(1) : identifier;
        }

        static string Literal(string text)
        {
            var quoted = new System.Text.StringBuilder(text.Length + 2).Append('"');
            foreach (var c in text)
            {
                quoted.Append(c == '"' || c == '\\' ? "\\" + c : c.ToString());
            }

            return quoted.Append('"').ToString();
        }
    }
}
