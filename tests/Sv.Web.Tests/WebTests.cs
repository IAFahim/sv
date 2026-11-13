using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sv.Rules;
using Sv.Rules.Emit;
using Sv.Web;
using Xunit;

namespace WebTests
{
    public sealed class WebTests
    {
        const string WorkedExampleDigest = "E0F445186E2C3672";
        const string ReferenceHash = "BD70627B1C671888";

        static string Root
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "reference")))
                {
                    folder = folder.Parent;
                }

                return folder!.FullName;
            }
        }

        static string Pack(string file) => File.ReadAllText(Path.Combine(Root, "packs", "reference", file));

        static string Scenario => File.ReadAllText(Path.Combine(Root, "packs", "reference", "scenarios", "worked_example.scenario.json"));

        static JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement;

        static string Check(string values) => SvWeb.Check(values, Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json"));

        [Fact]
        public void The_reference_pack_checks_clean_with_its_pinned_hash()
        {
            var result = Parse(Check(Pack("values.sv.json")));
            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Equal(ReferenceHash, result.GetProperty("hash").GetString());
            Assert.DoesNotContain(result.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("severity").GetString() == "error");
        }

        [Fact]
        public void The_worked_example_simulates_to_its_pinned_digest_with_a_trace_per_frame()
        {
            var result = Parse(SvWeb.Simulate(Pack("values.sv.json"), Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json"), Scenario));
            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.True(result.GetProperty("passed").GetBoolean());
            Assert.Equal(WorkedExampleDigest, result.GetProperty("digest").GetString());
            Assert.Equal(2, result.GetProperty("frames").GetArrayLength());
            Assert.All(result.GetProperty("frames").EnumerateArray(), frame => Assert.True(frame.GetProperty("trace").GetArrayLength() > 0));
            var names = result.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
            Assert.Equal(new[] { "P", "S1", "S2", "S3" }, names);
        }

        [Fact]
        public void Canonical_output_is_stable_and_keeps_the_hash()
        {
            var first = Parse(SvWeb.Canonical(Pack("values.sv.json"), Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json")));
            Assert.True(first.GetProperty("ok").GetBoolean());
            Assert.Equal(ReferenceHash, first.GetProperty("hash").GetString());
            var second = Parse(SvWeb.Canonical(first.GetProperty("values").GetString()!, first.GetProperty("rules").GetString()!, first.GetProperty("cards").GetString()!, first.GetProperty("templates").GetString()!));
            Assert.Equal(ReferenceHash, second.GetProperty("hash").GetString());
            foreach (var key in new[] { "values", "rules", "cards", "templates" })
            {
                Assert.Equal(first.GetProperty(key).GetString(), second.GetProperty(key).GetString());
            }
        }

        [Fact]
        public void A_broken_pack_returns_diagnostics_and_no_hash()
        {
            var result = Parse(Check(Pack("values.sv.json").Replace("\"class\":\"stat\"", "\"class\":\"nonsense\"")));
            Assert.False(result.GetProperty("ok").GetBoolean());
            Assert.Equal(string.Empty, result.GetProperty("hash").GetString());
            Assert.NotEmpty(result.GetProperty("diagnostics").EnumerateArray());
        }

        [Fact]
        public void A_broken_scenario_returns_its_error_instead_of_throwing()
        {
            var result = Parse(SvWeb.Simulate(Pack("values.sv.json"), Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json"), Scenario.Replace("\"frames\":2", "\"frames\":\"two\"")));
            Assert.False(result.GetProperty("ok").GetBoolean());
            Assert.Contains("SV0800", result.GetProperty("scenarioError").GetString());
        }

        [Fact]
        public void An_edited_pack_generates_the_same_unity_files_from_the_browsers_canonical_output()
        {
            var values = Pack("values.sv.json").Replace("\"nextLane\":21", "\"nextLane\":22")
                .Replace("{\"id\":20,\"name\":\"ChargeWard\",\"class\":\"intrinsic\",\"default\":0},", "{\"id\":20,\"name\":\"ChargeWard\",\"class\":\"intrinsic\",\"default\":0},\n{\"id\":21,\"name\":\"Armor\",\"class\":\"intrinsic\",\"default\":0},");
            var raw = PackCompiler.Compile(new PackSource(values, Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json")));
            Assert.NotNull(raw.Value);
            var canonical = Parse(SvWeb.Canonical(values, Pack("rules.sv.json"), Pack("cards.sv.json"), Pack("templates.sv.json")));
            var again = PackCompiler.Compile(new PackSource(canonical.GetProperty("values").GetString()!, canonical.GetProperty("rules").GetString()!, canonical.GetProperty("cards").GetString()!, canonical.GetProperty("templates").GetString()!));
            Assert.NotNull(again.Value);
            Assert.Equal(raw.Value!.Hash, again.Value!.Hash);
            var fromRaw = HostEmitter.Unity(raw.Value, "Sv.Kernels.Edited");
            var fromCanonical = HostEmitter.Unity(again.Value, "Sv.Kernels.Edited");
            Assert.Equal(fromRaw.Count, fromCanonical.Count);
            for (var i = 0; i < fromRaw.Count; i++)
            {
                Assert.Equal(fromRaw[i].Key, fromCanonical[i].Key);
                if (fromRaw[i].Key != "Rules.map.txt")
                {
                    Assert.True(fromRaw[i].Value == fromCanonical[i].Value, fromRaw[i].Key + " differs");
                }
            }

            var second = Parse(SvWeb.Canonical(canonical.GetProperty("values").GetString()!, canonical.GetProperty("rules").GetString()!, canonical.GetProperty("cards").GetString()!, canonical.GetProperty("templates").GetString()!));
            var fixedPoint = PackCompiler.Compile(new PackSource(second.GetProperty("values").GetString()!, second.GetProperty("rules").GetString()!, second.GetProperty("cards").GetString()!, second.GetProperty("templates").GetString()!));
            var again2 = HostEmitter.Unity(fixedPoint.Value!, "Sv.Kernels.Edited");
            for (var i = 0; i < fromCanonical.Count; i++)
            {
                Assert.True(fromCanonical[i].Value == again2[i].Value, fromCanonical[i].Key + " is not a fixed point");
            }
        }
    }
}
