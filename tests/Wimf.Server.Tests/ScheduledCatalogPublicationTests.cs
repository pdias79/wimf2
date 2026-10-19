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
    [Fact]
    public void UnsupportedNamesRemainReportedButDoNotBlockCompleteRepresentableCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-staged-names-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var target = Path.Combine(temp, "index.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "valid.txt"), "valid");
            File.WriteAllText(Path.Combine(root, "unsupported;name.txt"), "legacy cannot encode");
            var skipped = new List<string>();
            var count = ScheduledCatalogPublication.Refresh(root, target, onSkipped: skipped.Add);
            Assert.Equal(1, count);
            Assert.Single(skipped);
            Assert.StartsWith("Skipped unsupported file name:", skipped[0]);
            Assert.Single(LegacyCatalogReader.ReadFile(target).EnumerateEntries());
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public void RootDisappearingAfterScanPreservesPreviousCatalog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wimf-root-lost-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var target = Path.Combine(temp, "index.txt.gz");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "old.txt"), "old");
            ScheduledCatalogPublication.Refresh(root, target);
            var before = File.ReadAllBytes(target);
            Assert.Throws<InvalidDataException>(() =>
                ScheduledCatalogPublication.Refresh(root, target,
                    onMetrics: _ => Directory.Delete(root, recursive: true)));
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Empty(Directory.GetFiles(temp, ".wimf-staging-*"));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
    }

}
