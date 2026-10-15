namespace Wimf.Catalog;

public sealed record CatalogHeader(
    string Name,
    string RootPath,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Extensions);

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

public sealed record CatalogDiagnostic(int LineNumber, string Message);

public sealed class Catalog
{
    private readonly CatalogEntry?[] _entries;
    public CatalogHeader Header { get; }
    public IReadOnlyList<CatalogDiagnostic> Diagnostics { get; }
    public int RecordCount => _entries.Length - 1;

    internal Catalog(CatalogHeader header, CatalogEntry?[] entries, IReadOnlyList<CatalogDiagnostic> diagnostics)
    {
        Header = header;
        _entries = entries;
        Diagnostics = diagnostics;
    }

    public CatalogEntry? GetEntry(int id) =>
        id > 0 && id < _entries.Length ? _entries[id] : null;

    public string GetFullPath(int id)
    {
        var entry = GetEntry(id) ?? throw new ArgumentOutOfRangeException(nameof(id));
        var segments = new List<string> { entry.Name };
        var visited = new HashSet<int> { id };
        var parent = entry.ParentId;
        while (parent != 0)
        {
            if (!visited.Add(parent))
                throw new InvalidDataException($"Cycle in catalog at record {parent}.");
            var folder = GetEntry(parent) ?? throw new InvalidDataException(
                $"Record {id} references missing parent {parent}.");
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
            if (_entries[i] is { } entry)
                yield return entry;
    }
}
