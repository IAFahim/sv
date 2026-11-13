using System;
using System.Collections.Generic;
using System.Globalization;
using Sv.Pack;
using Sv.Rules;
using Ref = Sv.Rules.Reference;

namespace Sv.Web
{
    public static class SvWeb
    {
        public static string Check(string values, string rules, string cards, string templates)
        {
            var compiled = PackCompiler.Compile(new PackSource(values, rules, cards, templates));
            var json = new JsonWriter();
            json.BeginObject();
            json.Key("ok").Bool(compiled.Value != null).KeyDone();
            json.Key("hash").String(compiled.Value == null ? string.Empty : ScenarioRunner.Hex(compiled.Value.Hash)).KeyDone();
            Diagnostics(json, compiled.Diagnostics);
            json.EndObject();
            return json.ToString();
        }

        public static string Canonical(string values, string rules, string cards, string templates)
        {
            var compiled = PackCompiler.Compile(new PackSource(values, rules, cards, templates));
            var json = new JsonWriter();
            json.BeginObject();
            json.Key("ok").Bool(compiled.Value != null).KeyDone();
            if (compiled.Value != null)
            {
                var canonical = PackWriter.Write(compiled.Value.Pack);
                json.Key("hash").String(ScenarioRunner.Hex(compiled.Value.Hash)).KeyDone();
                json.Key("values").String(canonical.Values).KeyDone();
                json.Key("rules").String(canonical.Rules).KeyDone();
                json.Key("cards").String(canonical.Cards).KeyDone();
                json.Key("templates").String(canonical.Templates).KeyDone();
            }

            Diagnostics(json, compiled.Diagnostics);
            json.EndObject();
            return json.ToString();
        }

        public static string Simulate(string values, string rules, string cards, string templates, string scenarioJson)
        {
            var compiled = PackCompiler.Compile(new PackSource(values, rules, cards, templates));
            var json = new JsonWriter();
            json.BeginObject();
            if (compiled.Value == null)
            {
                json.Key("ok").Bool(false).KeyDone();
                Diagnostics(json, compiled.Diagnostics);
                json.EndObject();
                return json.ToString();
            }

            Scenario scenario;
            try
            {
                scenario = ScenarioReader.Read("scenario.json", scenarioJson, compiled.Value.Schema);
            }
            catch (ScenarioError error)
            {
                json.Key("ok").Bool(false).KeyDone();
                json.Key("scenarioError").String(error.Message).KeyDone();
                Diagnostics(json, compiled.Diagnostics);
                json.EndObject();
                return json.ToString();
            }

            var traces = new List<Ref.Trace>();
            var outcome = ScenarioRunner.Reference(compiled.Value, scenario, frame =>
            {
                var trace = new Ref.Trace();
                traces.Add(trace);
                return trace;
            });
            json.Key("ok").Bool(true).KeyDone();
            json.Key("passed").Bool(outcome.Passed).KeyDone();
            json.Key("hash").String(ScenarioRunner.Hex(compiled.Value.Hash)).KeyDone();
            json.Key("digest").String(ScenarioRunner.Hex(outcome.Digest)).KeyDone();
            json.Key("name").String(scenario.Name).KeyDone();
            json.Key("entities").BeginArray();
            for (var e = 0; e < scenario.Entities.Count; e++)
            {
                json.BeginObject().Key("name").String(scenario.Entities[e].Name).KeyDone().Key("slot").Int(scenario.Entities[e].Slot).KeyDone()
                    .Key("token").String(Ref.Trace.Slot(scenario.Entities[e].Slot)).KeyDone().EndObject();
            }

            json.EndArray().KeyDone();
            json.Key("frames").BeginArray();
            for (var f = 0; f < traces.Count; f++)
            {
                json.BeginObject().Key("frame").Int(f).KeyDone().Key("trace").BeginArray();
                var lines = traces[f].Lines;
                for (var i = 0; i < lines.Count; i++)
                {
                    json.String(lines[i]);
                }

                json.EndArray().KeyDone().EndObject();
            }

            json.EndArray().KeyDone();
            Strings(json, "lines", outcome.Lines);
            Strings(json, "failures", outcome.Failures);
            Diagnostics(json, compiled.Diagnostics);
            json.EndObject();
            return json.ToString();
        }

        static void Strings(JsonWriter json, string key, List<string> items)
        {
            json.Key(key).BeginArray();
            for (var i = 0; i < items.Count; i++)
            {
                json.String(items[i]);
            }

            json.EndArray().KeyDone();
        }

        static void Diagnostics(JsonWriter json, Diagnostic[] diagnostics)
        {
            json.Key("diagnostics").BeginArray();
            for (var i = 0; i < diagnostics.Length; i++)
            {
                var diagnostic = diagnostics[i];
                json.BeginObject()
                    .Key("file").String(diagnostic.File).KeyDone()
                    .Key("offset").Int(diagnostic.Offset).KeyDone()
                    .Key("severity").String(diagnostic.Severity == Severity.Error ? "error" : "warning").KeyDone()
                    .Key("code").String("SV" + ((int)diagnostic.Code).ToString("D4", CultureInfo.InvariantCulture)).KeyDone()
                    .Key("message").String(diagnostic.Message).KeyDone()
                    .EndObject();
            }

            json.EndArray().KeyDone();
        }
    }
}
