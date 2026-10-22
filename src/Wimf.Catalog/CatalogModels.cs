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
public readonly record struct CatalogEntryData
{
    // Unix calendar days fit in Int32 for all dates supported by DateTimeOffset.
    // Keeping this packed preserves an eight-byte saving per record (~75 MiB / 10M records).
    private readonly int _modifiedUnixDays;

    public string Name { get; }
    public string Extension { get; }
    public ulong Size { get; }
    public int ParentId { get; }
    public long ModifiedUnixDays => _modifiedUnixDays;

    public CatalogEntryData(string name, string extension, ulong size, int parentId, long modifiedUnixDays)
    {
        _modifiedUnixDays = checked((int)modifiedUnixDays);
        Name = name;
        Extension = extension;
        Size = size;
        ParentId = parentId;
    }

    public bool IsDirectory => string.Equals(Extension, "folder", StringComparison.OrdinalIgnoreCase);
    public DateTimeOffset ModifiedAt => DateTimeOffset.FromUnixTimeSeconds(checked((long)_modifiedUnixDays * 86400));
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

    /// <summary>
    /// Builds the final path in one allocation. A bounded parent traversal detects
    /// cycles without allocating a HashSet, then fills the output string backwards.
    /// </summary>
    public string GetFullPath(int id)
    {
        if (!TryGetEntryData(id, out _))
            throw new ArgumentOutOfRangeException(nameof(id));

        var root = Header.RootPath;
        // Catalog paths can originate on Windows even when Wimf runs on Linux.
        var separator = root.Contains('\\') ? '\\' : '/';
        var rootLength = root.AsSpan().TrimEnd("\\/").Length;
        var current = id;
        var segments = 0;
        var namesLength = 0;
        while (current != 0)
        {
            // An acyclic chain contains at most RecordCount distinct records.
            // A longer chain must contain a cycle, even if IDs jump backwards.
            if (!TryGetEntryData(current, out var entry))
                throw new InvalidDataException($"Record {id} references missing parent {current}.");
            if (++segments > RecordCount)
                throw new InvalidDataException($"Cycle in catalog at record {current}.");
            if (current != id && !entry.IsDirectory)
                throw new InvalidDataException($"Record {current} is not a directory.");
            namesLength = checked(namesLength + entry.Name.Length);
            current = entry.ParentId;
        }

        // Exactly one separator precedes each segment, including the file name.
        var totalLength = checked(rootLength + namesLength + segments);
        return string.Create(totalLength,
            (Catalog: this, Id: id, Root: root, RootLength: rootLength, Separator: separator),
            static (destination, state) =>
            {
                state.Root.AsSpan(0, state.RootLength).CopyTo(destination);
                destination[state.RootLength] = state.Separator;
                var end = destination.Length;
                var current = state.Id;
                while (current != 0)
                {
                    // Already validated by the first traversal; immutable slots.
                    state.Catalog.TryGetEntryData(current, out var entry);
                    end -= entry.Name.Length;
                    entry.Name.AsSpan().CopyTo(destination[end..]);
                    current = entry.ParentId;
                    if (current != 0)
                        destination[--end] = state.Separator;
                }
            });
    }

    public IEnumerable<CatalogEntry> EnumerateEntries()
    {
        for (int i = 1; i < _entries.Length; i++)
            if (TryGetEntryData(i, out var data))
                yield return new CatalogEntry(i, data.Name, data.Extension, data.Size, data.ParentId, data.ModifiedAt);
    }
}
