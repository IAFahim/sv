using Sv.Kernels.NorthStar3.Glue;
using Sv.Rules;
using Tl;
using Tl.Gen.Tlb;
using Xunit;

namespace Sv.NorthStar3
{
    public sealed class UnityLegTests
    {
        static string Root
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "tests", "Sv.Kernels.NorthStar3", "pack")))
                {
                    folder = folder.Parent;
                }

                return folder!.FullName;
            }
        }

        static string UnityGlueFolder => Path.Combine(Root, "packages", "com.sv.unity.v4", "Tests", "Tl", "Glue");

        static bool Rewrite => Environment.GetEnvironmentVariable("SV_UPDATE_TWIN") == "1";

        [Fact]
        public void The_unity_glue_equals_the_emitter_output()
        {
            var plan = PackCompiler.Compile(Sv.Pack.PackFolder.Read(Path.Combine(Root, "tests", "Sv.Kernels.NorthStar3", "pack"))).Value!;
            var emitted = Sv.Rules.Emit.TlGlueEmitter.Emit(plan, "Sv.Kernels.NorthStar3");
            var unityGlue = Path.Combine(UnityGlueFolder, "Glue.g.cs");
            if (Rewrite)
            {
                File.WriteAllText(unityGlue, emitted);
                return;
            }

            Assert.Equal(File.ReadAllText(unityGlue).Replace("\r\n", "\n"), emitted);
        }

        [Fact]
        public void The_unity_library_is_the_library_the_dotnet_rig_plays()
        {
            var unityLibrary = Path.Combine(UnityGlueFolder, "game.bytes");
            if (Rewrite)
            {
                File.WriteAllBytes(unityLibrary, Rig.Library());
                return;
            }

            Assert.Equal(Rig.Library(), File.ReadAllBytes(unityLibrary));
        }

        [Fact]
        public void The_timeline_fixture_bakes_to_the_library_the_dotnet_rig_plays()
        {
            var poisonJson = File.ReadAllBytes(Path.Combine(UnityGlueFolder, "Timelines~", "poison.json"));
            var manifest = LibraryBake.Manifest(new List<string> { "poison" }, new List<string>(), 60);
            var resolver = new BakerAssemblyResolver(new[] { typeof(PoisonClip).Assembly.Location });
            var poisonTlb = TimelineBaker.BakeJson(poisonJson, resolver, false, manifest);
            var kinds = LibraryBake.Kinds(new[] { typeof(PoisonClip).Assembly });
            var poison = new LibraryPart { Name = "poison", Tlb = poisonTlb, Kinds = LibraryBake.KindPayload("poison", poisonTlb, kinds), Next = (ushort)LibraryAssembler.NextOf(poisonTlb) };
            var fixtureLibrary = LibraryAssembler.Assemble(new List<LibraryPart> { poison }, 60, LibrarySettings.Default);
            Assert.Equal(Rig.Library(), fixtureLibrary);
        }
    }
}
