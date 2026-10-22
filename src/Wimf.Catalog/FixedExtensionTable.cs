namespace Wimf.Catalog;

/// <summary>Reserved extension IDs for newly indexed catalogs. Comparison is case-insensitive.</summary>
public static class FixedExtensionTable
{
    public const int Capacity = 256;
    public static readonly string[] Reserved =
    [
        "", "folder", "pdf", "doc", "docx", "txt", "jpg", "jpeg", "gif",
        "png", "ppt", "pptx", "xls", "xlsx", "pps"
    ];

    public static HashSet<string> CreateSet() =>
        new(Reserved, StringComparer.OrdinalIgnoreCase);
}
