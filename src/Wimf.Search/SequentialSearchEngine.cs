using System.Text.RegularExpressions;
using Wimf.Catalog;
using FileCatalog = Wimf.Catalog.Catalog;

namespace Wimf.Search;

public sealed record SearchResult(FileCatalog Catalog, CatalogEntry Entry, string FullPath);

/// <summary>Sequential reference implementation. Results follow catalog and record order.</summary>
public sealed class SequentialSearchEngine
{
    /// <summary>
    /// Searches files only. Invalid patterns, regex timeouts, cancellation and invalid
    /// paths on retained results propagate to the caller; no partial list is returned.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(
        IEnumerable<FileCatalog> catalogs, SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Text);
        ArgumentNullException.ThrowIfNull(query.Extensions);
        ArgumentOutOfRangeException.ThrowIfNegative(query.MaxResults);
        if (query.MinSize > query.MaxSize)
            throw new ArgumentException("Minimum size exceeds maximum size.", nameof(query));
        if (query.ModifiedFrom > query.ModifiedTo)
            throw new ArgumentException("Start date exceeds end date.", nameof(query));
        cancellationToken.ThrowIfCancellationRequested();

        Regex? regex = null;
        if (query.UseRegex)
        {
            if (query.RegexTimeout <= TimeSpan.Zero || query.RegexTimeout.TotalMilliseconds > int.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(query), "Regex timeout must be finite and positive.");
            regex = new Regex(query.Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, query.RegexTimeout);
        }
        // Regex patterns keep their syntax; separator-insensitive mode applies only
        // to literal words and quoted phrases.
        var words = query.UseRegex ? Array.Empty<string>() :
            SearchTerms.Parse(query.Text);
        if (query.IgnoreSeparators && !query.UseRegex)
            words = words.Select(RemoveSeparators).Where(word => word.Length > 0).ToArray();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in query.Extensions)
        {
            ArgumentNullException.ThrowIfNull(extension);
            extensions.Add(extension.TrimStart('.'));
        }
        var results = new List<SearchResult>();
        var hasExtensionFilter = extensions.Count != 0;
        var hasMinDate = query.ModifiedFrom.HasValue;
        var hasMaxDate = query.ModifiedTo.HasValue;
        var minDate = query.ModifiedFrom.GetValueOrDefault();
        var maxDate = query.ModifiedTo.GetValueOrDefault();
        var hasCancellation = cancellationToken.CanBeCanceled;
        var singleWord = words.Length == 1 ? words[0] : null;
        if (query.MaxResults == 0) return results;

        foreach (var catalog in catalogs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(catalog);
            // Walk slots directly so cancellation is checked even across malformed records.
            for (var id = 1; id <= catalog.RecordCount; id++)
            {
                if (hasCancellation) cancellationToken.ThrowIfCancellationRequested();
                if (!catalog.TryGetEntryData(id, out var entry) ||
                    (entry.IsDirectory && hasExtensionFilter) ||
                    (!entry.IsDirectory && hasExtensionFilter && !extensions.Contains(entry.Extension.TrimStart('.'))) ||
                    (!entry.IsDirectory && (entry.Size < query.MinSize || entry.Size > query.MaxSize)) ||
                    (hasMinDate && entry.ModifiedAt < minDate) ||
                    (hasMaxDate && entry.ModifiedAt > maxDate))
                    continue;
                var name = query.IgnoreSeparators && !query.UseRegex ? RemoveSeparators(entry.Name) : entry.Name;
                var matches = singleWord is not null
                    ? name.Contains(singleWord, StringComparison.OrdinalIgnoreCase)
                    : MatchesAllTerms(name, words);
                if (!matches || (regex is not null && !regex.IsMatch(entry.Name))) continue;
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new SearchResult(catalog, catalog.GetEntry(id)!, catalog.GetFullPath(id)));
                if (results.Count == query.MaxResults) return results;
            }
        }
        return results;
    }

    private static bool MatchesAllTerms(string name, string[] words)
    {
        foreach (var word in words)
            if (!name.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string RemoveSeparators(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '-'));
}
