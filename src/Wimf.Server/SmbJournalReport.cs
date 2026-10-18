using System.Text;

namespace Wimf.Server;

/// <summary>Read-only analysis of the optional SMB watcher diagnostic log.</summary>
public static class SmbJournalReport
{
    public sealed record Result(long Notifications, int DistinctPaths, int AffectedDirectories,
        long SessionStarts, long SessionEnds, long Errors, long MalformedLines,
        IReadOnlyDictionary<string, int> DirectoryCounts);

    public static Result Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new HashSet<string>(comparer);
        var directories = new Dictionary<string, int>(comparer);
        long notifications = 0, starts = 0, ends = 0, errors = 0, malformed = 0;

        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split('\t');
            if (fields.Length != 3 ||
                !DateTimeOffset.TryParse(fields[0], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                malformed++;
                continue;
            }

            string decoded;
            try
            {
                decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(fields[2]));
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
            {
                malformed++;
                continue;
            }

            switch (fields[1])
            {
                case "session-start":
                    starts++;
                    break;
                case "session-end":
                    ends++;
                    break;
                case "error":
                    errors++;
                    break;
                case "created":
                case "changed":
                case "deleted":
                case "renamed":
                    notifications++;
                    // Renames store "old => new"; count both affected directories.
                    var paths = fields[1] == "renamed"
                        ? decoded.Split(" => ", 2, StringSplitOptions.None)
                        : [decoded];
                    foreach (var changed in paths)
                    {
                        files.Add(changed);
                        var parent = Path.GetDirectoryName(changed);
                        if (string.IsNullOrWhiteSpace(parent)) continue;
                        directories[parent] = directories.GetValueOrDefault(parent) + 1;
                    }
                    break;
                default:
                    malformed++;
                    break;
            }
        }

        return new Result(notifications, files.Count, directories.Count, starts, ends, errors,
            malformed, directories);
    }
}
