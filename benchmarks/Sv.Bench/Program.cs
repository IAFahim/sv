using System;

internal static class Program
{
    static int Main(string[] args)
    {
        var scenario = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "all";
        var ms = 2000;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--ms" && i + 1 < args.Length)
            {
                ms = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var failures = SvV3Bench.V3.Run(scenario, ms) + SvV3Bench.Lego1m.Run(scenario, ms) + SvV3Bench.Scale.Run(scenario, ms);
        Console.WriteLine(failures == 0 ? "all scenarios ok" : failures + " scenario(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
