using Wimf.Server;

// Dedicated child process: locks cannot be simulated by two threads in one process.
if (args.Length != 3 || args[0] != "hold")
    return 64;
try
{
    using var held = SharedCatalogLock.TryAcquire(args[1], TimeSpan.FromMinutes(30));
    if (held is null) return 2;
    // Signal acquisition only after the OS lock and marker are published.
    Console.WriteLine("ACQUIRED");
    Console.Out.Flush();
    var command = Console.ReadLine();
    if (command == "crash")
        Environment.FailFast("Intentional lock recovery test");
    return command == "release" ? 0 : 3;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 4;
}
