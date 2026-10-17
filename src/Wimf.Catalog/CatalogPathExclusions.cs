namespace Wimf.Catalog;

/// <summary>
/// Normalizes explicit file/directory exclusions before scanning. An exclusion
/// matches exactly one path and, for a directory, everything below it.
/// </summary>
public sealed class CatalogPathExclusions
{
    private readonly string[] _paths;
    private readonly string[] _directoryPrefixes;
    private readonly StringComparison _comparison;
    private readonly string _root;

    public CatalogPathExclusions(string rootPath, IEnumerable<string>? exclusions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _paths = (exclusions ?? []).Select(Resolve).Distinct(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        _directoryPrefixes = _paths.Select(path => path + Path.DirectorySeparatorChar).ToArray();
    }

    public IReadOnlyList<string> Paths => _paths;

    private string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.IsPathFullyQualified(path) ? path : Path.Combine(_root, path)));
        if (string.Equals(full, _root, _comparison))
            throw new ArgumentException("Cannot exclude the indexed root directory.", nameof(path));
        if (!IsDescendant(full, _root))
            throw new ArgumentException($"Excluded path must be inside the indexed root: {path}", nameof(path));
        return full;
    }

    private bool IsDescendant(string path, string ancestor)
    {
        var prefix = Path.EndsInDirectorySeparator(ancestor) ? ancestor : ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, _comparison);
    }

    public bool Contains(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return ContainsFullPath(full);
    }

    /// <summary>
    /// Fast path for canonical absolute paths returned by FileSystemInfo.FullName.
    /// Callers must not pass relative paths or paths containing unresolved segments.
    /// </summary>
    internal bool ContainsFullPath(string fullPath)
    {
        for (var i = 0; i < _paths.Length; i++)
        {
            if (string.Equals(fullPath, _paths[i], _comparison) ||
                fullPath.StartsWith(_directoryPrefixes[i], _comparison))
                return true;
        }
        return false;
    }
}
