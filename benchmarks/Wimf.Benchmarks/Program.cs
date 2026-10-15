using System.Diagnostics;
using System.Globalization;
using System.Text;
using Wimf.Catalog;
using Wimf.Search;

var count = 100_000;
var iterations = 5;
var maxResults = 100;
string? existingCatalog = null;
var profileRefresh = false;
var folders = 0;
var depth = 8;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--profile-refresh")
        profileRefresh = true;
    else if (args[i] == "--folders" && i + 1 < args.Length &&
             int.TryParse(args[++i], out var parsedFolders) && parsedFolders >= 0)
        folders = parsedFolders;
    else if (args[i] == "--depth" && i + 1 < args.Length &&
             int.TryParse(args[++i], out var parsedDepth) && parsedDepth is >= 1 and <= 64)
        depth = parsedDepth;
    else if (args[i] == "--count" && i + 1 < args.Length &&
        int.TryParse(args[++i], out var parsedCount) && parsedCount is >= 1 and <= 20_000_000)
        count = parsedCount;
    else if (args[i] == "--iterations" && i + 1 < args.Length &&
             int.TryParse(args[++i], out var parsedIterations) && parsedIterations is >= 1 and <= 100)
        iterations = parsedIterations;
    else if (args[i] == "--max-results" && i + 1 < args.Length &&
             int.TryParse(args[++i], out var parsedMax) && parsedMax is >= 1 and <= 100_000)
        maxResults = parsedMax;
    else if (args[i] == "--catalog" && i + 1 < args.Length)
        existingCatalog = args[++i];
    else
    {
        Console.Error.WriteLine("Usage: [--count N --folders N --depth N | --catalog path] [--iterations N] [--max-results N] [--profile-refresh]");
        return 2;
    }
}

if ((existingCatalog is not null && (folders > 0 || depth != 8)) ||
    (existingCatalog is null && (folders >= count || (folders > 0 && depth > folders))))
{
    Console.Error.WriteLine("Folders require a synthetic catalog, with 0 <= folders < count and depth <= folders.");
    return 2;
}

var process = Process.GetCurrentProcess();
long WorkingSet()
{
    process.Refresh();
    return process.WorkingSet64;
}

