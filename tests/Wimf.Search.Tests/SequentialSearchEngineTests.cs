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
    public void MatchesAllWordsIgnoringCaseAndOrderIncludingDirectories()
    {
        var catalog = Read("REPORT annual;folder;0;0;1\nAnnual Report.PDF;PDF;10;1;2\nreport.txt;txt;20;0;3\n");
        var matches = _engine.Search([catalog], new() { Text = " report\tANNUAL " });
        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Entry.IsDirectory);
        var result = matches[1];
        Assert.Equal(2, result.Entry.Id);
        Assert.Same(catalog, result.Catalog);
        Assert.Equal(@"C:\Fixture\REPORT annual\Annual Report.PDF", result.FullPath);
    }

    [Fact]
    public void DirectoryNamesAreSearchableUnlessExtensionFilterIsActive()
    {
        var catalog = Read("Vacation Photos;folder;0;0;2\nphoto.jpg;jpg;10;1;2\nnotes.txt;txt;5;0;2\n");
        var dirs = _engine.Search([catalog], new() { Text = "Vacation" });
        Assert.True(Assert.Single(dirs).Entry.IsDirectory);
        Assert.Empty(_engine.Search([catalog], new() { Text = "Vacation", Extensions = ["jpg"] }));
    }

    [Fact]
    public void SeparatorInsensitiveQuotedPhraseMatchesAllFileNameVariants()
    {
        var catalog = Read("NEPDC71.txt;txt;1;0;2\nNEP---DC 71.txt;txt;1;0;2\nN-E--  P  DC7-1.txt;txt;1;0;2\nNEPDC72.txt;txt;1;0;2\n");
        var results = _engine.Search([catalog], new()
        {
            Text = "\"NE-P DC 71\"",
            IgnoreSeparators = true
        });
        Assert.Equal(new[] { "NEPDC71.txt", "NEP---DC 71.txt", "N-E--  P  DC7-1.txt" },
            results.Select(result => result.Entry.Name));
        Assert.Empty(_engine.Search([catalog], new() { Text = "\"NE-P DC 71\"" }));
    }

    [Fact]
    public void SeparatorInsensitiveModeDoesNotRewriteRegularExpressions()
    {
        var catalog = Read("NE-P DC 71.txt;txt;1;0;2\nNEPDC71.txt;txt;1;0;2\n");
        var results = _engine.Search([catalog], new()
        {
            Text = @"^NEPDC71",
            UseRegex = true,
            IgnoreSeparators = true
        });
        Assert.Equal("NEPDC71.txt", Assert.Single(results).Entry.Name);
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
    public void SingleMultipleAndEmptyTermsPreserveResultsAndOrder()
    {
        var catalog = Read("Annual Report.pdf;pdf;12;0;1\nreport draft.txt;txt;6;0;1\nnotes.txt;txt;3;0;1\n");
        Assert.Equal(new[] { "Annual Report.pdf", "report draft.txt" },
            _engine.Search([catalog], new() { Text = "report" }).Select(result => result.Entry.Name));
        Assert.Equal(new[] { "report draft.txt" },
            _engine.Search([catalog], new() { Text = "report draft" }).Select(result => result.Entry.Name));
        Assert.Equal(3, _engine.Search([catalog], new() { Text = "" }).Count);
        Assert.Empty(_engine.Search([catalog], new() { Text = "nonexistent" }));
    }

    [Fact]
    public void RegexMatchesCaseInsensitively()
    {
        var catalog = Read("Report42.PDF;pdf;1;0;1\nReportX.pdf;pdf;1;0;1\n");
        Assert.Single(_engine.Search([catalog], new() { Text = @"^report\d+\.pdf$", UseRegex = true }));
    }

    [Fact]
    public void RegexMatchesSyntheticBenchmarkStyleNames()
    {
        var catalog = Read("file_00000001.txt;txt;1;0;2\nfile_00000002.pdf;pdf;1;0;2\nnotes.txt;txt;1;0;2\n");
        var results = _engine.Search([catalog], new()
        {
            Text = @"^file_\d+\.",
            UseRegex = true,
            MaxResults = 1000
        });
        Assert.Equal(new[] { "file_00000001.txt", "file_00000002.pdf" },
            results.Select(result => result.Entry.Name));
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
            "loop-one;folder;0;2;1\n" +
            "loop-two;folder;0;1;1\n" +
            "broken.txt;txt;1;1;1\n" +
            "good.txt;txt;1;0;1\n" +
            "not-folder.txt;txt;1;4;1\n");

        var safe = _engine.Search([catalog], new() { Text = "good" });
        Assert.EndsWith("good.txt", Assert.Single(safe).FullPath);
        Assert.Throws<InvalidDataException>(() =>
            _engine.Search([catalog], new() { Text = "broken" }));
        Assert.Throws<InvalidDataException>(() =>
            _engine.Search([catalog], new() { Text = "not-folder" }));
        Assert.Empty(_engine.Search([catalog], new() { Text = "broken", MaxResults = 0 }));
    }

    [Fact]
    public void QuotedPhraseMatchesContiguousWordsInsteadOfSeparatedKeywords()
    {
        var catalog = Read("Annual Report.PDF;pdf;1;0;1\nAnnual 2026 Report.PDF;pdf;1;0;1\nReport Annual.PDF;pdf;1;0;1\n");
        var results = _engine.Search([catalog], new() { Text = "\"Annual Report\"" });
        Assert.Equal(["Annual Report.PDF"], results.Select(result => result.Entry.Name));
        Assert.Equal(3, _engine.Search([catalog], new() { Text = "Annual Report" }).Count);
    }

    [Fact]
    public void QuotesPreserveWhitespaceAndCombineWithUnquotedTermsInAnyOrder()
    {
        var catalog = Read("draft Annual Report final.pdf;pdf;1;0;1\nAnnual Report draft.pdf;pdf;1;0;1\nAnnual  Report draft.pdf;pdf;1;0;1\nReport Annual draft.pdf;pdf;1;0;1\n");
        var results = _engine.Search([catalog], new() { Text = "draft \"Annual Report\"" });
        Assert.Equal(["draft Annual Report final.pdf", "Annual Report draft.pdf"],
            results.Select(result => result.Entry.Name));
        var doubleSpaces = _engine.Search([catalog], new() { Text = "\"Annual  Report\"" });
        Assert.Equal("Annual  Report draft.pdf", Assert.Single(doubleSpaces).Entry.Name);
    }

    [Fact]
    public void QuotedTermParsingSupportsTabsEmptyQuotesAndCaseInsensitivity()
    {
        Assert.Equal(["first", "two three", "four"],
            SearchTerms.Parse("  first\t\"two three\"  \"\" four "));
        Assert.Equal(["foobar"], SearchTerms.Parse("foo\"bar\""));
        var catalog = Read("Annual Report.PDF;pdf;1;0;1\n");
        Assert.Single(_engine.Search([catalog], new() { Text = "\"annual report\"" }));
    }

    [Fact]
    public void UnclosedQuotesFailClearlyButRegexKeepsItsExistingSemantics()
    {
        Assert.Throws<ArgumentException>(() => _engine.Search([], new() { Text = "\"unfinished" }));
        var catalog = Read("Annual Report.PDF;pdf;1;0;1\n");
        Assert.Single(_engine.Search([catalog], new() { Text = "Annual.*Report", UseRegex = true }));
    }

}
