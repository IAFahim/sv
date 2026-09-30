using System;
using System.Diagnostics;

internal static unsafe class Program
{
    static int Main(string[] args)
    {
        var which = args.Length > 0 ? args[0] : "all";
        if (which is "all" or "million") MillionSample.Run();
        if (which is "all" or "frame") FrameSample.Run();
        if (which is "all" or "reconcile") ReconcileSample.Run();
        if (which is not ("all" or "million" or "frame" or "reconcile"))
        {
            Console.WriteLine("usage: dotnet run -c Release -- [million|frame|reconcile|all]");
            return 2;
        }

        return 0;
    }

    internal static (double Us, long Bytes) Measure(int iterations, Action run)
    {
        for (var warm = 0; warm < 3; warm++)
        {
            run();
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            run();
        }

        var us = (Stopwatch.GetTimestamp() - sw) / (double)iterations / Stopwatch.Frequency * 1_000_000.0;
        return (us, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
