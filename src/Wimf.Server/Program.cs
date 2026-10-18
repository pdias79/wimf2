using System.Text.RegularExpressions;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

// Analyze a diagnostic SMB log without treating it as a complete change history.
if (args.Length > 0 && args[0] == "--watch-report")
{
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: --watch-report FILE");
        return 2;
    }
    try
    {
        var report = SmbJournalReport.Analyze(args[1]);
        Console.WriteLine($"[WATCH-REPORT] notifications={report.Notifications} | files={report.DistinctPaths} | directories={report.AffectedDirectories} | starts={report.SessionStarts} | ends={report.SessionEnds} | errors={report.Errors} | malformed={report.MalformedLines}");
        Console.WriteLine("[WATCH-REPORT] Diagnostic only; no completeness guarantee; full scan required for reconciliation.");
        foreach (var directory in report.DirectoryCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(20))
            Console.WriteLine($"[WATCH-DIR] count={directory.Value} | {directory.Key}");
        return report.Errors == 0 && report.MalformedLines == 0 && report.SessionStarts == report.SessionEnds ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine("[WATCH-REPORT-ERROR] " + ex.Message);
        return 2;
    }
}

// Experimental read-only SMB notification probe. Does not update catalogs.
if (args.Length > 0 && args[0] == "--watch-root")
{
    if ((args.Length != 2 && args.Length != 4) || string.IsNullOrWhiteSpace(args[1]) ||
        (args.Length == 4 && (args[2] != "--watch-log" || string.IsNullOrWhiteSpace(args[3]))))
    {
        Console.Error.WriteLine("Usage: --watch-root DIRECTORY [--watch-log FILE] (stop with Ctrl+C)");
        return 2;
    }

    try
    {
        var root = Path.GetFullPath(args[1]);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Directory not found: {root}");
        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        // Optional append-only diagnostic trail; never a complete change journal.
        using var log = args.Length == 4
            ? new StreamWriter(new FileStream(Path.GetFullPath(args[3]),
                FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
                FileOptions.WriteThrough)) { AutoFlush = true }
            : null;
        var logLock = new object();
        long errors = 0;
        void Log(string action, string path)
        {
            if (log is null) return;
            lock (logLock)
            {
                try
                {
                    // Base64 makes arbitrary file paths unambiguous in tab-separated records.
                    var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
                    log.WriteLine($"{DateTimeOffset.UtcNow:O}\t{action}\t{encoded}");
                }
                catch (IOException ex)
                {
                    Interlocked.Increment(ref errors);
                    Console.Error.WriteLine($"[WATCH-ERROR] Journal unavailable: {ex.Message}; full rescan required.");
                }
            }
        }
        long observed = 0;
        long retained = 0;
        // Time-based de-duplication is diagnostic only: it must never be used as
        // proof that no filesystem change happened.
        var recent = new Dictionary<(string Action, string Path), DateTimeOffset>();
        var sync = new object();
        var window = TimeSpan.FromMilliseconds(500);
        void Record(string action, string path)
        {
            Interlocked.Increment(ref observed);
            Log(action, path); // Keep raw notifications, including duplicates.
            var now = DateTimeOffset.UtcNow;
            var shouldPrint = false;
            lock (sync)
            {
                // Avoid unbounded growth during long-running monitoring.
                if (recent.Count > 10000)
                {
                    foreach (var expiredKey in recent.Where(item => now - item.Value > window)
                                 .Select(item => item.Key).ToArray())
                        recent.Remove(expiredKey);
                }
                var key = (action, OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path);
                if (!recent.TryGetValue(key, out var previous) || now - previous >= window)
                {
                    recent[key] = now;
                    shouldPrint = true;
                    Interlocked.Increment(ref retained);
                }
            }
            if (shouldPrint)
                Console.WriteLine($"[WATCH] {DateTimeOffset.Now:O} | {action} | {path}");
        }
        watcher.Created += (_, e) => Record("created", e.FullPath);
        watcher.Changed += (_, e) => Record("changed", e.FullPath);
        watcher.Deleted += (_, e) => Record("deleted", e.FullPath);
        watcher.Renamed += (_, e) => Record("renamed", $"{e.OldFullPath} => {e.FullPath}");
        watcher.Error += (_, e) =>
        {
            Interlocked.Increment(ref errors);
            Log("error", e.GetException().GetType().Name);
            Console.Error.WriteLine($"[WATCH-ERROR] {e.GetException().Message}; notifications may have been lost; a full rescan is required.");
        };
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            Log("session-start", root);
            watcher.EnableRaisingEvents = true;
            Console.WriteLine($"[WATCH-START] {root} | observer only; no catalog changes; Ctrl+C to stop");
            try { await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token); }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            watcher.EnableRaisingEvents = false;
            Log("session-end", root);
            Console.WriteLine($"[WATCH-SUMMARY] events={Interlocked.Read(ref observed)} | grouped={Interlocked.Read(ref retained)} | duplicates={Interlocked.Read(ref observed) - Interlocked.Read(ref retained)} | errors={Interlocked.Read(ref errors)} | optional raw log, no replay");
            return Volatile.Read(ref errors) == 0 ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("[WATCH-ERROR] " + ex.Message);
        return 2;
    }
}

