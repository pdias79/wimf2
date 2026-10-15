namespace Wimf.Server;

/// <summary>Validate a request's optional catalog selection against one search snapshot.</summary>
public static class CatalogSelection
{
    public static int[] Resolve(int catalogCount, int[]? requested)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(catalogCount);
        if (requested is null) return Enumerable.Range(0, catalogCount).ToArray();
        if (requested.Length > 1000) throw new ArgumentException("Too many selected catalogs.");
        var seen = new HashSet<int>();
        foreach (var index in requested)
        {
            if (index < 0 || index >= catalogCount || !seen.Add(index))
                throw new ArgumentException("Invalid or duplicated catalog index.");
        }
        // Always search in source catalog order; selecting indices must not reorder results.
        return requested.Order().ToArray();
    }
}
