using Wimf.Catalog;

namespace Wimf.Server;

/// <summary>Immutable statistics generated once per loaded catalog snapshot.</summary>
public sealed record CatalogExtensionCount(string Extension, int Count);

public sealed record CatalogExtensionStatistics(int FileCount, CatalogExtensionCount[] Extensions)
{
    public static CatalogExtensionStatistics Calculate(Catalog catalog)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var entry in catalog.EnumerateEntries())
        {
            if (entry.Extension == "folder") continue;
            total++;
            var extension = string.IsNullOrEmpty(entry.Extension) ? "(sans extension)" : entry.Extension.ToLowerInvariant();
            counts[extension] = counts.GetValueOrDefault(extension) + 1;
        }
        return new CatalogExtensionStatistics(total,
            counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new CatalogExtensionCount(pair.Key, pair.Value)).ToArray());
    }
}
