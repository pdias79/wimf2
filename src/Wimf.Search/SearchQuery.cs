namespace Wimf.Search;

/// <summary>Inclusive filters combined with AND. Empty text matches every file.</summary>
public sealed record SearchQuery
{
    /// <summary>Whitespace-separated terms (quoted phrases remain intact), all required in any order, or a regex pattern.</summary>
    public string Text { get; init; } = "";
    public bool UseRegex { get; init; }
    public bool IgnoreSeparators { get; init; }
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
    /// <summary>Allowed extensions, case insensitive, with or without a leading dot.</summary>
    public IReadOnlyCollection<string> Extensions { get; init; } = Array.Empty<string>();
    public ulong? MinSize { get; init; }
    public ulong? MaxSize { get; init; }
    public DateTimeOffset? ModifiedFrom { get; init; }
    public DateTimeOffset? ModifiedTo { get; init; }
    public int MaxResults { get; init; } = 1000;
}
