using Wimf.Catalog;
using System.Text.Json.Serialization;

namespace Wimf.Server;

public sealed record HealthResponse(string Status, int CatalogCount, bool FileActionsEnabled = false);
public sealed record CatalogSummary(int Index, string Name, string RootPath, int RecordCount, int DiagnosticCount, DateTimeOffset CreatedAt);
public sealed record SearchRequest
{
    public string Text { get; init; } = "";
    public int[]? CatalogIndices { get; init; }
    public bool UseRegex { get; init; }
    public bool IgnoreSeparators { get; init; }
    public string[]? Extensions { get; init; }
    public ulong? MinSize { get; init; }
    public ulong? MaxSize { get; init; }
    public DateTimeOffset? ModifiedFrom { get; init; }
    public DateTimeOffset? ModifiedTo { get; init; }
    public int MaxResults { get; init; } = 100;
}
public sealed record SearchHit(string Catalog, int CatalogIndex, int Id, string Name, string FullPath, string Extension, ulong Size, DateTimeOffset ModifiedAt);
public sealed record SearchResponse(int Count, SearchHit[] Results, bool Truncated = false);
public sealed record ErrorResponse(string Error);
public sealed record FileActionRequest(string Action, int CatalogIndex, int Id, string FullPath);
public sealed record FileActionResponse(bool Success);
public sealed record ManagedCatalogStatus(string Name, string Path, string? Root,
    int? FileCount, DateTimeOffset? LastUpdate, bool Available,
    int? IndexingJobIndex, string? State, DateTimeOffset? NextUpdate,
    string? LastError, bool CanStart,
    int ScannedEntries = 0, DateTimeOffset? StartedAt = null,
    DateTimeOffset? LastSuccess = null, int SkippedEntries = 0, string[]? SkippedDetails = null,
    string[]? EffectiveExclusions = null, DateTimeOffset? QueuedAt = null,
    CatalogScanMetrics? ScanMetrics = null, int EnumeratedDirectories = 0,
    CatalogLockMarkerStatus? LockMarker = null,
    CatalogExtensionCount[]? ExtensionStatistics = null);
public sealed record RefreshStatus(int CatalogCount, int SourceCount, DateTimeOffset? LastRefresh,
    int FailedSources = 0, int FailedLocalCatalogs = 0);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CatalogSummary[]))]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(FileActionRequest))]
[JsonSerializable(typeof(FileActionResponse))]
[JsonSerializable(typeof(RefreshSummary))]
[JsonSerializable(typeof(RefreshStatus))]
[JsonSerializable(typeof(IndexingJobStatus[]))]
[JsonSerializable(typeof(CatalogScanMetrics))]
[JsonSerializable(typeof(IndexingRunHistoryEntry[]))]
[JsonSerializable(typeof(ManagedCatalogStatus[]))]
[JsonSerializable(typeof(CatalogMemoryBenchmark.Snapshot))]
[JsonSerializable(typeof(CatalogMemoryProfile.Report))]
[JsonSerializable(typeof(ExtensionIdFeasibilityReport))]
[JsonSerializable(typeof(ServerMemoryDiagnostics.Sample))]
public partial class WimfJsonContext : JsonSerializerContext;
