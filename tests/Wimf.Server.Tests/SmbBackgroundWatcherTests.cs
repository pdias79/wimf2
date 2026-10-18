using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class SmbBackgroundWatcherTests
{
    [Fact]
    public void NewWatcherAlwaysRequiresReconciliationAndCanRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-smb-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "share");
        var state = Path.Combine(directory, "state");
        try
        {
            Directory.CreateDirectory(root);
            using (var watcher = new SmbBackgroundWatcher(root, state))
            {
                Assert.True(watcher.NeedsFullScan);
                Assert.Empty(watcher.AffectedDirectories);
            }
            using (var restarted = new SmbBackgroundWatcher(root, state))
                Assert.True(restarted.NeedsFullScan);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