// Isolated comparison: never overwrites a configured production catalog.
if (args.Length > 0 && args[0] == "--benchmark-root")
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: --benchmark-root DIRECTORY [--workers 1,16,50] [--repeats 3] [--exclude PATH]...");
        return 2;
    }
    try
    {
        var modes = new[] { 1, 16, 50 };
        var repeats = 3;
        var exclusions = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--workers" when i + 1 < args.Length:
                    modes = args[++i].Split(',', StringSplitOptions.TrimEntries)
                        .Select(int.Parse).ToArray();
                    break;
                case "--repeats" when i + 1 < args.Length:
                    repeats = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--exclude" when i + 1 < args.Length:
                    exclusions.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException("Unknown benchmark argument: " + args[i]);
            }
        }
        if (repeats is < 1 or > 20 || modes.Length is < 1 or > 10 ||
            modes.Any(mode => mode is < 1 or > 50))
            throw new ArgumentException("Use 1-20 repeats and 1-10 worker modes between 1 and 50.");
        // Reverse the mode order every other round to reduce warm-cache bias.
        var measurements = modes.Distinct().ToDictionary(mode => mode, _ => new List<long>());
        var entryCounts = new HashSet<int>();
        var temp = Path.Combine(Path.GetTempPath(), "wimf-benchmark-" + Guid.NewGuid().ToString("N") + ".txt.gz");
        try
        {
            for (var round = 0; round < repeats; round++)
            {
                foreach (var mode in round % 2 == 0 ? modes : modes.Reverse())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var count = LegacyCatalogWriter.Build(args[1], temp, excludePaths: exclusions,
                        directoryPrefetch: mode);
                    watch.Stop();
                    entryCounts.Add(count);
                    measurements[mode].Add(watch.ElapsedMilliseconds);
                    Console.WriteLine($"[BENCH] workers={mode} | passage={round + 1}/{repeats} | total={watch.ElapsedMilliseconds} ms | elements={count}");
                    File.Delete(temp);
                }
            }
            var medians = new Dictionary<int, long>();
            foreach (var (mode, durations) in measurements)
            {
                var ordered = durations.OrderBy(value => value).ToArray();
                var median = ordered[(ordered.Length - 1) / 2];
                medians[mode] = median;
                Console.WriteLine($"[BENCH-SUMMARY] workers={mode} | min={ordered[0]} ms | mediane={median} ms | max={ordered[^1]} ms | moyenne={(long)durations.Average()} ms | essais={durations.Count}");
            }
            if (entryCounts.Count != 1)
            {
                Console.WriteLine("[BENCH-WARNING] Le nombre d’éléments varie entre les passages ; vérifier les modifications de l’arborescence avant de comparer les performances.");
                return 1;
            }
            // Prefer fewer workers when the median is within 5% of the fastest.
            // This is guidance only: never change the production configuration.
            var fastest = medians.Values.Min();
            var recommended = medians
                .Where(pair => pair.Value <= fastest * 1.05)
                .OrderBy(pair => pair.Key)
                .First();
            Console.WriteLine($"[BENCH-RECOMMENDATION] workers={recommended.Key} | mediane={recommended.Value} ms | meilleure_mediane={fastest} ms | tolerance=5% | appliquer_manuellement_dans_wimf.json");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                               or InvalidDataException or OverflowException or FormatException)
    {
        Console.Error.WriteLine("Benchmark failed: " + ex.Message);
        return 2;
    }
}

