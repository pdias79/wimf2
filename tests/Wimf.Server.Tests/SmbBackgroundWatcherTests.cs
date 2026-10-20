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

    [Fact]
    public void FailedWatcherRecoversAndKeepsTheSameJournal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-recover-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "share");
        var state = Path.Combine(directory, "state");
        Directory.CreateDirectory(root);
        try
        {
            using var watcher = new SmbBackgroundWatcher(root, state);
            watcher.Start();
            var journal = watcher.JournalPath;
            var before = new FileInfo(journal).Length;
            var method = typeof(SmbBackgroundWatcher).GetMethod("OnError",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var underlyingWatcher = typeof(SmbBackgroundWatcher).GetField("_watcher",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(watcher);
            method.Invoke(watcher, [underlyingWatcher, new ErrorEventArgs(new IOException("simulated loss"))]);
            Assert.True(watcher.WatchFailed);
            Assert.True(watcher.TryRecover());
            Assert.False(watcher.WatchFailed);
            Assert.Equal(journal, watcher.JournalPath);
            Assert.True(new FileInfo(journal).Length > before);
            Assert.False(watcher.TryRecover());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedJournalMarkerDisablesWatcherInsteadOfPretendingToRecover()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-marker-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "share");
        var state = Path.Combine(directory, "state");
        Directory.CreateDirectory(root);
        try
        {
            using var watcher = new SmbBackgroundWatcher(root, state);
            var stream = (FileStream)typeof(SmbBackgroundWatcher)
                .GetField("_stream", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.GetValue(watcher)!;
            stream.Dispose(); // Simulate persistent journal storage becoming unavailable.
            watcher.Start();
            Assert.True(watcher.WatchFailed);
            Assert.True(watcher.NeedsFullScan);
            Assert.False(watcher.TryRecover());
            Assert.True(watcher.WatchFailed);
            Assert.False(watcher.Recovered);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RecoveryDoesNotAllowIncrementalUntilReconciliationAfterTheGap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-gap-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "share");
        var state = Path.Combine(directory, "state");
        Directory.CreateDirectory(root);
        try
        {
            using var watcher = new SmbBackgroundWatcher(root, state);
            watcher.Start();
            Assert.True(watcher.CanPublishIncremental);
            var before = watcher.ContinuityEpoch;
            var onError = typeof(SmbBackgroundWatcher).GetMethod("OnError",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var underlying = typeof(SmbBackgroundWatcher).GetField("_watcher",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(watcher);
            onError.Invoke(watcher, [underlying, new ErrorEventArgs(new IOException("simulated disconnect"))]);
            Assert.False(watcher.CanPublishIncremental);
            Assert.True(watcher.TryRecover());
            Assert.False(watcher.WatchFailed);
            Assert.False(watcher.CanPublishIncremental);
            // Full scan that started before the disconnect may not clear the gap.
            watcher.MarkFullScanReconciled(before);
            Assert.False(watcher.CanPublishIncremental);
            watcher.MarkFullScanReconciled(watcher.ContinuityEpoch);
            Assert.True(watcher.CanPublishIncremental);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnavailableRootCannotStartWatcherUntilDirectoryReturns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wimf-recover-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "share");
        var state = Path.Combine(directory, "state");
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => new SmbBackgroundWatcher(root, state));
            Directory.CreateDirectory(root);
            using var watcher = new SmbBackgroundWatcher(root, state);
            watcher.Start();
            Assert.False(watcher.WatchFailed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
