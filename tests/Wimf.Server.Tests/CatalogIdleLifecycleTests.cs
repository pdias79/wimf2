using System.Text;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class CatalogIdleLifecycleTests
{

    [Fact]
    public async Task MetadataPollingDoesNotPreventIdleUnload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-metadata-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.txt");
            File.WriteAllText(source,
                "[WIMF Catalog]\\nExample\\n/root\\n0\\ntxt\\na.txt;txt;1;0;1\\n".Replace("\\\\n", "\\n"), Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], Path.Combine(dir, "cache"));
            await using var lifecycle = new CatalogIdleLifecycle(store,
                TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(20));
            await lifecycle.TouchAsync();
            Assert.Single(store.Snapshot);

            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!store.IsUnloaded)
            {
                // Matches the catalog-list endpoint: does not count as user activity.
                await lifecycle.WaitForPendingReloadAsync(limit.Token);
                await Task.Delay(40, limit.Token);
            }
            Assert.Empty(store.Snapshot);
            // Polling after unloading must not trigger a new catalog read.
            await lifecycle.WaitForPendingReloadAsync(limit.Token);
            Assert.True(store.IsUnloaded);
            Assert.Empty(store.Snapshot);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task IdleUnloadAndTypingReloadRemainTransparentToSearch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wimf-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.txt");
            File.WriteAllText(source,
                "[WIMF Catalog]\nExample\n/root\n0\ntxt\na.txt;txt;1;0;1\n", Encoding.UTF8);
            var store = new LiveCatalogStore([], [source], Path.Combine(dir, "cache"));
            await store.RefreshAsync();
            await using var lifecycle = new CatalogIdleLifecycle(store,
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20));
            using (await lifecycle.BeginSearchAsync(CancellationToken.None))
            {
                await Task.Delay(220);
                Assert.False(store.IsUnloaded); // in-flight searches cannot be interrupted
            }

            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!store.IsUnloaded)
            {
                await Task.Delay(25, limit.Token);
            }
            Assert.Empty(store.Snapshot);
            await lifecycle.TouchAsync(); // an edit, including clearing to empty
            Assert.False(store.IsUnloaded);
            Assert.Equal("Example", Assert.Single(store.Snapshot).Header.Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
