using System.Text.Json.Serialization;

namespace Wimf.Server;

public sealed record HealthResponse(string Status, int CatalogCount);
public sealed record CatalogSummary(string Name, string RootPath, int RecordCount, int DiagnosticCount, DateTimeOffset CreatedAt);
public sealed record SearchRequest
{
    public string Text { get; init; } = "";
    public bool UseRegex { get; init; }
    public string[]? Extensions { get; init; }
    public ulong? MinSize { get; init; }
    public ulong? MaxSize { get; init; }
    public DateTimeOffset? ModifiedFrom { get; init; }
    public DateTimeOffset? ModifiedTo { get; init; }
    public int MaxResults { get; init; } = 100;
}
public sealed record SearchHit(string Catalog, int Id, string Name, string FullPath, string Extension, ulong Size, DateTimeOffset ModifiedAt);
public sealed record SearchResponse(int Count, SearchHit[] Results);
public sealed record ErrorResponse(string Error);
public sealed record RefreshStatus(int CatalogCount, int SourceCount, DateTimeOffset? LastRefresh);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CatalogSummary[]))]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(RefreshSummary))]
[JsonSerializable(typeof(RefreshStatus))]
public partial class WimfJsonContext : JsonSerializerContext;
