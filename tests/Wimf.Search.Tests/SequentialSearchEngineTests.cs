using System.Text;
using System.Text.RegularExpressions;
using Wimf.Catalog;
using Xunit;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Search.Tests;

public class SequentialSearchEngineTests
{
    private readonly SequentialSearchEngine _engine = new();

    private static FileCatalog Read(string records, string root = @"C:\Fixture")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            $"[WIMF Catalog]\nSynthetic\n{root}\n0\npdf txt\n{records}"));
        return LegacyCatalogReader.Read(stream, false);
    }

    [Fact]
    public void MatchesAllWordsIgnoringCaseAndOrderAndSkipsDirectories()
    {
        var catalog = Read("REPORT annual;folder;0;0;1\nAnnual Report.PDF;PDF;10;1;2\nreport.txt;txt;20;0;3\n");
        var result = Assert.Single(_engine.Search([catalog], new() { Text = " report\tANNUAL " }));
        Assert.Equal(2, result.Entry.Id);
        Assert.Same(catalog, result.Catalog);
        Assert.Equal(@"C:\Fixture\REPORT annual\Annual Report.PDF", result.FullPath);
    }

    [Fact]
    public void CombinesInclusiveExtensionSizeAndDateFilters()
    {
        var catalog = Read("a.pdf;pdf;10;0;2\nb.PDF;PDF;20;0;3\nc.txt;txt;15;0;2\nd.pdf;pdf;9;0;2\ne.pdf;pdf;21;0;2\nf.pdf;pdf;15;0;1\ng.pdf;pdf;15;0;4\n");
        var results = _engine.Search([catalog], new()
        {
            Extensions = [".PdF"], MinSize = 10, MaxSize = 20,
            ModifiedFrom = DateTimeOffset.UnixEpoch.AddDays(2),
            ModifiedTo = DateTimeOffset.UnixEpoch.AddDays(3)
        });
        Assert.Equal(new[] { 1, 2 }, results.Select(r => r.Entry.Id));
    }

    [Fact]
    public void SearchesMultipleCatalogsWithGlobalLimitAndStableOrder()
    {
        var first = Read("invalid\na.txt;txt;1;0;1\n");
        var second = Read("b.txt;txt;1;0;1\nc.txt;txt;1;0;1\n", "/synthetic");
        var results = _engine.Search([first, second], new() { MaxResults = 2 });
        Assert.Equal(new[] { "a.txt", "b.txt" }, results.Select(r => r.Entry.Name));
        Assert.Equal(2, results[0].Entry.Id);
        Assert.Same(second, results[1].Catalog);
        Assert.Equal("/synthetic/b.txt", results[1].FullPath);
    }

    [Fact]
    public void DoesNotReconstructRejectedOrOverLimitPaths()
    {
        var catalog = Read("other.txt;txt;1;999;1\nmatch.txt;txt;1;0;1\nmatch.txt;txt;1;999;1\n");
        Assert.Single(_engine.Search([catalog], new() { Text = "match", MaxResults = 1 }));
        Assert.Throws<InvalidDataException>(() => _engine.Search([catalog], new() { Text = "other" }));
    }

    [Fact]
    public void RegexMatchesCaseInsensitively()
    {
        var catalog = Read("Report42.PDF;pdf;1;0;1\nReportX.pdf;pdf;1;0;1\n");
        Assert.Single(_engine.Search([catalog], new() { Text = @"^report\d+\.pdf$", UseRegex = true }));
    }

    [Fact]
    public void RegexTimeoutPropagates()
    {
        var catalog = Read(new string('a', 100_000) + "!;txt;1;0;1\n");
        Assert.Throws<RegexMatchTimeoutException>(() => _engine.Search([catalog], new()
        {
            Text = "^(a+)+$", UseRegex = true, RegexTimeout = TimeSpan.FromMilliseconds(1)
        }));
    }

    [Fact]
    public void RejectsInvalidQueries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Search([], new() { MaxResults = -1 }));
        Assert.Throws<ArgumentException>(() => _engine.Search([], new() { MinSize = 2, MaxSize = 1 }));
        Assert.Throws<ArgumentException>(() => _engine.Search([], new()
        {
            ModifiedFrom = DateTimeOffset.UnixEpoch.AddDays(1), ModifiedTo = DateTimeOffset.UnixEpoch
        }));
        Assert.ThrowsAny<ArgumentException>(() => _engine.Search([], new() { UseRegex = true, Text = "[" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Search([], new()
        {
            UseRegex = true, RegexTimeout = Regex.InfiniteMatchTimeout
        }));
    }

    [Fact]
    public void ZeroLimitDoesNotEnumerateCatalogs()
    {
        IEnumerable<FileCatalog> Unexpected()
        {
            throw new InvalidOperationException();
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        Assert.Empty(_engine.Search(Unexpected(), new() { MaxResults = 0 }));
    }

    [Fact]
    public void SupportsCancellationBeforeAndDuringSearch()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => _engine.Search([], new(), source.Token));

        using var during = new CancellationTokenSource();
        IEnumerable<FileCatalog> Catalogs()
        {
            yield return Read("a.txt;txt;1;0;1\n");
            during.Cancel();
            yield return Read("b.txt;txt;1;0;1\n");
        }
        Assert.Throws<OperationCanceledException>(() => _engine.Search(Catalogs(), new(), during.Token));
    }
    [Fact]
    public void SearchIgnoresCorruptPathsUntilResultIsSelected()
    {
        var catalog = Read(
            "loop-one;folder;0;2;1\\n" +
            "loop-two;folder;0;1;1\\n" +
            "broken.txt;txt;1;1;1\\n" +
            "good.txt;txt;1;0;1\\n" +
            "not-folder.txt;txt;1;4;1\\n");

        var safe = _engine.Search([catalog], new() { Text = "good" });
        Assert.Equal(@"C:\\Fixture\\good.txt", Assert.Single(safe).FullPath);
        Assert.Throws<InvalidDataException>(() =>
            _engine.Search([catalog], new() { Text = "broken" }));
        Assert.Throws<InvalidDataException>(() =>
            _engine.Search([catalog], new() { Text = "not-folder" }));
        Assert.Single(_engine.Search([catalog], new() { Text = "broken", MaxResults = 0 }) switch
        {
            { Count: 0 } => new[] { true },
            _ => Array.Empty<bool>()
        });
    }

}
