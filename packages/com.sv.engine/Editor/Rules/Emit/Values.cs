namespace Sv.Rules.Emit
{
    public static class ValuesEmitter
    {
        public const string FileName = "Values.g.cs";

        public static string Emit(PackSchema schema, ulong packHash, string ns)
        {
            var code = new CodeWriter();
            code.Line("using Sv;");
            code.Blank();
            code.Open("namespace " + ns);
            Enum(code, "Stat", "ushort", schema.Stats);
            code.Blank();
            Enum(code, "Intrinsic", "ushort", schema.Intrinsics);
            code.Blank();
            Enum(code, "Event", "ushort", schema.Events);
            code.Blank();
            Enum(code, "Channel", "byte", schema.Channels);
            code.Blank();
            Enum(code, "Kind", "byte", schema.Kinds);
            code.Blank();
            code.Open("public enum Template : int");
            for (var i = 0; i < schema.Templates.Length; i++)
            {
                code.Line(Names.Identifier(schema.Templates[i].Name) + " = " + Names.Number(i) + ",");
            }

            code.Close();
            code.Blank();
            code.Open("public static class Layout");
            code.Line("public const ulong CompiledAgainst = " + Names.Hex(packHash) + ";");
            code.Blank();
            code.Line("public static readonly ulong PackHash = " + Names.Hex(packHash) + ";");
            code.Blank();
            code.Line("public static readonly int LanesPerChunk = " + Names.Number(schema.NextLane) + ";");
            code.Blank();
            code.Line("public static readonly int Channels = " + Names.Number(schema.Channels.Length) + ";");
            code.Blank();
            code.Line("public static readonly int Kinds = " + Names.Number(schema.Kinds.Length) + ";");
            code.Blank();
            code.Line("public static int LaneOf(Stat stat) => (int)stat;");
            code.Blank();
            code.Line("public static int LaneOf(Intrinsic intrinsic) => (int)intrinsic;");
            code.Blank();
            code.Line("public static int LaneOf(Event raised) => (int)raised >> 5;");
            code.Blank();
            code.Line("public static int WordOf(Event raised) => (int)raised & 31;");
            code.Blank();
            code.Line("public static bool Matches(ulong callerCompiledAgainst) => callerCompiledAgainst == PackHash;");
            code.Blank();
            code.Open("public static LaneClass[] Classes()");
            code.Line("var classes = new LaneClass[" + Names.Number(schema.NextLane) + "];");
            for (var i = 0; i < schema.Stats.Length; i++)
            {
                code.Line("classes[" + Names.Number(schema.Stats[i].Lane) + "] = LaneClass.Stat;");
            }

            for (var i = 0; i < schema.Intrinsics.Length; i++)
            {
                code.Line("classes[" + Names.Number(schema.Intrinsics[i].Lane) + "] = LaneClass.Intrinsic;");
            }

            var lastEventLane = -1;
            for (var i = 0; i < schema.Events.Length; i++)
            {
                var lane = schema.Events[i].Lane;
                if (lane != lastEventLane)
                {
                    code.Line("classes[" + Names.Number(lane) + "] = LaneClass.Event;");
                    lastEventLane = lane;
                }
            }

            code.Line("return classes;");
            code.Close();
            code.Blank();
            Switch(code, "public static int ScaleOf(Stat stat)", "stat", "Stat", schema.Stats, true);
            code.Blank();
            Switch(code, "public static int ScaleOf(Intrinsic intrinsic)", "intrinsic", "Intrinsic", schema.Intrinsics, true);
            code.Blank();
            Switch(code, "public static int DefaultOf(Stat stat)", "stat", "Stat", schema.Stats, false);
            code.Blank();
            Switch(code, "public static int DefaultOf(Intrinsic intrinsic)", "intrinsic", "Intrinsic", schema.Intrinsics, false);
            code.Blank();
            code.Open("public static bool DefaultOf(Event raised)");
            var anyRaised = false;
            for (var i = 0; i < schema.Events.Length; i++)
            {
                anyRaised |= schema.Events[i].EventDefault;
            }

            if (anyRaised)
            {
                code.Open("switch (raised)");
                for (var i = 0; i < schema.Events.Length; i++)
                {
                    if (schema.Events[i].EventDefault)
                    {
                        code.Line("case Event." + Names.Identifier(schema.Events[i].Name) + ":");
                    }
                }

                code.Line("    return true;");
                code.Line("default:");
                code.Line("    return false;");
                code.Close();
            }
            else
            {
                code.Line("return false;");
            }

            code.Close();
            code.Close();
            code.Close();
            return code.ToString();
        }

        static void Enum(CodeWriter code, string name, string underlying, ValueDef[] rows)
        {
            code.Open("public enum " + name + " : " + underlying);
            for (var i = 0; i < rows.Length; i++)
            {
                code.Line(Names.Identifier(rows[i].Name) + " = " + Names.Number(rows[i].Id) + ",");
            }

            code.Close();
        }

        static void Switch(CodeWriter code, string head, string argument, string type, ValueDef[] rows, bool scale)
        {
            code.Open(head);
            code.Open("switch (" + argument + ")");
            for (var i = 0; i < rows.Length; i++)
            {
                var value = scale ? rows[i].Scale : rows[i].IntDefault;
                var plain = scale ? 1 : 0;
                if (value != plain)
                {
                    code.Line("case " + type + "." + Names.Identifier(rows[i].Name) + ": return " + Names.Int(value) + ";");
                }
            }

            code.Line("default: return " + (scale ? "1" : "0") + ";");
            code.Close();
            code.Close();
        }
    }
}
