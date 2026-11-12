using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Sv.Rules;

namespace Sv.Pack
{
    public readonly struct EmittedFile
    {
        public string Name { get; }

        public string Text { get; }

        public EmittedFile(string name, string text)
        {
            Name = name;
            Text = text;
        }
    }

    public static class EmitBridge
    {
        public const string EmitterType = "Sv.Rules.Emit.PackEmitter";
        public const string EmitterMethod = "Emit";

        public static bool TryEmit(PackPlan plan, string rootNamespace, List<EmittedFile> files, out string error)
        {
            var emitter = typeof(PackPlan).Assembly.GetType(EmitterType, false);
            var method = emitter?.GetMethod(EmitterMethod, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(PackPlan), typeof(string) }, null);
            if (method == null)
            {
                error = "the pack emitter " + EmitterType + "." + EmitterMethod + "(PackPlan, string) is not in Sv.Rules";
                return false;
            }

            if (method.Invoke(null, new object[] { plan, rootNamespace }) is not IEnumerable<KeyValuePair<string, string>> emitted)
            {
                error = EmitterType + "." + EmitterMethod + " returned no (file name, text) pairs";
                return false;
            }

            foreach (var file in emitted)
            {
                files.Add(new EmittedFile(file.Key, file.Value));
            }

            error = string.Empty;
            return true;
        }
    }

    public static class HostGlue
    {
        public const string FileName = "HostKernels.g.cs";
        public const string TypeName = "HostKernels";

        public static EmittedFile Emit(string rootNamespace)
        {
            var text = new StringBuilder();
            text.Append("using Sv;\n\n");
            text.Append("namespace ").Append(rootNamespace).Append('\n');
            text.Append("{\n");
            text.Append("    public unsafe struct ").Append(TypeName).Append(" : global::Sv.Host.IPackKernels\n");
            text.Append("    {\n");
            Line(text, "public ulong PackHash => Layout.PackHash;");
            Line(text, "public ulong CompiledAgainst => Layout.CompiledAgainst;");
            Line(text, "public int ScratchLanes => Kernels.ScratchLanes;");
            Line(text, "public int DeltaLanes => Kernels.DeltaLanes;");
            Line(text, "public int EventCount => Kernels.EventCount;");
            Line(text, "public int LanesWrittenPerChunk => Kernels.LanesWrittenPerChunk;");
            Line(text, "public int DeltaTarget(int deltaLane) => Kernels.DeltaTarget(deltaLane);");
            Line(text, "public int FloorStat(int deltaLane) => Kernels.FloorStat(deltaLane);");
            Line(text, "public int CeilingStat(int deltaLane) => Kernels.CeilingStat(deltaLane);");
            Line(text, "public void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling) => Kernels.Bounds(world, chunk, deltaLane, floor, ceiling);");
            Line(text, "public int EventId(int ordinal) => Kernels.EventId(ordinal);");
            Line(text, "public bool Forwards(int template) => Kernels.Forwards(template);");
            Line(text, "public int Forward(in WorldView world, int slot, int channel) => Kernels.Forward(world, slot, channel);");
            Line(text, "public ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, byte* records, ref EffectWriter next) => Kernels.Decide(world, chunk, capacity, effects, count, scratch, records, ref next);");
            Line(text, "public ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges) => Kernels.Events(world, ref writer, chunk, recheck, scratch, edges);");
            Line(text, "public void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next) => Kernels.EdgeEmits(world, chunk, capacity, edges, scratch, ref next);");
            text.Append("    }\n");
            text.Append("}\n");
            return new EmittedFile(FileName, text.ToString());
        }

        static void Line(StringBuilder text, string member) => text.Append("        ").Append(member).Append('\n');
    }

    public static class PackBinary
    {
        public const string FileName = "pack.svb";
        public static readonly byte[] Magic = { (byte)'S', (byte)'V', (byte)'B', (byte)'1' };

        public static byte[] Write(ContentPack pack, ulong hash)
        {
            var canonical = PackWriter.Write(pack);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
            {
                writer.Write(Magic);
                writer.Write(hash);
                Text(writer, canonical.Values);
                Text(writer, canonical.Rules);
                Text(writer, canonical.Cards);
                Text(writer, canonical.Templates);
            }

            return stream.ToArray();
        }

        static void Text(BinaryWriter writer, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static PackSource Read(byte[] image, out ulong hash)
        {
            using var reader = new BinaryReader(new MemoryStream(image), Encoding.UTF8);
            var magic = reader.ReadBytes(Magic.Length);
            for (var i = 0; i < Magic.Length; i++)
            {
                if (magic.Length != Magic.Length || magic[i] != Magic[i])
                {
                    throw new InvalidDataException("sv: not a pack.svb image");
                }
            }

            hash = reader.ReadUInt64();
            var values = Text(reader);
            var rules = Text(reader);
            var cards = Text(reader);
            var templates = Text(reader);
            return new PackSource(values, rules, cards, templates);
        }

        static string Text(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }
    }

    public static class PackFolder
    {
        public static PackSource Read(string folder)
        {
            var path = Path.GetFullPath(folder);
            if (File.Exists(path) && path.EndsWith(PackBinary.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return PackBinary.Read(File.ReadAllBytes(path), out _);
            }

            return new PackSource(
                File.ReadAllText(Path.Combine(path, PackSource.ValuesFile)),
                File.ReadAllText(Path.Combine(path, PackSource.RulesFile)),
                File.ReadAllText(Path.Combine(path, PackSource.CardsFile)),
                File.ReadAllText(Path.Combine(path, PackSource.TemplatesFile)));
        }

        public static string InputKey(PackSource source, string rootNamespace, string toolVersion)
        {
            var hash = PackHash.Of(source);
            var salt = 14695981039346656037ul;
            var tail = rootNamespace + "\n" + toolVersion;
            unchecked
            {
                for (var i = 0; i < tail.Length; i++)
                {
                    salt = (salt ^ tail[i]) * 1099511628211ul;
                }
            }

            return (hash ^ salt).ToString("X16", CultureInfo.InvariantCulture);
        }

        public static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path))
            {
                var old = File.ReadAllBytes(path);
                if (old.AsSpan().SequenceEqual(bytes))
                {
                    return false;
                }
            }

            File.WriteAllBytes(path, bytes);
            return true;
        }

        public static bool WriteIfChanged(string path, string text) =>
            WriteIfChanged(path, new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n")));
    }
}
