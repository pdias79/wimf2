using System.Text.RegularExpressions;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

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
            job?.LastSuccess, job?.SkippedEntries ?? 0, job?.SkippedDetails);
    }).ToArray();
    return Results.Json(managed, WimfJsonContext.Default.ManagedCatalogStatusArray);
});

// Download the complete server-side TXT report, without exposing arbitrary disk paths.
app.MapGet("/api/indexing/{index:int}/skipped-report", (int index) =>
{
    var stream = indexingScheduler?.OpenSkippedReport(index);
    return stream is null
        ? Results.NotFound()
        : Results.File(stream, "text/plain; charset=utf-8",
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

if (options.Indexing is { Length: > 0 })
{
    var shutdown = app.Lifetime.ApplicationStopping;
    indexingScheduler = new CatalogIndexScheduler(options.Indexing,
        afterSuccess: async (job, token) =>
        {
            var summary = await store.RefreshAsync(token);
            Console.WriteLine($"Indexed catalog '{job.Output}' reloaded; " +
                $"{summary.CatalogCount} catalog(s) searchable.");
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

Console.WriteLine($"Wimf daemon loaded {store.Snapshot.Length} catalog(s). Listening on http://127.0.0.1:5080");
if (enableFileActions) Console.WriteLine("Local file actions enabled (requires an interactive desktop session).");
await app.RunAsync();
return 0;
