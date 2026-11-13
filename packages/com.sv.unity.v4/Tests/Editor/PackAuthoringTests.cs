using System.IO;
using NUnit.Framework;
using Sv.Rules;
using Sv.Unity.Editor;
using UnityEditor.PackageManager;

namespace Sv.Unity.Tests
{
    public sealed class PackAuthoringTests
    {
        static string PackageRoot => PackageInfo.FindForAssembly(typeof(PackAuthoringTests).Assembly)!.resolvedPath;

        static string PackFolder(string pack) => Path.GetFullPath(Path.Combine(PackageRoot, "..", "..", "packs", pack));

        static string Normalize(string text) => text.Replace("\r\n", "\n");

        [TestCase("reference", "Reference")]
        [TestCase("lego", "Lego")]
        public void Editor_output_equals_the_committed_generated_files(string pack, string folder)
        {
            var authoring = new SvPackAuthoring(PackFolder(pack));
            var files = authoring.Generate("Sv.Kernels." + folder, out var result);
            Assert.That(files, Is.Not.Null, string.Join("\n", SvPackAuthoring.Lines(result)));
            var committed = Path.Combine(PackageRoot, "Tests", "Packs", folder);
            foreach (var file in files!)
            {
                Assert.That(Normalize(file.Value), Is.EqualTo(Normalize(File.ReadAllText(Path.Combine(committed, file.Key)))), file.Key + " differs from the committed copy");
            }
        }

        [Test]
        public void Validate_reports_a_broken_rule_file_with_its_diagnostics()
        {
            var authoring = new SvPackAuthoring(PackFolder("reference"));
            Assert.That(authoring.Validate().Value, Is.Not.Null);
            authoring.Rules = authoring.Rules.Replace("\"rows\"", "\"rowz\"");
            var result = authoring.Validate();
            Assert.That(result.Value, Is.Null);
            Assert.That(new System.Collections.Generic.List<string>(SvPackAuthoring.Lines(result)), Is.Not.Empty);
        }

        [Test]
        public void Add_value_takes_the_next_lane_and_the_pack_still_compiles()
        {
            var authoring = new SvPackAuthoring(PackFolder("reference"));
            var before = authoring.Validate().Value!;
            authoring.AddValue("Armor", "intrinsic", "0");
            authoring.AddValue("Haste", "stat", "1.0");
            Assert.That(authoring.Values, Does.Contain("\"nextLane\":" + (before.Schema.NextLane + 2)));
            var after = authoring.Validate();
            Assert.That(after.Value, Is.Not.Null, string.Join("\n", SvPackAuthoring.Lines(after)));
            Assert.That(after.Value!.Schema.FindByName(ValueClass.Intrinsic, "Armor")!.Id, Is.EqualTo(before.Schema.NextLane));
            Assert.That(after.Value.Schema.FindByName(ValueClass.Stat, "Haste")!.Id, Is.EqualTo(before.Schema.NextLane + 1));
            Assert.That(after.Value.Hash, Is.Not.EqualTo(before.Hash));
        }

        [Test]
        public void Save_round_trips_through_a_temp_folder()
        {
            var temp = Path.Combine(Path.GetTempPath(), "sv-authoring-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var name in SvPackAuthoring.FileNames)
                {
                    File.Copy(Path.Combine(PackFolder("reference"), name), Path.Combine(temp, name));
                }

                var authoring = new SvPackAuthoring(temp);
                authoring.AddValue("Armor", "intrinsic", "0");
                authoring.Save();
                Assert.That(new SvPackAuthoring(temp).Values, Does.Contain("\"name\":\"Armor\""));
                Assert.That(new SvPackAuthoring(PackFolder("reference")).Values, Does.Not.Contain("Armor"));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
