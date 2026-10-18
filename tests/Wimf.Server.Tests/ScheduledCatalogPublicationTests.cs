using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public class ScheduledCatalogPublicationTests
{
    [Fact]
    public void PublishesValidatedFullScanAndPreservesPreviousOnCancellation()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-staged-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var target = Path.Combine(temp, "catalog.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "old.txt"), "old");
            Assert.Equal(1, ScheduledCatalogPublication.Refresh(root, target));
            var original = File.ReadAllBytes(target);
            File.WriteAllText(Path.Combine(root, "new.txt"), "new");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                ScheduledCatalogPublication.Refresh(root, target, cancellationToken: cancellation.Token));
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(2, ScheduledCatalogPublication.Refresh(root, target));
            Assert.Equal(2, LegacyCatalogReader.ReadFile(target).EnumerateEntries().Count());
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }
}
