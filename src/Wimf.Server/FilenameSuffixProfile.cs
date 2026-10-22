using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Read-only estimate of lossless filename suffix splitting opportunities.</summary>
public static class FilenameSuffixProfile
{
    public sealed record CatalogFinding(string Name, long Entries, long FileEntries,
        long SuffixMatches, long NonMatchingExtensions, long PotentialUtf16Bytes);
    public sealed record SuffixReport(long Entries, long SuffixMatches,
        long PotentialUtf16Bytes, CatalogFinding[] Catalogs);

    public static SuffixReport Analyze(IReadOnlyList<Wimf.Catalog.Catalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var findings = new CatalogFinding[catalogs.Count];
        for (var c = 0; c < catalogs.Count; c++)
        {
            var catalog = catalogs[c];
            long valid = 0, files = 0, matches = 0, nonMatching = 0, saved = 0;
            for (var id = 1; id <= catalog.RecordCount; id++)
            {
                if (!catalog.TryGetEntryData(id, out var entry)) continue;
                valid++;
                if (entry.IsDirectory) continue;
                files++;
                var extension = entry.Extension;
                if (extension.Length == 0) continue;
                var name = entry.Name;
                var suffixLength = extension.Length + 1;
                if (name.Length > suffixLength && name[name.Length - suffixLength] == '.' &&
                    name.AsSpan(name.Length - extension.Length).Equals(extension.AsSpan(), StringComparison.Ordinal))
                {
                    matches++;
                    saved += 2L * suffixLength;
                }
                else nonMatching++;
            }
            findings[c] = new CatalogFinding(catalog.Header.Name, valid, files, matches, nonMatching, saved);
        }
        return new SuffixReport(findings.Sum(x => x.Entries),
            findings.Sum(x => x.SuffixMatches), findings.Sum(x => x.PotentialUtf16Bytes), findings);
    }
}
