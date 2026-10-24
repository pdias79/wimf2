using System.Diagnostics;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class SharedCatalogMultiprocessTests
{
    [Fact]
    public async Task IndependentProcessCannotStealLockAndCrashReleasesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-process-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? owner = null;
        try
        {
            var catalog = Path.Combine(directory, "catalog.txt.gz");
            File.WriteAllText(catalog, "last valid catalog");
            var original = File.ReadAllBytes(catalog);
            owner = StartProbe("hold", catalog);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var ready = await owner.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.True(ready == "ACQUIRED",
                $"Probe failed to acquire lock. stdout={ready ?? "<end>"} exitCode={(owner.HasExited ? owner.ExitCode : -1)}");
            Assert.False(owner.HasExited);

            using (var contender = StartProbe("once", catalog))
            {
                await contender.WaitForExitAsync(deadline.Token);
                Assert.Equal(2, contender.ExitCode);
            }
            File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddMinutes(-31));
            using (var contender = StartProbe("once", catalog))
            {
                await contender.WaitForExitAsync(deadline.Token);
                Assert.Equal(2, contender.ExitCode);
            }
            Assert.Equal(original, File.ReadAllBytes(catalog));

            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync(deadline.Token);
            using var successor = StartProbe("once", catalog);
            await successor.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, successor.ExitCode);
            Assert.Equal(original, File.ReadAllBytes(catalog));
        }
        finally
        {
            if (owner is { HasExited: false }) owner.Kill(entireProcessTree: true);
            owner?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Process StartProbe(string mode, string catalog)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Wimf.Server.Tests.runtimeconfig.json"));
        start.ArgumentList.Add(typeof(Wimf.LockProbe.ProbeMarker).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(catalog);
        return Process.Start(start)!;
    }
}