static double MiB(long bytes) => bytes / 1048576.0;
static long Allocated() => GC.GetTotalAllocatedBytes(precise: true);
static (int Gen0, int Gen1, int Gen2) Collections() =>
    (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
static void PrintGc(string label, long allocatedBefore, (int Gen0, int Gen1, int Gen2) before)
{
    var after = Collections();
    Console.WriteLine($"{label}: allocated {MiB(Allocated() - allocatedBefore):F2} MiB; " +
        $"GC gen0={after.Gen0 - before.Gen0}, gen1={after.Gen1 - before.Gen1}, gen2={after.Gen2 - before.Gen2}");
}
static void PrintManaged(string label) =>
    Console.WriteLine($"{label}: managed heap {MiB(GC.GetTotalMemory(forceFullCollection: false)):F2} MiB; " +
        $"live heap bytes {MiB(GC.GetGCMemoryInfo().HeapSizeBytes):F2} MiB; " +
        $"fragmentation {MiB(GC.GetGCMemoryInfo().FragmentedBytes):F2} MiB.");

static string Duration(double ms) => ms.ToString("F3", CultureInfo.InvariantCulture);
static void Report(string label, double ms, long memory, int hits) =>
    Console.WriteLine($"{label,-28} {Duration(ms),12} {MiB(memory),15:F1} {hits,10}");

Console.WriteLine($"Wimf 2 sequential baseline | .NET {Environment.Version} | {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Iterations: {iterations}, max results: {maxResults}; synthetic folders: {folders}, max folder depth: {depth}");
Console.WriteLine("Memory = process working set (MiB), not managed heap. Results depend on machine, file system and JIT.");
Console.WriteLine($"{"Operation",-28} {"Time ms",12} {"Working set MiB",15} {"Hits",10}");

var baselineMemory = WorkingSet();
var loadAllocationStart = Allocated();
var loadGcStart = Collections();
var watch = Stopwatch.StartNew();
Wimf.Catalog.Catalog catalog;
if (existingCatalog is not null)
{
    catalog = LegacyCatalogReader.ReadFile(existingCatalog);
}
else
{
    using var stream = new SyntheticCatalogStream(count, folders, depth);
    catalog = LegacyCatalogReader.Read(stream, compressed: false);
}
watch.Stop();
Report("Load catalog", watch.Elapsed.TotalMilliseconds, WorkingSet(), catalog.RecordCount);
Console.WriteLine($"Record count: {catalog.RecordCount:N0}; load working-set delta: {MiB(WorkingSet() - baselineMemory):F1} MiB.");
PrintGc("Load catalog", loadAllocationStart, loadGcStart);
PrintManaged("After load");
Console.WriteLine("Warmup runs are excluded from search timings. No forced GC inside timed regions.");

var engine = new SequentialSearchEngine();
var queries = new (string Name, SearchQuery Query)[]
{
    ("No hits", new SearchQuery { Text = "not_existing_9e710be2", MaxResults = maxResults }),
    ("Rare filename", new SearchQuery { Text = "needle_rare", MaxResults = maxResults }),
    ("Common filename", new SearchQuery { Text = "file", MaxResults = maxResults }),
    ("Extension filter", new SearchQuery { Text = "file", Extensions = ["pdf"], MaxResults = maxResults }),
    ("Regex", new SearchQuery { Text = @"^file_\d+\.", UseRegex = true, MaxResults = maxResults })
};

foreach (var (name, query) in queries)
{
    var warmup = engine.Search([catalog], query);
    var times = new double[iterations];
    var allocations = new double[iterations];
    var gcBefore = Collections();
    int hits = warmup.Count;
    for (var i = 0; i < iterations; i++)
    {
        var beforeAlloc = Allocated();
        var sw = Stopwatch.StartNew();
        var result = engine.Search([catalog], query);
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
        allocations[i] = MiB(Allocated() - beforeAlloc);
        hits = result.Count;
    }
    Array.Sort(times);
    Report(name + " median", times[times.Length / 2], WorkingSet(), hits);
    Array.Sort(allocations);
    var gcAfter = Collections();
    Console.WriteLine($"  Allocated/search median: {allocations[allocations.Length / 2]:F4} MiB; " +
        $"collections across {iterations} iterations: " +
        $"gen0={gcAfter.Gen0 - gcBefore.Gen0}, gen1={gcAfter.Gen1 - gcBefore.Gen1}, gen2={gcAfter.Gen2 - gcBefore.Gen2}");
}
if (catalog.RecordCount > 0)
{
    // Independent of name matching, measure path reconstruction at different depths.
    // Each sample ID is known to be a valid file in synthetic mode; real catalogs may
    // have malformed slots or directories, so choose eligible IDs explicitly.
    var pathIds = new List<int>();
    if (existingCatalog is null)
    {
        var firstFile = folders + 1;
        var lastFile = catalog.RecordCount;
        pathIds.Add(firstFile);
        if (lastFile != firstFile) pathIds.Add(lastFile);
        if (folders > 0)
        {
            // File references rotate through all folder IDs; one path at deepest spine.
            var deepestFile = firstFile + (depth - 1);
            if (deepestFile <= lastFile && !pathIds.Contains(deepestFile))
                pathIds.Add(deepestFile);
        }
    }
    else
    {
        for (var id = 1; id <= catalog.RecordCount && pathIds.Count < 3; id++)
            if (catalog.TryGetEntryData(id, out var entry) && !entry.IsDirectory)
                pathIds.Add(id);
    }
    foreach (var id in pathIds)
    {
        var path = catalog.GetFullPath(id);
        var times = new double[iterations];
        var allocations = new double[iterations];
        const int pathRepeats = 1000;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var beforeAlloc = Allocated();
            var sw = Stopwatch.StartNew();
            string? last = null;
            for (var repeat = 0; repeat < pathRepeats; repeat++)
                last = catalog.GetFullPath(id);
            sw.Stop();
            times[iteration] = sw.Elapsed.TotalMilliseconds * 1000 / pathRepeats;
            allocations[iteration] = (double)(Allocated() - beforeAlloc) / pathRepeats;
            if (last != path) throw new InvalidOperationException("Path reconstruction is nondeterministic.");
            GC.KeepAlive(last);
        }
        Array.Sort(times);
        Array.Sort(allocations);
        var depthOfPath = path.Count(ch => ch == '\\\\' || ch == '/');
        Console.WriteLine($"  Path ID {id} (separators={depthOfPath}): median {times[iterations / 2]:F2} us, " +
            $"allocated {allocations[iterations / 2]:F0} bytes/call ({pathRepeats} calls/sample)");
    }
}
if (profileRefresh)
{
    // Simulates retention of an old catalog while reading a replacement.
    // This models the memory-pressure pattern, not the network/cache update itself.
    Console.WriteLine("Simulated refresh: keeping old catalog referenced while loading another copy.");
    var beforeRefreshWorkingSet = WorkingSet();
    var beforeRefreshAllocated = Allocated();
    var beforeRefreshGc = Collections();
    var refreshWatch = Stopwatch.StartNew();
    Wimf.Catalog.Catalog replacement;
    if (existingCatalog is not null)
        replacement = LegacyCatalogReader.ReadFile(existingCatalog);
    else
    {
        using var stream = new SyntheticCatalogStream(count, folders, depth);
        replacement = LegacyCatalogReader.Read(stream, compressed: false);
    }
    refreshWatch.Stop();
    GC.KeepAlive(catalog);
    Report("Second catalog load", refreshWatch.Elapsed.TotalMilliseconds, WorkingSet(), replacement.RecordCount);
    PrintGc("Second catalog load", beforeRefreshAllocated, beforeRefreshGc);
    PrintManaged("Both catalogs retained");
    Console.WriteLine($"Working-set change with both catalogs: {MiB(WorkingSet() - beforeRefreshWorkingSet):F2} MiB; " +
        $"process peak working set: {MiB(process.PeakWorkingSet64):F2} MiB.");
    GC.KeepAlive(replacement);
}
return 0;

