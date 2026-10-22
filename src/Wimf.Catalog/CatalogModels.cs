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
    // Store extension IDs separately to avoid an 8-byte reference per record.
    // All arrays are immutable after construction and indexed by legacy record ID.
    private readonly CompactEntry[] _entries;
    private readonly string[] _extensions;
    private readonly byte[]? _byteExtensionIds;
    private readonly ushort[]? _shortExtensionIds;
    private readonly int[]? _intExtensionIds;
    private readonly System.Collections.BitArray _splitSuffixes;

    private readonly record struct CompactEntry(string Name, ulong Size, int ParentId, int ModifiedUnixDays);

    public CatalogHeader Header { get; }
    public IReadOnlyList<CatalogDiagnostic> Diagnostics { get; }
    public int RecordCount => _entries.Length - 1;

    internal Catalog(CatalogHeader header, IReadOnlyList<CatalogEntryData> entries, IReadOnlyList<CatalogDiagnostic> diagnostics)
    {
        Header = header;
        Diagnostics = diagnostics;
        _entries = new CompactEntry[entries.Count];
        _splitSuffixes = new System.Collections.BitArray(entries.Count);
        var extensionLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var extensions = new List<string>(FixedExtensionTable.Reserved);
        for (var i = 0; i < extensions.Count; i++)
            extensionLookup.Add(extensions[i], i);
        // Begin with byte IDs. Widen only when the number of distinct extensions
        // exceeds the current tier; avoid a full int[] for common catalogs.
        byte[]? byteIds = new byte[entries.Count];
        ushort[]? shortIds = null;
        int[]? intIds = null;
        for (var i = 1; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Name is null) continue;
            var storedName = entry.Name;
            var suffixLength = entry.Extension.Length + 1;
            if (!entry.IsDirectory && entry.Extension.Length > 0 &&
                storedName.Length > suffixLength &&
                storedName[storedName.Length - suffixLength] == '.' &&
                storedName.AsSpan(storedName.Length - entry.Extension.Length)
                    .SequenceEqual(entry.Extension.AsSpan()))
            {
                storedName = storedName[..^suffixLength];
                _splitSuffixes[i] = true;
            }
            _entries[i] = new CompactEntry(storedName, entry.Size, entry.ParentId,
                checked((int)entry.ModifiedUnixDays));
            if (!extensionLookup.TryGetValue(entry.Extension, out var index))
            {
                index = extensions.Count;
                extensionLookup.Add(entry.Extension, index);
                extensions.Add(entry.Extension);
            }
            if (byteIds is not null && index > byte.MaxValue)
            {
                shortIds = new ushort[entries.Count];
                for (var j = 1; j < i; j++) shortIds[j] = byteIds[j];
                byteIds = null;
            }
            if (shortIds is not null && index > ushort.MaxValue)
            {
                intIds = new int[entries.Count];
                for (var j = 1; j < i; j++) intIds[j] = shortIds[j];
                shortIds = null;
            }
            if (byteIds is not null) byteIds[i] = (byte)index;
            else if (shortIds is not null) shortIds[i] = (ushort)index;
            else intIds![i] = index;
        }
        _extensions = extensions.ToArray();
        _byteExtensionIds = byteIds;
        _shortExtensionIds = shortIds;
        _intExtensionIds = intIds;
    }

    /// <summary>Builds compact storage directly from the parsed legacy fields.</summary>
    internal sealed class Builder
    {
        private readonly List<CompactEntry> _records = [default];
        private readonly List<int> _extensionIds = [0];
        private readonly List<bool> _suffixes = [false];
        private readonly List<string> _extensions = new(FixedExtensionTable.Reserved);
        private readonly Dictionary<string, int> _lookup = new(StringComparer.Ordinal);

        internal Builder()
        {
            for (var i = 0; i < _extensions.Count; i++)
                _lookup.Add(_extensions[i], i);
        }

        internal void AddInvalid()
        {
            _records.Add(default);
            _extensionIds.Add(0);
            _suffixes.Add(false);
        }

        internal void Add(ReadOnlySpan<char> name, string extension, ulong size, int parentId, int days)
        {
            var isDirectory = string.Equals(extension, "folder", StringComparison.OrdinalIgnoreCase);
            var suffixLength = extension.Length + 1;
            var split = !isDirectory && extension.Length > 0 && name.Length > suffixLength &&
                name[name.Length - suffixLength] == '.' &&
                name[^extension.Length..].SequenceEqual(extension.AsSpan());
            var stored = split ? name[..^suffixLength].ToString() : name.ToString();
            if (!_lookup.TryGetValue(extension, out var extensionId))
            {
                extensionId = _extensions.Count;
                _lookup.Add(extension, extensionId);
                _extensions.Add(extension);
            }
            _records.Add(new CompactEntry(stored, size, parentId, days));
            _extensionIds.Add(extensionId);
            _suffixes.Add(split);
        }

        internal void ValidateParents(List<CatalogDiagnostic> diagnostics)
        {
            for (var id = 1; id < _records.Count; id++)
            {
                var entry = _records[id];
                if (entry.Name is null || entry.ParentId == 0) continue;
                var parent = entry.ParentId;
                if (parent >= _records.Count || _records[parent].Name is null ||
                    !string.Equals(_extensions[_extensionIds[parent]], "folder",
                        StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(new CatalogDiagnostic(id + 5, $"Invalid parent reference {parent}."));
            }
        }

        internal Catalog Build(CatalogHeader header, IReadOnlyList<CatalogDiagnostic> diagnostics)
        {
            var count = _records.Count;
            var byteIds = _extensions.Count <= 256 ? new byte[count] : null;
            var shortIds = _extensions.Count > 256 && _extensions.Count <= 65536 ? new ushort[count] : null;
            var intIds = _extensions.Count > 65536 ? new int[count] : null;
            for (var id = 1; id < count; id++)
            {
                var extensionId = _extensionIds[id];
                if (byteIds is not null) byteIds[id] = checked((byte)extensionId);
                else if (shortIds is not null) shortIds[id] = checked((ushort)extensionId);
                else intIds![id] = extensionId;
            }
            return new Catalog(header, _records.ToArray(), _extensions.ToArray(),
                byteIds, shortIds, intIds, new System.Collections.BitArray(_suffixes.ToArray()), diagnostics);
        }
    }

    private Catalog(CatalogHeader header, CompactEntry[] entries, string[] extensions,
        byte[]? byteIds, ushort[]? shortIds, int[]? intIds,
        System.Collections.BitArray suffixes, IReadOnlyList<CatalogDiagnostic> diagnostics)
    {
        Header = header;
        Diagnostics = diagnostics;
        _entries = entries;
        _extensions = extensions;
        _byteExtensionIds = byteIds;
        _shortExtensionIds = shortIds;
        _intExtensionIds = intIds;
        _splitSuffixes = suffixes;
    }

    /// <summary>Fast search access without constructing complete filename strings.</summary>
    public bool TryGetSearchParts(int id, out CatalogEntryData data, out bool suffixSplit)
    {
        if (id > 0 && id < _entries.Length && _entries[id].Name is not null)
        {
            var entry = _entries[id];
            var extensionId = _byteExtensionIds is not null ? _byteExtensionIds[id] :
                _shortExtensionIds is not null ? _shortExtensionIds[id] : _intExtensionIds![id];
            data = new CatalogEntryData(entry.Name, _extensions[extensionId],
                entry.Size, entry.ParentId, entry.ModifiedUnixDays);
            suffixSplit = _splitSuffixes[id];
            return true;
        }
        data = default;
        suffixSplit = false;
        return false;
    }

    /// <summary>Access a record, reconstructing its public data without allocating a new object.</summary>
    public bool TryGetEntryData(int id, out CatalogEntryData data)
    {
        if (id > 0 && id < _entries.Length && _entries[id].Name is not null)
        {
            var entry = _entries[id];
            var extensionId = _byteExtensionIds is not null ? _byteExtensionIds[id] :
                _shortExtensionIds is not null ? _shortExtensionIds[id] : _intExtensionIds![id];
            var extension = _extensions[extensionId];
            var name = _splitSuffixes[id] ? string.Concat(entry.Name, ".", extension) : entry.Name;
            data = new CatalogEntryData(name, extension,
                entry.Size, entry.ParentId, entry.ModifiedUnixDays);
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