// Standalone catalog-creation command: no web daemon is started.
if (args.Length > 0 && args[0] == "--build-root")
{
    const string usage = "Usage: wimfd --build-root DIRECTORY --output CATALOG.txt[.gz] [--name NAME] [--update-existing] [--exclude PATH]...";
    if (args.Length < 4 || args[2] != "--output")
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
    try
    {
        string? name = null;
        var update = false;
        var exclusions = new List<string>();
        for (var i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name" when name is null && i + 1 < args.Length &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    name = args[++i];
                    break;
                case "--update-existing" when !update:
                    update = true;
                    break;
                case "--exclude" when i + 1 < args.Length &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    exclusions.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException("Unknown, duplicated or incomplete option: " + args[i]);
            }
        }
        if (update)
        {
            var summary = LegacyCatalogUpdater.Update(args[1], args[3], name,
                onSkipped: Console.Error.WriteLine, excludePaths: exclusions);
            Console.WriteLine($"Updated {args[3]}: {summary.Total} entries; " +
                $"{summary.Added} added, {summary.Modified} modified, {summary.Removed} removed, " +
                $"{summary.Unchanged} unchanged; {summary.Skipped} paths skipped.");
        }
        else
        {
            var skipped = 0;
            var count = LegacyCatalogWriter.Build(args[1], args[3], name,
                onSkipped: warning => { skipped++; Console.Error.WriteLine(warning); },
                excludePaths: exclusions);
            Console.WriteLine($"Created {args[3]} with {count} entries; {skipped} paths skipped.");
        }
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or OverflowException)
    {
        Console.Error.WriteLine($"Catalog creation failed: {ex.Message}");
        return 2;
    }
}

DaemonOptions options;
try
{
    options = DaemonOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                           or System.Text.Json.JsonException or InvalidDataException)
{
    Console.Error.WriteLine($"Invalid server configuration: {ex.Message}");
    Console.Error.WriteLine("Usage: wimfd [--config config.json] [--catalog local.gz]... [--source network.gz]... [--cache-dir directory] [--refresh-seconds N] [--enable-file-actions]");
    return 2;
}

var paths = options.Catalogs.ToList();
var sources = options.Sources.ToList();
var cacheDirectory = options.CacheDirectory;
var refreshSeconds = options.RefreshSeconds;
var enableFileActions = options.EnableFileActions;

LiveCatalogStore store;
try { store = new LiveCatalogStore(paths, sources, cacheDirectory, loadLocalFiles: true,
    allowMissingCatalogs: options.Indexing?.Select(job => job.Output),
    tolerateUnavailableCatalogs: true); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                           or System.Text.DecoderFallbackException or ArgumentException)
{
    Console.Error.WriteLine($"Unable to load local catalogs: {ex.Message}");
    return 2;
}
var initialRefresh = await store.RefreshAsync();
Console.WriteLine($"Loaded {initialRefresh.CatalogCount} catalog(s); {initialRefresh.FailedSources} source(s) unavailable, {initialRefresh.FailedLocalCatalogs} local catalog(s) unavailable.");

var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
// Avoid verbose ASP.NET Core request/endpoint logs during frequent UI polling.
// Warnings and errors remain visible; explicit [BENCH] console output is unaffected.
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WimfJsonContext.Default));

var app = builder.Build();

// The UI and API are deliberately bound to localhost until authentication is implemented.
app.MapGet("/", WebUi.GetPage);
var engine = new SequentialSearchEngine();

app.MapGet("/api/health", () =>
    Results.Json(new HealthResponse("ok", store.Snapshot.Length), WimfJsonContext.Default.HealthResponse));

app.MapGet("/api/catalogs", () =>
    Results.Json(store.Snapshot.Select((c, index) =>
        new CatalogSummary(index, c.Header.Name, c.Header.RootPath, c.RecordCount,
            c.Diagnostics.Count, c.Header.CreatedAt)).ToArray(),
        WimfJsonContext.Default.CatalogSummaryArray));

