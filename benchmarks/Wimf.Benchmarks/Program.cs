using System.Diagnostics;
using System.Globalization;
using System.Text;
using Wimf.Catalog;
using Wimf.Search;

var count = 100_000;
var iterations = 5;
var maxResults = 100;
string? existingCatalog = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--count" && i + 1 < args.Length &&
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
        Console.Error.WriteLine("Usage: --count N [--iterations N] [--max-results N] OR --catalog path [--iterations N] [--max-results N]");
        return 2;
    }
}

var process = Process.GetCurrentProcess();
long WorkingSet()
{
    process.Refresh();
    return process.WorkingSet64;
}

static double MiB(long bytes) => bytes / 1048576.0;
static string Duration(double ms) => ms.ToString("F3", CultureInfo.InvariantCulture);
static void Report(string label, double ms, long memory, int hits) =>
    Console.WriteLine($"{label,-28} {Duration(ms),12} {MiB(memory),15:F1} {hits,10}");

Console.WriteLine($"Wimf 2 sequential baseline | .NET {Environment.Version} | {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Iterations: {iterations}, max results: {maxResults}");
Console.WriteLine("Memory = process working set (MiB), not managed heap. Results depend on machine, file system and JIT.");
Console.WriteLine($"{"Operation",-28} {"Time ms",12} {"Working set MiB",15} {"Hits",10}");

var baselineMemory = WorkingSet();
var watch = Stopwatch.StartNew();
Wimf.Catalog.Catalog catalog;
if (existingCatalog is not null)
{
    catalog = LegacyCatalogReader.ReadFile(existingCatalog);
}
else
{
    using var stream = new SyntheticCatalogStream(count);
    catalog = LegacyCatalogReader.Read(stream, compressed: false);
}
watch.Stop();
Report("Load catalog", watch.Elapsed.TotalMilliseconds, WorkingSet(), catalog.RecordCount);
Console.WriteLine($"Record count: {catalog.RecordCount:N0}; load working-set delta: {MiB(WorkingSet() - baselineMemory):F1} MiB.");
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
    int hits = warmup.Count;
    for (var i = 0; i < iterations; i++)
    {
        var sw = Stopwatch.StartNew();
        var result = engine.Search([catalog], query);
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
        hits = result.Count;
    }
    Array.Sort(times);
    Report(name + " median", times[times.Length / 2], WorkingSet(), hits);
}
return 0;

/// <summary>
/// Generates a realistic WIMF catalog one UTF-8 record at a time, avoiding a second
/// multi-gigabyte source string or file. The reader still materializes its catalog.
/// </summary>
internal sealed class SyntheticCatalogStream : Stream
{
    private readonly int _count;
    private int _next;
    private byte[] _buffer = [];
    private int _offset;

    public SyntheticCatalogStream(int count) => _count = count;

    private byte[] NextLine()
    {
        if (_next == 0)
        {
            _next++;
            return Encoding.UTF8.GetBytes("[WIMF Catalog]\nSynthetic benchmark\nC:\\Synthetic\n0\npdf txt\n");
        }
        if (_next > _count) return [];
        var id = _next++;
        var name = id % 100_000 == 0 ? $"needle_rare_{id:D8}.pdf" : $"file_{id:D8}.{(id % 3 == 0 ? "pdf" : "txt")}";
        return Encoding.UTF8.GetBytes($"{name};{(id % 3 == 0 ? "pdf" : "txt")};{id * 17L};0;20000\n");
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
