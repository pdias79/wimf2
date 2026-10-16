using System.Text.RegularExpressions;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

// Standalone catalog-creation command: no web daemon is started.
if (args.Length > 0 && args[0] == "--build-root")
{
    if (args.Length is not (4 or 6) || args[2] != "--output" ||
        (args.Length == 6 && args[4] != "--name"))
    {
        Console.Error.WriteLine("Usage: wimfd --build-root DIRECTORY --output CATALOG.txt[.gz] [--name NAME]");
        return 2;
    }
    try
    {
        var skipped = 0;
        var count = LegacyCatalogWriter.Build(args[1], args[3], args.Length == 6 ? args[5] : null,
            onSkipped: warning => { skipped++; Console.Error.WriteLine(warning); });
        Console.WriteLine($"Created {args[3]} with {count} entries; {skipped} inaccessible paths skipped.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                               or InvalidDataException or OverflowException)
    {
        Console.Error.WriteLine($"Catalog creation failed: {ex.Message}");
        return 2;
    }
}

var paths = new List<string>();
var sources = new List<string>();
string? cacheDirectory = null;
var refreshSeconds = 300;
var enableFileActions = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--enable-file-actions":
            enableFileActions = true;
            break;
        case "--catalog" when i + 1 < args.Length:
            paths.Add(args[++i]);
            break;
        case "--source" when i + 1 < args.Length:
            sources.Add(args[++i]);
            break;
        case "--cache-dir" when i + 1 < args.Length:
            cacheDirectory = args[++i];
            break;
        case "--refresh-seconds" when i + 1 < args.Length &&
            int.TryParse(args[i + 1], out var seconds) && seconds >= 0:
            refreshSeconds = seconds;
            i++;
            break;
        default:
            Console.Error.WriteLine("Usage: wimfd [--catalog local.gz]... [--source network.gz]... [--cache-dir directory] [--refresh-seconds N] [--enable-file-actions]");
            return 2;
    }
}

if (sources.Count > 0 && string.IsNullOrWhiteSpace(cacheDirectory))
{
    Console.Error.WriteLine("--cache-dir is required when --source is provided.");
    return 2;
}

var catalogs = new List<Wimf.Catalog.Catalog>();
foreach (var path in paths)
{
    try { catalogs.Add(LegacyCatalogReader.ReadFile(path)); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                               or System.Text.DecoderFallbackException or ArgumentException)
    {
        Console.Error.WriteLine($"Unable to load catalog '{path}': {ex.Message}");
        return 2;
    }
}

var store = new LiveCatalogStore(catalogs, sources, cacheDirectory);
var initialRefresh = await store.RefreshAsync();
Console.WriteLine($"Loaded {initialRefresh.CatalogCount} catalog(s); {initialRefresh.FailedSources} source(s) unavailable.");

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
    Results.Json(new RefreshStatus(store.Snapshot.Length, store.SourceCount, store.LastRefresh),
        WimfJsonContext.Default.RefreshStatus));

app.MapPost("/api/refresh", async (CancellationToken cancellationToken) =>
{
    var summary = await store.RefreshAsync(cancellationToken);
    return Results.Json(summary, WimfJsonContext.Default.RefreshSummary);
});

if (sources.Count > 0 && refreshSeconds > 0)
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

Console.WriteLine($"Wimf daemon loaded {store.Snapshot.Length} catalog(s). Listening on http://127.0.0.1:5080");
if (enableFileActions) Console.WriteLine("Local file actions enabled (requires an interactive desktop session).");
await app.RunAsync();
return 0;
