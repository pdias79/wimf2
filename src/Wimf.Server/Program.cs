using System.Text.RegularExpressions;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

var paths = new List<string>();
var sources = new List<string>();
string? cacheDirectory = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--catalog" when i + 1 < args.Length:
            paths.Add(args[++i]);
            break;
        case "--source" when i + 1 < args.Length:
            sources.Add(args[++i]);
            break;
        case "--cache-dir" when i + 1 < args.Length:
            cacheDirectory = args[++i];
            break;
        default:
            Console.Error.WriteLine("Usage: wimfd [--catalog local.gz]... [--source network.gz]... [--cache-dir directory]");
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

foreach (var source in sources)
{
    try
    {
        var (catalog, _) = CatalogCache.Load(source, cacheDirectory!, Console.WriteLine);
        catalogs.Add(catalog);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                               or System.Text.DecoderFallbackException or ArgumentException)
    {
        // Keep serving the catalog sources that are available.
        Console.Error.WriteLine($"Skipping unavailable source '{source}': {ex.Message}");
    }
}

var builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, WimfJsonContext.Default));

var app = builder.Build();

// The UI and API are deliberately bound to localhost until authentication is implemented.
app.MapGet("/", WebUi.GetPage);
var engine = new SequentialSearchEngine();

app.MapGet("/api/health", () =>
    Results.Json(new HealthResponse("ok", catalogs.Count), WimfJsonContext.Default.HealthResponse));

app.MapGet("/api/catalogs", () =>
    Results.Json(catalogs.Select(c =>
        new CatalogSummary(c.Header.Name, c.Header.RootPath, c.RecordCount,
            c.Diagnostics.Count, c.Header.CreatedAt)).ToArray(),
        WimfJsonContext.Default.CatalogSummaryArray));

app.MapPost("/api/search", (SearchRequest request, CancellationToken cancellationToken) =>
{
    if (request.Text is null || request.Text.Length > 1024 ||
        request.MaxResults < 0 || request.MaxResults > 1000 ||
        request.Extensions is { Length: > 100 })
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
        var results = engine.Search(catalogs, query, cancellationToken);
        var hits = results.Select(r => new SearchHit(
            r.Catalog.Header.Name, r.Entry.Id, r.Entry.Name, r.FullPath,
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

Console.WriteLine($"Wimf daemon loaded {catalogs.Count} catalog(s). Listening on http://127.0.0.1:5080");
await app.RunAsync();
return 0;
