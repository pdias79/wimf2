using System.Text.RegularExpressions;
using Wimf.Catalog;
using Wimf.Search;
using Wimf.Server;

var paths = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] != "--catalog" || ++i >= args.Length)
    {
        Console.Error.WriteLine("Usage: wimfd [--catalog path.txt.gz]...");
        return 2;
    }
    paths.Add(args[i]);
}

var catalogs = new List<Wimf.Catalog.Catalog>();
foreach (var path in paths)
{
    try
    {
        catalogs.Add(LegacyCatalogReader.ReadFile(path));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                               or System.Text.DecoderFallbackException or ArgumentException)
    {
        Console.Error.WriteLine($"Unable to load catalog '{path}': {ex.Message}");
        return 2;
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
