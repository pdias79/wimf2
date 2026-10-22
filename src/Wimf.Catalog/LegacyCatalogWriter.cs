using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Wimf.Catalog;

/// <summary>Builds a legacy-compatible UTF-8 or gzip catalog from an existing directory tree.</summary>
public sealed record CatalogScanMetrics(long DirectoryEnumerationMilliseconds, long MetadataMilliseconds, long? OutputMilliseconds, int DirectoryCount, long DirectorySortMilliseconds = 0);

public static class LegacyCatalogWriter
{
    public static int Build(string rootPath, string outputPath, string? catalogName = null,
        CancellationToken cancellationToken = default, Action<string>? onSkipped = null,
        IEnumerable<string>? excludePaths = null, Action<int>? onProgress = null,
        Action<CatalogScanMetrics>? onMetrics = null, int directoryPrefetch = 1,
        Action<int>? onDirectoriesEnumerated = null, IEnumerable<string>? extensions = null)
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
        if (directoryPrefetch < 1 || directoryPrefetch > 50)
            throw new ArgumentOutOfRangeException(nameof(directoryPrefetch), "Directory prefetch must be between 1 and 50.");
        var nextId = 0;
        var extensionTable = FixedExtensionTable.CreateSet();
        var skippedExtensionLimit = 0;
        long enumerationTicks = 0, metadataTicks = 0, sortTicks = 0;
        var directoryCount = 0;
        var overallTimer = System.Diagnostics.Stopwatch.StartNew();
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
                // Single worker retains the legacy depth-first order. Concurrent workers
                // traverse breadth-first like the original Python Folder_Explorer.
                var queue = new Queue<(DirectoryInfo Folder, int Parent)>();
                if (directoryPrefetch == 1) stack.Push((new DirectoryInfo(root), 0));
                else queue.Enqueue((new DirectoryInfo(root), 0));
                // Bounded BFS lookahead keeps network requests in flight while the main
                // thread emits entries deterministically in discovery order.
                var prefetched = new Dictionary<string, Task<(FileSystemInfo[] Children, long EnumerationTicks, long SortTicks)>>();
                var enumeratedDirectories = 0;
                Task<(FileSystemInfo[] Children, long EnumerationTicks, long SortTicks)> Fetch(DirectoryInfo folder) =>
                    Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var start = System.Diagnostics.Stopwatch.GetTimestamp();
                        var children = folder.EnumerateFileSystemInfos().ToArray();
                        var enumerated = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                        start = System.Diagnostics.Stopwatch.GetTimestamp();
                        Array.Sort(children, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
                        var sorted = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                        onDirectoriesEnumerated?.Invoke(Interlocked.Increment(ref enumeratedDirectories));
                        return (children, enumerated, sorted);
                    }, cancellationToken);
                void Prefetch()
                {
                    if (directoryPrefetch == 1) return;
                    foreach (var pending in queue)
                    {
                        if (prefetched.Count >= directoryPrefetch) break;
                        if (!prefetched.ContainsKey(pending.Folder.FullName))
                            prefetched.Add(pending.Folder.FullName, Fetch(pending.Folder));
                    }
                }
                Prefetch();
                while (stack.Count > 0 || queue.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (folder, parent) = directoryPrefetch == 1 ? stack.Pop() : queue.Dequeue();
                    // File-system enumeration order is not stable. Sort by ordinal name to
                    // make generated record order reproducible on one filesystem.
                    FileSystemInfo[] children;
                    try
                    {
                        if (directoryPrefetch == 1)
                        {
                            var start = System.Diagnostics.Stopwatch.GetTimestamp();
                            children = folder.EnumerateFileSystemInfos().ToArray();
                            enumerationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                            start = System.Diagnostics.Stopwatch.GetTimestamp();
                            Array.Sort(children, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
                            sortTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                            onDirectoriesEnumerated?.Invoke(++enumeratedDirectories);
                        }
                        else
                        {
                            var task = prefetched.Remove(folder.FullName, out var queued)
                                ? queued : Fetch(folder);
                            var result = task.GetAwaiter().GetResult();
                            children = result.Children;
                            enumerationTicks += result.EnumerationTicks;
                            sortTicks += result.SortTicks;
                        }
                    }
                    catch (IOException firstError) when (parent != 0)
                    {
                        // An SMB enumeration can fail transiently while a different
                        // computer is modifying the share. Re-enumerate once before
                        // treating the folder as inaccessible.
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Thread.Sleep(150);
                            cancellationToken.ThrowIfCancellationRequested();
                            var start = System.Diagnostics.Stopwatch.GetTimestamp();
                            children = folder.EnumerateFileSystemInfos().ToArray();
                            enumerationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                            start = System.Diagnostics.Stopwatch.GetTimestamp();
                            Array.Sort(children, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
                            sortTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                            onDirectoriesEnumerated?.Invoke(++enumeratedDirectories);
                        }
                        catch (Exception retryError) when (retryError is IOException or
                            UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            onSkipped?.Invoke($"Skipped inaccessible directory: {folder.FullName} ({retryError.Message}; initial: {firstError.Message})");
                            Prefetch();
                            continue;
                        }
                    }
                    catch (Exception ex) when (parent != 0 &&
                        ex is UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        onSkipped?.Invoke($"Skipped inaccessible directory: {folder.FullName} ({ex.Message})");
                        Prefetch();
                        continue;
                    }
                    directoryCount++;
                    var descend = new List<(DirectoryInfo Folder, int Parent)>();
                    foreach (var child in children)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var childPath = child.FullName;
                        var childName = child.Name;
                        if (exclusions.ContainsFullPath(childPath)) continue;
                        if (string.Equals(childPath, output, pathComparison) ||
                            string.Equals(childPath, temporary, pathComparison)) continue;
                        // The legacy semicolon-separated format cannot encode these names.
                        // Skip the entry (and its descendants if it is a directory) rather
                        // than abandoning an entire disk scan.
                        if (childName.IndexOfAny([';', '\r', '\n']) >= 0)
                        {
                            onSkipped?.Invoke($"Skipped unsupported file name: {childPath}");
                            continue;
                        }
                        bool isDirectory;
                        string extension;
                        ulong size;
                        long days;
                        var metadataStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        try
                        {
                            var attributes = child.Attributes;
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                            isDirectory = (attributes & FileAttributes.Directory) != 0;
                            extension = isDirectory ? "folder" : Path.GetExtension(childName).TrimStart('.');
                            size = isDirectory ? 0UL : checked((ulong)((FileInfo)child).Length);
                            var modified = new DateTimeOffset(child.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                            days = Math.DivRem(modified, 86400, out var remainder);
                            if (modified < 0 && remainder != 0) days--;
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                        {
                            onSkipped?.Invoke($"Skipped inaccessible entry: {childPath} ({ex.Message})");
                            continue;
                        }
                        // Reserve common extensions, then admit new ones up to 256.
                        // Do not increment the record ID when skipping a file.
                        if (!isDirectory && !extensionTable.Contains(extension))
                        {
                            if (extensionTable.Count == FixedExtensionTable.Capacity)
                            {
                                skippedExtensionLimit++;
                                onSkipped?.Invoke($"Skipped extension table limit (256): {childPath} (extension '{extension}')");
                                continue;
                            }
                            extensionTable.Add(extension);
                        }
                        metadataTicks += System.Diagnostics.Stopwatch.GetTimestamp() - metadataStart;
                        writer.Write(childName);
                        writer.Write(';');
                        writer.Write(extension);
                        writer.Write(';');
                        writer.Write(size.ToString(CultureInfo.InvariantCulture));
                        writer.Write(';');
                        writer.Write(parent.ToString(CultureInfo.InvariantCulture));
                        writer.Write(';');
                        writer.WriteLine(days.ToString(CultureInfo.InvariantCulture));
                        var id = ++nextId;
                        if ((nextId & 255) == 0) onProgress?.Invoke(nextId);
                        if (isDirectory) descend.Add(((DirectoryInfo)child, id));
                    }
                    if (directoryPrefetch == 1)
                        for (var i = descend.Count - 1; i >= 0; i--) stack.Push(descend[i]);
                    else
                        foreach (var childDirectory in descend) queue.Enqueue(childDirectory);
                    Prefetch();
                }
                writer.Flush();
                file.Flush(flushToDisk: true);
                // Writer scopes close before committing the finished gzip/plain file.
                // Number of records is derived later via the reader.
            }
            if (extensions is not null)
            {
                var filtered = temporary + ".filtered";
                try
                {
                    nextId = CatalogExtensionFilter.Prune(temporary, filtered, extensions,
                        output.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
                    File.Move(filtered, temporary, overwrite: true);
                }
                finally { if (File.Exists(filtered)) File.Delete(filtered); }
            }
            File.Move(temporary, output, overwrite: true);
            onProgress?.Invoke(nextId);
            overallTimer.Stop();
            if (onMetrics is not null)
            {
                var enumerationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(0, enumerationTicks).TotalMilliseconds;
                var metadataMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(0, metadataTicks).TotalMilliseconds;
                var sortMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(0, sortTicks).TotalMilliseconds;
                onMetrics(new CatalogScanMetrics(enumerationMs, metadataMs,
                    directoryPrefetch == 1
                        ? Math.Max(0, (long)overallTimer.Elapsed.TotalMilliseconds - enumerationMs - metadataMs - sortMs)
                        : null, directoryCount, sortMs));
            }
            return nextId;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
