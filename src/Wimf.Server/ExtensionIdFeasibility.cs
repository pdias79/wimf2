using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Read-only feasibility analysis for per-catalog byte-sized extension IDs.</summary>
public static class ExtensionIdFeasibility
{
    public sealed record CatalogResult(string Name, long Entries, int DistinctExtensions,
        bool FitsInByte, long EntriesWithExtensions);
    public sealed record ExtensionIdReport(int CatalogCount, long Entries, int MaximumDistinctExtensions,
        bool AllFitInByte, CatalogResult[] Catalogs);

    public static Report Analyze(IReadOnlyList<Wimf.Catalog.Catalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var output = new CatalogResult[catalogs.Count];
        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            var extensions = new HashSet<string>(StringComparer.Ordinal);
            long valid = 0;
            long nonEmpty = 0;
            for (var id = 1; id <= catalog.RecordCount; id++)
            {
                if (!catalog.TryGetEntryData(id, out var entry)) continue;
                valid++;
                // Preserve the exact extension spelling. Case-folding would not be lossless.
                extensions.Add(entry.Extension);
                if (entry.Extension.Length > 0) nonEmpty++;
            }
            output[i] = new CatalogResult(catalog.Header.Name, valid,
                extensions.Count, extensions.Count <= byte.MaxValue + 1, nonEmpty);
        }
        return new Report(output.Length, output.Sum(c => c.Entries),
            output.Length == 0 ? 0 : output.Max(c => c.DistinctExtensions),
            output.All(c => c.FitsInByte), output);
    }
}
