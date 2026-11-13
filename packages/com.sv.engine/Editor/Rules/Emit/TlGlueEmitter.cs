using System.Collections.Generic;

namespace Sv.Rules.Emit
{
    public static class TlGlueEmitter
    {
        public const string FileName = "Glue.g.cs";
        public const string OutSuffix = "Out";
        public const string FeedSuffix = "Feed";
        public const string ClipSuffix = "Clip";
        public const string TrackSuffix = "Track";

        public static string Emit(PackPlan plan, string rootNamespace)
        {
            var code = new CodeWriter();
            code.Line("using Tl;");
            code.Blank();
            code.Open("namespace " + rootNamespace + ".Glue");
            var channels = TimelineChannels(plan);
            for (var i = 0; i < channels.Count; i++)
            {
                if (i > 0)
                {
                    code.Blank();
                }

                Channel(code, rootNamespace, channels[i]);
            }

            code.Close();
            return code.ToString();
        }

        public static List<string> TimelineChannels(PackPlan plan)
        {
            var names = new List<string>();
            var channels = plan.Schema.Channels;
            for (var i = 0; i < channels.Length; i++)
            {
                if (channels[i].Timeline)
                {
                    names.Add(channels[i].Name);
                }
            }

            return names;
        }

        static void Channel(CodeWriter code, string rootNamespace, string channel)
        {
            var name = Names.Identifier(channel);
            code.Line("[TlState]");
            code.Line("public partial struct " + name + OutSuffix + " { public int Value; }");
            code.Blank();
            code.Line("[TlKind]");
            code.Open("public struct " + name + FeedSuffix);
            code.Line("public static void Combine(ref int total, in int next) => total += next;");
            code.Blank();
            code.Line("public static void Apply(in int total, ref " + name + OutSuffix + " outbox) => outbox.Value += total;");
            code.Close();
            code.Blank();
            code.Line("[TlClip(\"" + rootNamespace + "/" + channel + "\")]");
            code.Line("public struct " + name + ClipSuffix + " { public int Amount; }");
            code.Blank();
            code.Open("public struct " + name + TrackSuffix + " : ITlTrack<" + name + ClipSuffix + ">");
            code.Line("[Feeds(typeof(" + name + FeedSuffix + "))]");
            code.Line("public static int Tick(in TlFrame<" + name + ClipSuffix + "> frame) => frame.Direction * frame.Clip.Amount;");
            code.Close();
        }
    }
}
