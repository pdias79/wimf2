using System.Text;
using Wimf.Catalog;
using Wimf.Server;
using Xunit;

namespace Wimf.Server.Tests;

public sealed class LocalFileActionsTests
{
    private static Wimf.Catalog.Catalog Catalog(string root, string rows)
    {
        var content = $"[WIMF Catalog]\nTest\n{root}\n0\ntxt\n{rows}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return LegacyCatalogReader.Read(stream, compressed: false);
    }

    [Fact]
    public void ResolveRequiresMatchingCatalogIdentityAndExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "hello.txt");
            File.WriteAllText(path, "example");
            var catalog = Catalog(root, "hello.txt;txt;7;0;2\n");
            var indexed = catalog.GetFullPath(1);
            Assert.Equal(path, LocalFileActions.Resolve([catalog], 0, 1, indexed));
            Assert.Throws<ArgumentException>(() => LocalFileActions.Resolve([catalog], -1, 1, indexed));
            Assert.Throws<ArgumentException>(() => LocalFileActions.Resolve([catalog], 1, 1, indexed));
            Assert.Throws<ArgumentException>(() => LocalFileActions.Resolve([catalog], 0, 999, indexed));
            Assert.Throws<InvalidOperationException>(() =>
                LocalFileActions.Resolve([catalog], 0, 1, indexed + ".exe"));
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() =>
                LocalFileActions.Resolve([catalog], 0, 1, indexed));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveAllowsExistingDirectoryAndRejectsUnsafeCatalogNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = Catalog(root, "folder;folder;0;0;2\n../escape.txt;txt;1;0;2\n");
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            Assert.Equal(Path.Combine(root, "folder"), LocalFileActions.Resolve([catalog], 0, 1, catalog.GetFullPath(1)));
            Assert.Throws<InvalidDataException>(() => LocalFileActions.Resolve([catalog], 0, 2, catalog.GetFullPath(2)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DirectoryResolutionRejectsStaleAndMissingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "wimf-directory-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, "documents");
            Directory.CreateDirectory(folder);
            var catalog = Catalog(root, "documents;folder;0;0;2\n");
            Assert.Equal(folder, LocalFileActions.Resolve([catalog], 0, 1, catalog.GetFullPath(1)));
            Assert.Throws<InvalidOperationException>(() =>
                LocalFileActions.Resolve([catalog], 0, 1, catalog.GetFullPath(1) + "-stale"));
            Directory.Delete(folder);
            Assert.Throws<FileNotFoundException>(() =>
                LocalFileActions.Resolve([catalog], 0, 1, catalog.GetFullPath(1)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopyIsUnsupportedOnNonWindowsHosts()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() =>
            LocalFileActions.Execute("copy", "/tmp/example.txt"));
    }
}
