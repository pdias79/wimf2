using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class SharedCatalogLockTests
{
    [Fact]
    public void OnlyOneHolderMayAcquireLockAndReleaseAllowsAnother()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = Path.Combine(directory, "database.txt.gz");
            using (var first = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(first);
                Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            }
            using (var second = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(second);
                Assert.True(File.Exists(catalog + ".wimf.lock"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void OldMarkerDoesNotOverrideAnActiveOperatingSystemLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = Path.Combine(directory, "database.txt.gz");
            using (var first = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)))
            {
                Assert.NotNull(first);
                File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddHours(-2));
                Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }


    [Fact]
    public async Task TwoIndependentProcessesRespectExclusivityAndCrashRelease()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-process-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var catalog = Path.Combine(directory, "shared.txt.gz");
        System.Diagnostics.Process? owner = null;
        try
        {
            owner = StartProbe("hold", catalog);
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var ready = await owner.StandardOutput.ReadLineAsync(readyTimeout.Token);
            Assert.Equal("ACQUIRED", ready);
            Assert.False(owner.HasExited);

            using (var contender = StartProbe("once", catalog))
            {
                await contender.WaitForExitAsync(readyTimeout.Token);
                Assert.Equal(2, contender.ExitCode);
            }
            File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddHours(-2));
            using (var staleContender = StartProbe("once", catalog))
            {
                await staleContender.WaitForExitAsync(readyTimeout.Token);
                Assert.Equal(2, staleContender.ExitCode);
            }

            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync(readyTimeout.Token);

            using var successor = StartProbe("once", catalog);
            await successor.WaitForExitAsync(readyTimeout.Token);
            Assert.Equal(0, successor.ExitCode);
            Assert.True(File.Exists(catalog + ".wimf.lock"));
        }
        finally
        {
            if (owner is { HasExited: false }) owner.Kill(entireProcessTree: true);
            owner?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static System.Diagnostics.Process StartProbe(string action, string catalog)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Wimf.Server.Tests.runtimeconfig.json"));
        start.ArgumentList.Add(typeof(Wimf.LockProbe.ProbeMarker).Assembly.Location);
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(catalog);
        return System.Diagnostics.Process.Start(start)!;
    }

    [Fact]
    public void InvalidExpiryIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SharedCatalogLock.TryAcquire(path, TimeSpan.Zero));
    }
}
