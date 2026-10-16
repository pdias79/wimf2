using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Wimf.Catalog;

/// <summary>Builds a legacy-compatible UTF-8 or gzip catalog from an existing directory tree.</summary>
public static class LegacyCatalogWriter
{
    public static int Build(string rootPath, string outputPath, string? catalogName = null,
        CancellationToken cancellationToken = default, Action<string>? onSkipped = null,
        IEnumerable<string>? excludePaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Source directory not found: {root}");
        var output = Path.GetFullPath(outputPath);
        var exclusions = new CatalogPathExclusions(root, excludePaths);
        // The destination may be within the indexed tree (including a disk root).
        // Omit both the existing output and the file being written to prevent self-indexing.
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (root.Contains('\n') || root.Contains('\r'))
            throw new ArgumentException("The source root contains a line break.", nameof(rootPath));

        var defaultName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        var name = string.IsNullOrWhiteSpace(catalogName) ?
            (string.IsNullOrWhiteSpace(defaultName) ? root : defaultName) : catalogName;
        if (name.Contains('\n') || name.Contains('\r'))
            throw new ArgumentException("The catalog name contains a line break.", nameof(catalogName));

        var outputDirectory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(outputDirectory);
        var temporary = Path.Combine(outputDirectory, "." + Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var nextId = 0;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = output.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true) : null)
            using (var writer = new StreamWriter((Stream?)gzip ?? file, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true))
            {
                writer.WriteLine("[WIMF Catalog]");
                writer.WriteLine(name);
                writer.WriteLine(root);
                writer.WriteLine(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                writer.WriteLine(); // Legacy extension-header field; reader accepts an empty list.

                var stack = new Stack<(DirectoryInfo Folder, int Parent)>();
                stack.Push((new DirectoryInfo(root), 0));
                while (stack.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (folder, parent) = stack.Pop();
                    // File-system enumeration order is not stable. Sort by ordinal name to
                    // make generated record order reproducible on one filesystem.
                    FileSystemInfo[] children;
                    try
                    {
                        children = folder.EnumerateFileSystemInfos()
                            .OrderBy(info => info.Name, StringComparer.Ordinal).ToArray();
                    }
                    catch (Exception ex) when (parent != 0 &&
                        ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                    {
                        onSkipped?.Invoke($"Skipped inaccessible directory: {folder.FullName} ({ex.Message})");
                        continue;
                    }
                    var descend = new List<(DirectoryInfo Folder, int Parent)>();
                    foreach (var child in children)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (exclusions.Contains(child.FullName)) continue;
                        if (string.Equals(child.FullName, output, pathComparison) ||
                            string.Equals(child.FullName, temporary, pathComparison)) continue;
                        // The legacy semicolon-separated format cannot encode these names.
                        // Skip the entry (and its descendants if it is a directory) rather
                        // than abandoning an entire disk scan.
                        if (child.Name.IndexOfAny([';', '\r', '\n']) >= 0)
                        {
                            onSkipped?.Invoke($"Skipped unsupported file name: {child.FullName}");
                            continue;
                        }
                        bool isDirectory;
                        string extension;
                        ulong size;
                        long days;
                        try
                        {
                            var attributes = child.Attributes;
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                            isDirectory = (attributes & FileAttributes.Directory) != 0;
                            extension = isDirectory ? "folder" : Path.GetExtension(child.Name).TrimStart('.');
                            size = isDirectory ? 0UL : checked((ulong)((FileInfo)child).Length);
                            var modified = new DateTimeOffset(child.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                            days = Math.DivRem(modified, 86400, out var remainder);
                            if (modified < 0 && remainder != 0) days--;
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                        {
                            onSkipped?.Invoke($"Skipped inaccessible entry: {child.FullName} ({ex.Message})");
                            continue;
                        }
                        writer.Write(child.Name);
                        writer.Write(';');
                        writer.Write(extension);
                        writer.Write(';');
                        writer.Write(size.ToString(CultureInfo.InvariantCulture));
                        writer.Write(';');
                        writer.Write(parent.ToString(CultureInfo.InvariantCulture));
                        writer.Write(';');
                        writer.WriteLine(days.ToString(CultureInfo.InvariantCulture));
                        var id = ++nextId;
                        if (isDirectory) descend.Add(((DirectoryInfo)child, id));
                    }
                    for (var i = descend.Count - 1; i >= 0; i--) stack.Push(descend[i]);
                }
                writer.Flush();
                file.Flush(flushToDisk: true);
                // Writer scopes close before committing the finished gzip/plain file.
                // Number of records is derived later via the reader.
            }
            File.Move(temporary, output, overwrite: true);
            return nextId;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