app.MapPost("/api/search", (SearchRequest request, CancellationToken cancellationToken) =>
{
    if (request.Text is null || request.Text.Length > 1024 ||
        request.MaxResults < 0 || request.MaxResults > 1000 ||
        request.Extensions is { Length: > 100 } ||
        request.CatalogIndices is { Length: > 1000 })
        return Results.Json(new ErrorResponse("Invalid query or result limit."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 400);

    try
    {
        var query = new SearchQuery
        {
            Text = request.Text,
            UseRegex = request.UseRegex,
            RegexTimeout = TimeSpan.FromMilliseconds(100),
            Extensions = request.Extensions ?? [],
            MinSize = request.MinSize,
            MaxSize = request.MaxSize,
            ModifiedFrom = request.ModifiedFrom,
            ModifiedTo = request.ModifiedTo,
            MaxResults = request.MaxResults
        };
        var snapshot = store.Snapshot;
        var indices = CatalogSelection.Resolve(snapshot.Length, request.CatalogIndices);
        var selected = indices.Select(index => snapshot[index]).ToArray();
        var results = engine.Search(selected, query, cancellationToken);
        var hits = results.Select(r => new SearchHit(
            r.Catalog.Header.Name, indices[Array.IndexOf(selected, r.Catalog)], r.Entry.Id, r.Entry.Name, r.FullPath,
            r.Entry.Extension, r.Entry.Size, r.Entry.ModifiedAt)).ToArray();
        return Results.Json(new SearchResponse(hits.Length, hits),
            WimfJsonContext.Default.SearchResponse);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(new ErrorResponse(ex.Message),
            WimfJsonContext.Default.ErrorResponse, statusCode: 400);
    }
    catch (RegexMatchTimeoutException)
    {
        return Results.Json(new ErrorResponse("Regex evaluation timed out."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 422);
    }
    catch (InvalidDataException)
    {
        return Results.Json(new ErrorResponse("A matching catalog entry has an invalid path."),
            WimfJsonContext.Default.ErrorResponse, statusCode: 422);
    }
});

// File actions are explicitly opt-in: a website must not be allowed to activate
// local file operations merely because the daemon listens on 127.0.0.1.
app.MapPost("/api/file-action", (HttpContext context, FileActionRequest request) =>
{
    IResult Error(string message, int status) =>
        Results.Json(new ErrorResponse(message), WimfJsonContext.Default.ErrorResponse, statusCode: status);

    if (!enableFileActions)
        return Error("File actions are disabled. Restart with --enable-file-actions.", 403);

    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin") ||
        context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        return Error("File action requests must originate from the local Wimf page.", 403);

    if (request.Action is not ("open" or "reveal" or "copy"))
        return Error("Unknown file action.", 400);

    try
    {
        var path = LocalFileActions.Resolve(store.Snapshot, request.CatalogIndex, request.Id, request.FullPath);
        LocalFileActions.Execute(request.Action, path);
        return Results.Json(new FileActionResponse(true), WimfJsonContext.Default.FileActionResponse);
    }
    catch (FileNotFoundException) { return Error("The indexed file is no longer accessible.", 404); }
    catch (InvalidOperationException) { return Error("Search results are stale or opening this file is prohibited.", 409); }
    catch (PlatformNotSupportedException) { return Error("This action is unavailable in the current desktop session.", 501); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or UnauthorizedAccessException
                               or IOException or System.ComponentModel.Win32Exception)
    {
        return Error("Unable to perform this action on the indexed file.", 422);
    }
});

app.MapGet("/api/refresh-status", () =>
    Results.Json(new RefreshStatus(store.Snapshot.Length, store.SourceCount, store.LastRefresh,
        store.LastSummary?.FailedSources ?? 0, store.LastSummary?.FailedLocalCatalogs ?? 0),
        WimfJsonContext.Default.RefreshStatus));

app.MapPost("/api/refresh", async (CancellationToken cancellationToken) =>
{
    var summary = await store.RefreshAsync(cancellationToken);
    return Results.Json(summary, WimfJsonContext.Default.RefreshSummary);
});

if ((sources.Count > 0 || paths.Count > 0) && refreshSeconds > 0)
{
    var shutdown = app.Lifetime.ApplicationStopping;
    _ = Task.Run(async () =>
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(refreshSeconds));
            while (await timer.WaitForNextTickAsync(shutdown))
            {
                try { await store.RefreshAsync(shutdown); }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
                catch (Exception ex) { Console.Error.WriteLine($"Catalog refresh failed: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }, shutdown);
}

CatalogIndexScheduler? indexingScheduler = null;
app.MapGet("/api/indexing-status", () =>
    Results.Json(indexingScheduler?.Status ?? [], WimfJsonContext.Default.IndexingJobStatusArray));

app.MapGet("/api/indexing-history", () =>
    Results.Json(indexingScheduler?.History ?? [], WimfJsonContext.Default.IndexingRunHistoryEntryArray));

// Count entries only once per immutable catalog snapshot, not every UI poll.
var fileCounts = new System.Runtime.CompilerServices.ConditionalWeakTable<
    Wimf.Catalog.Catalog, System.Runtime.CompilerServices.StrongBox<int>>();
// Catalog management includes configured local files that have not yet been generated.
app.MapGet("/api/catalog-management", () =>
{
    var jobs = indexingScheduler?.Status ?? [];
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var managed = store.LocalCatalogStatus.Select(entry =>
    {
        var catalog = entry.Catalog;
        var index = Array.FindIndex(jobs, job => comparer.Equals(job.Output, entry.Path));
        var job = index >= 0 ? jobs[index] : null;
        return new ManagedCatalogStatus(
            catalog?.Header.Name ?? job?.Name ?? Path.GetFileName(entry.Path),
            entry.Path, catalog?.Header.RootPath ?? job?.Root,
            catalog is null ? null : fileCounts.GetValue(catalog,
                loaded => new System.Runtime.CompilerServices.StrongBox<int>(
                    loaded.EnumerateEntries().Count(e => e.Extension != "folder"))).Value,
            catalog?.Header.CreatedAt, catalog is not null,
            index >= 0 ? index : null, job?.State, job?.NextUpdate, job?.LastError,
            job?.CanStart ?? false, job?.ScannedEntries ?? 0, job?.StartedAt,
            job?.LastSuccess, job?.SkippedEntries ?? 0, job?.SkippedDetails,
            job?.EffectiveExclusions, job?.QueuedAt, job?.ScanMetrics, job?.EnumeratedDirectories ?? 0);
    }).ToArray();
    return Results.Json(managed, WimfJsonContext.Default.ManagedCatalogStatusArray);
});

// Download the complete server-side TXT report, without exposing arbitrary disk paths.
app.MapGet("/api/indexing/{index:int}/skipped-report", (int index) =>
{
    var stream = indexingScheduler?.OpenSkippedReport(index);
    if (stream is null) return Results.NotFound();
    return Results.File(stream, "text/plain; charset=utf-8",
        $"wimf2-elements-ignores-{index}.txt");
});

// Manual scans are restricted to requests originating from the local Wimf page.
app.MapPost("/api/indexing/{index:int}/start", (HttpContext context, int index) =>
{
    if (indexingScheduler is null || index < 0 || index >= indexingScheduler.Status.Length)
        return Results.NotFound();
    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin"))
        return Results.StatusCode(403);
    return indexingScheduler.TryQueueManual(index, app.Lifetime.ApplicationStopping)
        ? Results.StatusCode(202) : Results.StatusCode(409);
});

app.MapPost("/api/indexing/{index:int}/cancel", (HttpContext context, int index) =>
{
    if (indexingScheduler is null || index < 0 || index >= indexingScheduler.Status.Length)
        return Results.NotFound();
    var origin = context.Request.Headers.Origin.ToString();
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.Equals(origin, "http://127.0.0.1:5080", StringComparison.Ordinal) ||
        !string.Equals(context.Request.Host.Value, "127.0.0.1:5080", StringComparison.OrdinalIgnoreCase) ||
        (site.Length > 0 && site != "same-origin"))
        return Results.StatusCode(403);
    return indexingScheduler.TryCancel(index) ? Results.StatusCode(202) : Results.StatusCode(409);
});

if (options.Indexing is { Length: > 0 })
{
    var shutdown = app.Lifetime.ApplicationStopping;
    indexingScheduler = new CatalogIndexScheduler(options.Indexing,
        afterSuccess: async (job, token) =>
        {
            await store.RefreshLocalCatalogAsync(job.Output, token);
        });
    var scheduler = indexingScheduler;
    _ = Task.Run(async () =>
    {
        try { await scheduler.RunAsync(shutdown); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex) { Console.Error.WriteLine($"Indexing scheduler stopped: {ex.Message}"); }
    }, shutdown);
    Console.WriteLine($"Scheduled indexing configured for {options.Indexing.Length} catalog(s).");
}

// Monitoring is opt-in and observational: indexing continues to use full scans.
var smbWatchers = new List<SmbBackgroundWatcher>();
foreach (var root in options.WatchRoots ?? [])
{
    try
    {
        var watcher = new SmbBackgroundWatcher(root);
        watcher.Start();
        smbWatchers.Add(watcher);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"[SMB-WATCH-ERROR] Cannot monitor '{root}': {ex.Message}; full scans remain enabled.");
    }
}
app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (var watcher in smbWatchers) watcher.Dispose();
});

Console.WriteLine($"Wimf daemon loaded {store.Snapshot.Length} catalog(s). Listening on http://127.0.0.1:5080");
if (enableFileActions) Console.WriteLine("Local file actions enabled (requires an interactive desktop session).");
await app.RunAsync();
return 0;
