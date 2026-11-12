using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SvV0
{
    public static unsafe class StatFoldHost
    {
        public static string Run(string host, string outPath)
        {
            var n = StatFoldSets.SetCount;
            var bases = (float*)Marshal.AllocHGlobal(n * 4);
            var counts = (byte*)Marshal.AllocHGlobal(n);
            var entries = (ProbeStatEntry*)Marshal.AllocHGlobal(n * StatFoldSets.MaxEntries * sizeof(ProbeStatEntry));
            var results = (uint*)Marshal.AllocHGlobal(n * 4);
            StatFoldSets.Fill(bases, counts, entries);
            StatFoldSets.FoldAll(bases, counts, entries, results);
            if (outPath != null)
            {
                var bytes = new byte[n * 4];
                Marshal.Copy((IntPtr)results, bytes, 0, bytes.Length);
                File.WriteAllBytes(outPath, bytes);
            }
            var line = Report(host, results);
            Marshal.FreeHGlobal((IntPtr)bases);
            Marshal.FreeHGlobal((IntPtr)counts);
            Marshal.FreeHGlobal((IntPtr)entries);
            Marshal.FreeHGlobal((IntPtr)results);
            return line;
        }

        public static string Report(string host, uint* results)
        {
            var raw = StatFoldSets.Digest(results);
            var nan = 0;
            for (var i = 0; i < StatFoldSets.SetCount; i++)
            {
                if ((results[i] & 0x7F800000u) == 0x7F800000u && (results[i] & 0x7FFFFFu) != 0)
                {
                    nan++;
                    results[i] = 0x7FC00000u;
                }
            }
            var canonical = StatFoldSets.Digest(results);
            return "statfold host=" + host + " sets=" + StatFoldSets.SetCount + " raw=" + raw.ToString("X16") + " canonical=" + canonical.ToString("X16") + " nan=" + nan;
        }

        public static int Main(string[] args)
        {
            var line = Run(args.Length > 0 ? args[0] : "unknown", args.Length > 1 ? args[1] : null);
            Console.WriteLine(line);
            return 0;
        }
    }
}
