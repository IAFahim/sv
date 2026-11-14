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
            var requests = RequestEvents(plan);
            code.Line("using Tl;");
            if (requests.Count > 0)
            {
                code.Line("using Sv.Host;");
            }

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

            if (requests.Count > 0)
            {
                if (channels.Count > 0)
                {
                    code.Blank();
                }

                Requests(code, rootNamespace, requests);
            }

            code.Close();
            return code.ToString();
        }

        public static List<ValueDef> RequestEvents(PackPlan plan)
        {
            var events = new List<ValueDef>();
            var all = plan.Schema.Events;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].Starts.Length > 0)
                {
                    events.Add(all[i]);
                }
            }

            return events;
        }

        static void Requests(CodeWriter code, string rootNamespace, List<ValueDef> events)
        {
            code.Open("public sealed class TimelineRequests");
            code.Line("static readonly int[] Raised =");
            code.Block();
            for (var i = 0; i < events.Count; i++)
            {
                code.Line("(int)global::" + rootNamespace + ".Event." + Names.Identifier(events[i].Name) + ",");
            }

            code.Close("};");
            code.Blank();
            code.Line("static readonly string[] Timelines =");
            code.Block();
            for (var i = 0; i < events.Count; i++)
            {
                code.Line("\"" + events[i].Starts + "\",");
            }

            code.Close("};");
            code.Blank();
            code.Line("readonly ushort[] resolved = new ushort[Raised.Length];");
            code.Blank();
            code.Open("public TimelineRequests(TimelineWorld world)");
            code.Open("for (var i = 0; i < Raised.Length; i++)");
            code.Line("resolved[i] = world.Find(Timelines[i]);");
            code.Close();
            code.Close();
            code.Blank();
            code.Open("public unsafe void Apply(PlaybackTable players, HostEvent* events, int count)");
            code.Open("for (var at = 0; at < count; at++)");
            code.Open("for (var i = 0; i < Raised.Length; i++)");
            code.Open("if (events[at].Event == Raised[i] && resolved[i] != 0)");
            code.Open("if (events[at].Entered)");
            code.Line("players.Start(events[at].Slot, resolved[i]);");
            code.Close();
            code.Open("else");
            code.Line("players.Stop(events[at].Slot, resolved[i]);");
            code.Close();
            code.Close();
            code.Close();
            code.Close();
            code.Close();
            code.Close();
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