/// <summary>
/// Generates a realistic WIMF catalog one UTF-8 record at a time, avoiding a second
/// multi-gigabyte source string or file. The reader still materializes its catalog.
/// </summary>
internal sealed class SyntheticCatalogStream : Stream
{
    private readonly int _count;
    private readonly int _folders;
    private readonly int _depth;
    private int _next;
    private byte[] _buffer = [];
    private int _offset;

    public SyntheticCatalogStream(int count, int folders = 0, int depth = 8)
    {
        _count = count;
        _folders = folders;
        _depth = depth;
    }

    private byte[] NextLine()
    {
        if (_next == 0)
        {
            _next++;
            return Encoding.UTF8.GetBytes("[WIMF Catalog]\nSynthetic benchmark\nC:\\Synthetic\n0\npdf txt\n");
        }
        if (_next > _count) return [];
        var id = _next++;
        if (id <= _folders)
        {
            // Depth-limited spine with branching children; parents precede children.
            var parent = id <= _depth ? id - 1 : 1 + ((id - _depth - 1) % _depth);
            return Encoding.UTF8.GetBytes($"folder_{id:D8};folder;0;{parent};20000\n");
        }
        var fileIndex = id - _folders;
        var parentId = _folders == 0 ? 0 : 1 + ((fileIndex - 1) % _folders);
        var extension = fileIndex % 3 == 0 ? "pdf" : "txt";
        var name = fileIndex % 100_000 == 0 ? $"needle_rare_{fileIndex:D8}.pdf" : $"file_{fileIndex:D8}.{extension}";
        return Encoding.UTF8.GetBytes($"{name};{extension};{fileIndex * 17L};{parentId};20000\n");
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> target)
    {
        var written = 0;
        while (!target.IsEmpty)
        {
            if (_offset == _buffer.Length)
            {
                _buffer = NextLine();
                _offset = 0;
                if (_buffer.Length == 0) break;
            }
            var take = Math.Min(target.Length, _buffer.Length - _offset);
            _buffer.AsSpan(_offset, take).CopyTo(target);
            target = target[take..];
            written += take;
            _offset += take;
        }
        return written;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
