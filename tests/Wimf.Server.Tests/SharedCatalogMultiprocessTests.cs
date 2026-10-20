using System.Diagnostics;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class SharedCatalogMultiprocessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentProcessCannotStealActiveLockAndReleasesAfterExit(bool crash)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-process-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? child = null;
        try
        {
            var catalog = Path.Combine(directory, "catalog.txt.gz");
            File.WriteAllText(catalog, "last valid catalog");
            var original = File.ReadAllBytes(catalog);
            child = StartProbe(catalog);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.Equal("ACQUIRED", await child.StandardOutput.ReadLineAsync(deadline.Token));
            Assert.False(child.HasExited);
            Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));

            // A 31-minute-old marker cannot evict a live OS lock, even on another process.
            File.SetLastWriteTimeUtc(catalog + ".wimf.lock", DateTime.UtcNow.AddMinutes(-31));
            Assert.Null(SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30)));
            Assert.Equal(original, File.ReadAllBytes(catalog));

            await child.StandardInput.WriteLineAsync(crash ? "crash" : "release");
            await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync(deadline.Token);
            if (crash) Assert.NotEqual(0, child.ExitCode);
            else Assert.Equal(0, child.ExitCode);

            using var recovered = SharedCatalogLock.TryAcquire(catalog, TimeSpan.FromMinutes(30));
            Assert.NotNull(recovered);
            Assert.Equal(original, File.ReadAllBytes(catalog));
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Process StartProbe(string catalog)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Wimf.slnx")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var project = Path.Combine(root.FullName, "tests", "Wimf.LockProbe", "Wimf.LockProbe.csproj");
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.FullName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(project);
        info.ArgumentList.Add("--configuration");
        info.ArgumentList.Add("Release");
        info.ArgumentList.Add("--no-build");
        info.ArgumentList.Add("--");
        info.ArgumentList.Add("hold");
        info.ArgumentList.Add(catalog);
        info.ArgumentList.Add("unused");
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start lock probe.");
    }
}
