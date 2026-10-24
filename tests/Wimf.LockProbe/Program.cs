using Wimf.Server;

namespace Wimf.LockProbe;

public static class ProbeMarker { }

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("hold" or "once"))
            return 64;
        using var held = SharedCatalogLock.TryAcquire(args[1], TimeSpan.FromMinutes(30));
        if (held is null) return 2;
        Console.WriteLine("ACQUIRED");
        Console.Out.Flush();
        if (args[0] == "hold")
            Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
