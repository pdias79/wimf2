namespace Wimf.Catalog;

public sealed record CatalogHeader(
    string Name,
    string RootPath,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Extensions);

/// <summary>Public materialized entry, kept compatible with existing consumers.</summary>
public sealed record CatalogEntry(
    int Id,
    string Name,
    string Extension,
    ulong Size,
    int ParentId,
    DateTimeOffset ModifiedAt)
{
    public bool IsDirectory => string.Equals(Extension, "folder", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// In-array representation: no per-entry object, ID field or DateTimeOffset object.
/// A default value represents an invalid/missing legacy slot.
/// </summary>
public readonly record struct CatalogEntryData(
    string Name, string Extension, ulong Size, int ParentId, long ModifiedUnixDays)
{
    public bool IsDirectory => string.Equals(Extension, "folder", StringComparison.OrdinalIgnoreCase);
    public DateTimeOffset ModifiedAt => DateTimeOffset.FromUnixTimeSeconds(checked(ModifiedUnixDays * 86400));
}

public sealed record CatalogDiagnostic(int LineNumber, string Message);

public sealed class Catalog
{
    private readonly CatalogEntryData[] _entries;
    public CatalogHeader Header { get; }
    public IReadOnlyList<CatalogDiagnostic> Diagnostics { get; }
    public int RecordCount => _entries.Length - 1;

    internal Catalog(CatalogHeader header, CatalogEntryData[] entries, IReadOnlyList<CatalogDiagnostic> diagnostics)
    {
        Header = header;
        _entries = entries;
        Diagnostics = diagnostics;
    }

    /// <summary>Access a stored record without allocating a CatalogEntry object.</summary>
    public bool TryGetEntryData(int id, out CatalogEntryData data)
    {
        if (id > 0 && id < _entries.Length && _entries[id].Name is not null)
        {
            data = _entries[id];
            return true;
        }
        data = default;
        return false;
    }

    public CatalogEntry? GetEntry(int id) =>
        TryGetEntryData(id, out var data)
            ? new CatalogEntry(id, data.Name, data.Extension, data.Size, data.ParentId, data.ModifiedAt)
            : null;

    public string GetFullPath(int id)
    {
        if (!TryGetEntryData(id, out var entry))
            throw new ArgumentOutOfRangeException(nameof(id));
        var segments = new List<string> { entry.Name };
        var visited = new HashSet<int> { id };
        var parent = entry.ParentId;
        while (parent != 0)
        {
            if (!visited.Add(parent))
                throw new InvalidDataException($"Cycle in catalog at record {parent}.");
            if (!TryGetEntryData(parent, out var folder))
                throw new InvalidDataException($"Record {id} references missing parent {parent}.");
            if (!folder.IsDirectory)
                throw new InvalidDataException($"Record {parent} is not a directory.");
            segments.Add(folder.Name);
            parent = folder.ParentId;
        }
        segments.Reverse();
        // Catalog paths may come from Windows while running on Linux.
        var separator = Header.RootPath.Contains('\\') ? '\\' : '/';
        return Header.RootPath.TrimEnd('\\', '/') + separator + string.Join(separator, segments);
    }

    public IEnumerable<CatalogEntry> EnumerateEntries()
    {
        for (int i = 1; i < _entries.Length; i++)
            if (TryGetEntryData(i, out var data))
                yield return new CatalogEntry(i, data.Name, data.Extension, data.Size, data.ParentId, data.ModifiedAt);
    }
}
